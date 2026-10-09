using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.Models.Mapping;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Library;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// Proposes a provider game for each game without metadata, from the title read in its file name (the rules of
    /// <see cref="TitleMatching"/>). The proposals are only sent to the server for the checked games.
    /// </summary>
    public partial class AutoMatchUserControl : UserControl
    {
        private readonly ObservableCollection<AutoMatchItem> items = new();
        internal ObservableCollection<AutoMatchItem> Items => items;
        private CancellationTokenSource? analysis;

        public AutoMatchUserControl()
        {
            InitializeComponent();
            uiItems.ItemsSource = items;
            Loaded += async (_, _) =>
            {
                Focus();
                await LoadProviders();
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
            UpdateSelection();
        }

        private async Task LoadProviders()
        {
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/metadata/providers");
                MetadataProviderDto[] providers = (JsonSerializer.Deserialize<MetadataProviderDto[]>(json) ?? Array.Empty<MetadataProviderDto>())
                    .Where(p => p.Enabled != false).OrderByDescending(p => p.Priority).ToArray();
                uiProvider.ItemsSource = providers;
                uiProvider.SelectedIndex = providers.Length > 0 ? 0 : -1;
                uiAnalyze.IsEnabled = providers.Length > 0;
                if (providers.Length == 0)
                    uiStatus.Text = Loc.T("No metadata provider is enabled on the server.");
            }
            catch (Exception ex)
            {
                uiAnalyze.IsEnabled = false;
                uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }

        private async void Analyze_Click(object? sender, RoutedEventArgs e)
        {
            if (uiProvider.SelectedItem is not MetadataProviderDto provider || string.IsNullOrEmpty(provider.Slug))
                return;
            analysis?.Cancel();
            analysis = new CancellationTokenSource();
            CancellationToken token = analysis.Token;
            items.Clear();
            UpdateSelection();
            SetBusy(true, Loc.T("Loading the library..."));
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?limit=-1");
                Game[] games = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                bool doubtful = uiDoubtful.IsChecked == true;
                List<AutoMatchItem> todo = new();
                foreach (Game game in games.Where(g => !string.IsNullOrEmpty(g.Path)))
                {
                    GameMetadata? mapped = game.ProviderMetadata?.FirstOrDefault(m => m.ProviderSlug == provider.Slug);
                    bool unmapped = game.ProviderMetadata != null ? mapped == null : string.IsNullOrEmpty(game.Metadata?.Title);
                    double currentScore = 0;
                    if (!unmapped)
                    {
                        if (!doubtful)
                            continue;
                        string? title = mapped?.Title ?? game.Metadata?.Title;
                        currentScore = TitleMatching.Score(game.Path, new TitleCandidate(title ?? "", mapped?.ReleaseDate ?? game.Metadata?.ReleaseDate));
                        if (currentScore >= TitleMatching.MinimumScore)
                            continue;
                    }
                    todo.Add(new AutoMatchItem(game, mapped?.ProviderDataId, unmapped ? null : mapped?.Title ?? game.Metadata?.Title, currentScore));
                }
                if (todo.Count == 0)
                {
                    SetBusy(false, doubtful ? Loc.T("Every game matches its file name.") : Loc.T("Every game is mapped."));
                    return;
                }
                // A few searches at a time: each one asks the provider through the server
                int done = 0;
                using SemaphoreSlim gate = new(3);
                await Task.WhenAll(todo.Select(async item =>
                {
                    await gate.WaitAsync(token);
                    try
                    {
                        await Propose(item, provider.Slug, token);
                    }
                    finally
                    {
                        gate.Release();
                    }
                    if (token.IsCancellationRequested)
                        return;
                    items.Add(item);
                    item.PropertyChanged += (_, _) => UpdateSelection();
                    UpdateSelection();
                    uiStatus.Text = Loc.F("Searching... {0} / {1}", ++done, todo.Count);
                }));
                // The sure ones first, then the best scores
                List<AutoMatchItem> sorted = items.OrderByDescending(i => i.IsConfident).ThenByDescending(i => i.HasProposal).ThenByDescending(i => i.Score).ToList();
                items.Clear();
                foreach (AutoMatchItem item in sorted)
                    items.Add(item);
                SetBusy(false, Loc.F("{0} proposals for {1} games", items.Count(i => i.HasProposal), items.Count));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetBusy(false, WebExceptionHelper.TryGetServerMessage(ex));
            }
            UpdateSelection();
        }

        private static async Task Propose(AutoMatchItem item, string slug, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // A provider id written in the file name ("(igdb-1942)") wins
            string? pinned = TitleMatching.PinnedId(item.Game.Path, slug);
            if (pinned != null)
            {
                if (pinned != item.CurrentProviderId)
                    item.SetProposal(new MinimalGame { ProviderSlug = slug, ProviderDataId = pinned, Title = Loc.F("{0} (ID in the file name)", item.SearchTerm) }, 1, true);
                return;
            }
            if (string.IsNullOrWhiteSpace(item.SearchTerm))
                return;
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/metadata/providers/{slug}/search?query={Uri.EscapeDataString(item.SearchTerm)}");
                MinimalGame[] results = JsonSerializer.Deserialize<MinimalGame[]>(json) ?? Array.Empty<MinimalGame>();
                TitleMatch? best = TitleMatching.Best(item.Game.Path, results.Select(r => new TitleCandidate(r.Title ?? "", r.ReleaseDate)).ToList());
                if (best == null)
                    return;
                MinimalGame proposal = results[best.Index];
                // A mapped game is only proposed a clearly better game
                if (item.CurrentTitle != null && (proposal.ProviderDataId == item.CurrentProviderId || best.Score < item.CurrentScore + 0.05))
                    return;
                item.SetProposal(proposal, best.Score, best.IsConfident);
            }
            catch (Exception ex)
            {
                item.Error = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }

        private void CheckSure_Click(object? sender, RoutedEventArgs e)
        {
            foreach (AutoMatchItem item in items)
                item.IsChecked = item.HasProposal && !item.IsDone && item.IsConfident;
        }

        private async void Apply_Click(object? sender, RoutedEventArgs e)
        {
            if (uiProvider.SelectedItem is not MetadataProviderDto provider)
                return;
            List<AutoMatchItem> checkedItems = items.Where(i => i.IsChecked && i.HasProposal && !i.IsDone).ToList();
            if (checkedItems.Count == 0)
                return;
            SetBusy(true, "");
            int mapped = 0, failed = 0;
            foreach (AutoMatchItem item in checkedItems)
            {
                uiStatus.Text = Loc.F("Mapping... {0} / {1}", mapped + failed + 1, checkedItems.Count);
                try
                {
                    UpdateGameDto update = new() { MappingRequests = new List<MapGameDto> { new() { ProviderSlug = provider.Slug!, ProviderDataId = item.Proposal!.ProviderDataId } } };
                    string json = await WebHelper.PutAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{item.Game.ID}", JsonSerializer.Serialize(update));
                    Game? game = JsonSerializer.Deserialize<Game>(json);
                    if (game != null)
                    {
                        InstallViewModel.Instance.RefreshGame(game);
                        MainWindowViewModel.Instance.Library?.RefreshGame(game);
                    }
                    item.MarkDone();
                    mapped++;
                }
                catch (Exception ex)
                {
                    item.Error = WebExceptionHelper.TryGetServerMessage(ex);
                    failed++;
                }
            }
            SetBusy(false, failed == 0 ? Loc.F("{0} games mapped", mapped) : Loc.F("{0} games mapped, {1} failed", mapped, failed));
            MainWindowViewModel.Instance.AppBarText = uiStatus.Text;
            UpdateSelection();
        }

        private void SetBusy(bool busy, string status)
        {
            uiLoading.IsVisible = uiLoading.IsActive = busy;
            uiAnalyze.IsEnabled = uiApply.IsEnabled = uiProvider.IsEnabled = !busy;
            uiStatus.Text = status;
        }

        private void UpdateSelection()
        {
            int count = items.Count(i => i.IsChecked && !i.IsDone);
            uiSelection.Text = Loc.F("{0} checked", count);
            uiApply.IsEnabled = count > 0 && !uiLoading.IsVisible;
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

        private void Close()
        {
            analysis?.Cancel();
            MainWindowViewModel.Instance.ClosePopup();
        }
    }

    /// <summary>A game of the library and the provider game proposed for it.</summary>
    internal class AutoMatchItem : ViewModelBase
    {
        public AutoMatchItem(Game game, string? currentProviderId, string? currentTitle, double currentScore)
        {
            Game = game;
            CurrentProviderId = currentProviderId;
            CurrentTitle = currentTitle;
            CurrentScore = currentScore;
            FileName = game.Path.Replace('\\', '/').Split('/').Last();
            SearchTerm = TitleMatching.SearchTerm(game.Path);
        }

        public Game Game { get; }
        public string FileName { get; }
        public string SearchTerm { get; }
        public string? CurrentProviderId { get; }
        public string? CurrentTitle { get; }
        public double CurrentScore { get; }
        public string CurrentText => CurrentTitle == null ? Loc.T("Not mapped") : Loc.F("Currently: {0}", CurrentTitle);

        public MinimalGame? Proposal { get; private set; }
        public double Score { get; private set; }
        public bool IsConfident { get; private set; }
        public bool HasProposal => Proposal != null && !IsDone;
        public bool IsUnsure => Proposal != null && !IsConfident && !IsDone;
        public string ProposalTitle => IsDone ? Loc.F("{0} ✓", Proposal?.Title) : Proposal?.Title ?? Error ?? Loc.T("No close game found");
        public string ProposalInfo => Proposal == null ? "" : string.Join(" · ", new[]
        {
            Loc.F("Match {0}", $"{Math.Round(Score * 100):0} %"),
            Proposal.ReleaseDate?.Year.ToString(),
            Loc.F("ID: {0}", Proposal.ProviderDataId),
        }.Where(s => !string.IsNullOrEmpty(s)));

        private string? error;
        public string? Error
        {
            get => error;
            set { error = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProposalTitle)); }
        }

        private bool isChecked;
        public bool IsChecked
        {
            get => isChecked;
            set { if (isChecked == value) return; isChecked = value; OnPropertyChanged(); }
        }

        public bool IsDone { get; private set; }

        public void SetProposal(MinimalGame proposal, double score, bool confident)
        {
            Proposal = proposal;
            Score = score;
            IsConfident = confident;
            // Pre-checked when sure, except to replace an existing mapping
            isChecked = confident && CurrentTitle == null;
            foreach (string name in new[] { nameof(Proposal), nameof(Score), nameof(IsConfident), nameof(IsUnsure), nameof(HasProposal), nameof(ProposalTitle), nameof(ProposalInfo), nameof(IsChecked) })
                OnPropertyChanged(name);
        }

        public void MarkDone()
        {
            IsDone = true;
            IsChecked = false;
            foreach (string name in new[] { nameof(IsDone), nameof(HasProposal), nameof(IsUnsure), nameof(ProposalTitle) })
                OnPropertyChanged(name);
        }
    }
}
