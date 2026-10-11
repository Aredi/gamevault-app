using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Library;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// "What to play tonight?": proposes one game of the library at random, the forgotten ones more likely, narrowed by
    /// a few filters (installed, never played, not finished, short, genre).
    /// </summary>
    public partial class TonightUserControl : UserControl
    {
        private readonly Random random = new();
        private readonly List<int> proposed = new();
        private Dictionary<int, Game> games = new();
        private List<TonightCandidate> candidates = new();
        private Game? shown;
        private bool loaded;

        public TonightUserControl()
        {
            InitializeComponent();
            // Installed games first when there are some: they start at once
            uiInstalledOnly.IsChecked = InstallViewModel.Instance.InstalledGames.Any(g => g.Key != null);
            Loaded += async (_, _) =>
            {
                Focus();
                await LoadAsync();
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    MainWindowViewModel.Instance.ClosePopup();
                }
                else if (e.Key is Key.Space or Key.Right && loaded)
                {
                    e.Handled = true;
                    Propose();
                }
            };
        }

        internal Game? Shown => shown;
        internal int MatchingCount => TonightPicker.Filter(candidates, CurrentFilter()).Count;

        /// <summary>The library, the player's states (completed...) and play times.</summary>
        internal async Task LoadAsync()
        {
            var all = new Dictionary<int, Game>();
            foreach (var install in InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null))
                all[install.Key.ID] = install.Key;
            var states = new Dictionary<int, string?>();
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?limit=-1");
                foreach (Game game in JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>())
                    if (game.DeletedAt == null)
                        all.TryAdd(game.ID, game);
                User? me = JsonSerializer.Deserialize<User>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/users/me"));
                foreach (Progress progress in me?.Progresses ?? Array.Empty<Progress>())
                    if (progress.Game != null)
                        states[progress.Game.ID] = progress.State;
            }
            catch (Exception ex)
            {
                // Offline: the installed games are enough
                Log.Ignored(ex);
            }
            games = all;
            var installed = InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null).Select(g => g.Key.ID).ToHashSet();
            var records = InstallViewModel.Instance.PlayRecords;
            candidates = all.Values.Select(g =>
            {
                records.TryGetValue(g.ID, out var record);
                return new TonightCandidate(g.ID, installed.Contains(g.ID), record?.MinutesPlayed ?? 0, record?.LastPlayedAt?.ToUniversalTime(),
                    states.GetValueOrDefault(g.ID), g.Metadata?.AveragePlaytime, g.Metadata?.Genres?.Select(x => x.Name).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>());
            }).ToList();

            var genres = candidates.SelectMany(c => c.Genres).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToList();
            genres.Insert(0, Loc.T("All genres"));
            uiGenre.ItemsSource = genres;
            uiGenre.SelectedIndex = 0;
            uiLoading.IsVisible = false;
            loaded = true;
            Propose();
        }

        private TonightFilter CurrentFilter() => new(
            uiInstalledOnly.IsChecked == true, uiNeverPlayed.IsChecked == true, uiNotFinished.IsChecked == true, uiShort.IsChecked == true,
            uiGenre.SelectedIndex > 0 ? uiGenre.SelectedItem as string : null);

        /// <summary>Draws a game among those matching the filters.</summary>
        internal void Propose()
        {
            var pool = TonightPicker.Filter(candidates, CurrentFilter());
            uiCount.Text = Loc.F(pool.Count == 1 ? "{0} matching game" : "{0} matching games", pool.Count);
            uiAnother.IsEnabled = pool.Count > 1;
            DateTime now = DateTime.UtcNow;
            TonightCandidate? pick = TonightPicker.Pick(pool, random, proposed.TakeLast(Math.Max(0, Math.Min(5, pool.Count - 1))).ToList(), now);
            uiCard.IsVisible = pick != null;
            uiEmpty.IsVisible = pick == null;
            if (pick == null)
            {
                shown = null;
                uiEmpty.Text = Loc.T("No game matches these filters.");
                return;
            }
            proposed.Add(pick.Id);
            shown = games[pick.Id];
            uiArt.Data = shown;
            uiCover.Data = shown;
            uiTitle.Text = shown.Metadata?.Title ?? shown.Title;
            uiReason.Text = TonightPicker.IsNeverPlayed(pick) ? Loc.T("NEVER PLAYED")
                : pick.LastPlayedUtc is DateTime last && (now - last).TotalDays >= 60 ? Loc.T("SET ASIDE FOR A WHILE")
                : Loc.T("WHY NOT TONIGHT?");
            var info = new List<string>();
            if (pick.Genres.Count > 0)
                info.Add(string.Join(", ", pick.Genres.Take(3)));
            if (pick.AverageMinutes is > 0)
                info.Add(Loc.F("about {0} to finish", new GameTimeConverter().Convert(pick.AverageMinutes, typeof(string), null, CultureInfo.CurrentCulture)));
            if (pick.LastPlayedUtc is DateTime played)
                info.Add(Loc.F("last session {0}", RelativeDate.Format(played)));
            uiInfo.Text = string.Join(" · ", info);
            uiDescription.Text = (string?)new PlainTextConverter().Convert(shown.Metadata?.Description, typeof(string), null, CultureInfo.CurrentCulture);
            uiPlayText.Text = pick.Installed ? Loc.T("Play") : Loc.T("Install");
            uiPlayIcon.Data = (Avalonia.Media.Geometry?)Avalonia.Application.Current!.FindResource(pick.Installed ? "IconPlay" : "IconArrowDown");
        }

        private void Filter_Changed(object? sender, RoutedEventArgs e)
        {
            if (loaded)
                Propose();
        }

        private void Genre_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (loaded)
                Propose();
        }

        private void Another_Click(object? sender, RoutedEventArgs e) => Propose();

        private async void Play_Click(object? sender, RoutedEventArgs e)
        {
            if (shown == null)
                return;
            Game game = shown;
            MainWindowViewModel.Instance.ClosePopup();
            try
            {
                if (InstallViewModel.Instance.InstalledGames.Any(g => g.Key?.ID == game.ID))
                    await InstallUserControl.PlayGame(game.ID);
                else
                    await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }

        private void GamePage_Click(object? sender, RoutedEventArgs e)
        {
            if (shown == null)
                return;
            MainWindowViewModel.Instance.ClosePopup();
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(shown));
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }
}
