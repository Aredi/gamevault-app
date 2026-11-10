using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Library;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>A game found for the challenge.</summary>
    public sealed class ChallengeGameChoice
    {
        public ChatGame Game { get; init; } = new();
        public string Title => Game.Title;
        public string Subtitle { get; init; } = "";
    }

    /// <summary>An administrator chooses the challenge of the week: what counts, how much, and on which game.</summary>
    public partial class ChallengeEditPopup : UserControl
    {
        private sealed record KindChoice(string Kind, string Label, int Goal);

        private readonly Action saved;
        private readonly DispatcherTimer search = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private List<RetroGame> retroGames = new();
        private int searchGeneration;
        /// <summary>Filling the fields is not the administrator changing them.</summary>
        private bool filling;

        internal ChatGame? Chosen { get; private set; }

        public ChallengeEditPopup() : this(new ChallengeInfo { Goal = 300 }, () => { }) { }

        /// <param name="current">This week's challenge: where the fields start.</param>
        public ChallengeEditPopup(ChallengeInfo current, Action saved)
        {
            InitializeComponent();
            this.saved = saved;
            var kinds = new List<KindChoice>
            {
                new(ChallengeInfo.Hours, Loc.T("Play time, on any game"), 300),
                new(ChallengeInfo.Games, Loc.T("Different games played"), 3),
                new(ChallengeInfo.Discovery, Loc.T("Play time on a game never launched"), 120),
                new(ChallengeInfo.Retro, Loc.T("Play time on retro games"), 120),
                new(ChallengeInfo.Achievements, Loc.T("Retro achievements earned"), 3),
                new(ChallengeInfo.Game, Loc.T("Play time on a chosen game"), 120),
            };
            filling = true;
            uiKind.ItemsSource = kinds;
            uiKind.SelectedItem = kinds.FirstOrDefault(k => k.Kind == current.Kind) ?? kinds[0];
            Chosen = current.TheGame;
            ShowKind(current.Goal);
            filling = false;
            search.Tick += async (_, _) =>
            {
                search.Stop();
                await SearchAsync(uiSearch.Text ?? "");
            };
            AttachedToVisualTree += async (_, _) => await LoadRetroGamesAsync();
        }

        /// <summary>The retro games of every platform, asked once (the Retro page keeps them).</summary>
        internal async Task LoadRetroGamesAsync()
        {
            try { retroGames = await MainWindowViewModel.Instance.Retro.AllGamesAsync(); }
            catch (Exception ex) { Log.Ignored(ex); }
            if (!string.IsNullOrWhiteSpace(uiSearch.Text))
                await SearchAsync(uiSearch.Text);
        }

        internal string Kind => (uiKind.SelectedItem as KindChoice)?.Kind ?? ChallengeInfo.Hours;
        private static bool IsCount(string kind) => kind is ChallengeInfo.Games or ChallengeInfo.Achievements;

        /// <summary>The goal as the service wants it: minutes, or a number of games or achievements.</summary>
        internal int Goal => IsCount(Kind) ? (int)(uiGoal.Value ?? 0) : (int)Math.Round((uiGoal.Value ?? 0) * 60);

        /// <summary>The fields of the chosen kind, with this goal (the usual one when none is given).</summary>
        private void ShowKind(int? goal = null)
        {
            var kind = (KindChoice)uiKind.SelectedItem!;
            bool count = IsCount(kind.Kind);
            int value = goal is > 0 ? goal.Value : kind.Goal;
            uiGoalLabel.Text = kind.Kind switch
            {
                ChallengeInfo.Games => Loc.T("Number of games"),
                ChallengeInfo.Achievements => Loc.T("Number of achievements"),
                _ => Loc.T("Hours to play"),
            };
            // The largest bounds first: the old value must stay inside them while they change
            (uiGoal.Minimum, uiGoal.Maximum) = (0, 6000);
            uiGoal.FormatString = count ? "0" : "0.##";
            uiGoal.Increment = count ? 1 : 0.5m;
            uiGoal.Value = count ? value : Math.Round(value / 60m, 2);
            (uiGoal.Minimum, uiGoal.Maximum) = count ? (1, kind.Kind == ChallengeInfo.Games ? 20 : 200) : (0.25m, 100);

            bool retroOnly = kind.Kind == ChallengeInfo.Achievements;
            uiGameSection.IsVisible = kind.Kind is ChallengeInfo.Game or ChallengeInfo.Achievements;
            uiGameLabel.Text = retroOnly ? Loc.T("On one retro game (optional)") : Loc.T("The game");
            if (!uiGameSection.IsVisible || (retroOnly && Chosen is { IsRetro: false }))
                Chosen = null;
            ShowChosen();
        }

        private void ShowChosen()
        {
            uiChosen.IsVisible = Chosen != null;
            uiPicker.IsVisible = Chosen == null;
            uiChosenText.Text = Chosen == null ? "" : Chosen.IsRetro ? $"{Chosen.Title} ({Chosen.PlatformName ?? Chosen.Platform})" : Chosen.Title;
            uiPreview.Text = new ChallengeInfo { Kind = Kind, Goal = Goal, TheGame = Chosen }.Text;
        }

        private void Kind_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (filling || uiKind.SelectedItem == null)
                return;
            filling = true;
            ShowKind();
            filling = false;
            uiResults.ItemsSource = null;
            if (!string.IsNullOrWhiteSpace(uiSearch.Text))
                Search_TextChanged(null, null!);
        }

        private void Goal_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!filling)
                ShowChosen();
        }

        private void Search_TextChanged(object? sender, TextChangedEventArgs e)
        {
            searchGeneration++;
            search.Stop();
            search.Start();
        }

        /// <summary>The retro games by their name, and (for the time on a game) the library's as the server finds them.</summary>
        internal async Task SearchAsync(string query)
        {
            int generation = ++searchGeneration;
            if (string.IsNullOrWhiteSpace(query))
            {
                uiResults.ItemsSource = null;
                return;
            }
            var found = retroGames
                .Select(game => (Game: game, Score: QuickMatch.Score(query, game.Name)))
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score).Take(12)
                .Select(match => (match.Score, Choice: new ChallengeGameChoice { Game = ChatGame.Of(match.Game, false), Subtitle = match.Game.Platform.Name }))
                .ToList();
            if (Kind == ChallengeInfo.Game)
            {
                try
                {
                    string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?search={Uri.EscapeDataString(query.Trim())}&limit=20");
                    Game[] games = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                    // The server found them (also by other words than the title): never below a weak title match
                    found.AddRange(games.Where(g => g.DeletedAt == null).Select(g => (Math.Max(QuickMatch.Score(query, g.Title), 100), new ChallengeGameChoice { Game = ChatGame.Of(g, false), Subtitle = Loc.T("Library") })));
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            if (generation != searchGeneration)
                return;
            uiResults.ItemsSource = found.OrderByDescending(match => match.Score).Take(14).Select(match => match.Choice).ToList();
        }

        private void Result_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ChallengeGameChoice choice)
                Choose(choice.Game);
        }

        internal void Choose(ChatGame? game)
        {
            Chosen = game;
            ShowChosen();
        }

        private void Unchoose_Click(object? sender, RoutedEventArgs e) => Choose(null);

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();

        private async void Save_Click(object? sender, RoutedEventArgs e) => await SaveAsync();

        internal async Task SaveAsync()
        {
            if (Kind == ChallengeInfo.Game && Chosen == null)
            {
                Tell(Loc.T("Choose the game of the challenge."));
                return;
            }
            uiSave.IsEnabled = false;
            try
            {
                await ChallengesService.SetAsync(Kind, Goal, Chosen);
                MainWindowViewModel.Instance.ClosePopup();
                saved();
            }
            catch (Exception ex) { Tell(ex is InvalidOperationException ? ex.Message : WebExceptionHelper.TryGetServerMessage(ex)); }
            finally { uiSave.IsEnabled = true; }
        }

        private void Tell(string text)
        {
            uiStatus.Text = text;
            uiStatus.IsVisible = true;
        }
    }
}
