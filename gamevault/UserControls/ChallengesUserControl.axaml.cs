using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using GameVault.Core;
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
    /// <summary>A player on a ranking of the Challenges page.</summary>
    public sealed class ChallengeRow
    {
        public string Place { get; init; } = "";
        public User User { get; init; } = new();
        public string Name { get; init; } = "";
        /// <summary>"1 h 20 / 5 h".</summary>
        public string Value { get; init; } = "";
        /// <summary>"Completed yesterday"; empty while the goal is not reached.</summary>
        public string Done { get; init; } = "";
        /// <summary>The way to the goal, from 0 to 1 (the ranking of the week).</summary>
        public double Fraction { get; init; }
        public bool HasBar { get; init; }
        /// <summary>Under the name when there is no bar: "2 times first".</summary>
        public string Detail { get; init; } = "";
        public bool IsMe { get; init; }
        public FontWeight Weight => IsMe ? FontWeight.Bold : FontWeight.SemiBold;
        public double PlaceOpacity => Place is "1" or "2" or "3" ? 1 : 0.55;
    }

    /// <summary>A week before this one.</summary>
    public sealed class ChallengePastRow
    {
        public string Week { get; init; } = "";
        public string Text { get; init; } = "";
        public string Result { get; init; } = "";
    }

    public partial class ChallengesUserControl : UserControl
    {
        /// <summary>What the page shows; null before the first answer and when the server keeps no challenges.</summary>
        internal ChallengeState? State { get; private set; }
        internal List<ChallengeRow> Board { get; private set; } = new();
        internal List<ChallengeRow> AllTime { get; private set; } = new();
        internal List<ChallengePastRow> Past { get; private set; } = new();

        /// <summary>The players of the server, for their avatars.</summary>
        private Dictionary<int, User> users = new();
        private DateTime usersLoadedAt;
        private Game? pcGame;

        public ChallengesUserControl()
        {
            InitializeComponent();
            uiAllTime.ItemTemplate = uiBoard.ItemTemplate;
            // Fresh each time the page is opened (the page host attaches the page it shows)
            AttachedToVisualTree += (_, _) =>
            {
                Focus();
                uiLoading.IsActive = uiLoading.IsVisible = State == null;
                _ = LoadAsync();
            };
            ChallengesService.Completed += () =>
            {
                if (this.IsAttachedToVisualTree())
                    _ = LoadAsync();
            };
        }

        internal async Task LoadAsync()
        {
            string? problem = null;
            try
            {
                State = await ChallengesService.GetAsync();
                if (State == null)
                    problem = Loc.T("The challenges are not available on this server.");
                else
                    await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                State = null;
                problem = Loc.T("The SanctuaryVault service could not be reached.");
            }
            uiLoading.IsActive = uiLoading.IsVisible = false;
            uiStatus.Text = problem;
            uiStatus.IsVisible = problem != null;
            uiContent.IsVisible = State != null;
            uiChoose.IsVisible = State != null && LoginManager.Instance.GetCurrentUser()?.Role == PERMISSION_ROLE.ADMIN;
            if (State != null)
                Show(State);
        }

        /// <summary>Names and avatars, asked again after 10 minutes or when a player of the ranking is not among them.</summary>
        private async Task LoadUsersAsync()
        {
            bool all = State!.Board.Select(r => r.UserId).Concat(State.AllTime.Select(h => h.UserId)).All(users.ContainsKey);
            if (all && DateTime.UtcNow - usersLoadedAt < TimeSpan.FromMinutes(10))
                return;
            try
            {
                var list = JsonSerializer.Deserialize<User[]>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/users")) ?? Array.Empty<User>();
                users = list.ToDictionary(u => u.ID);
                usersLoadedAt = DateTime.UtcNow;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private User UserOf(int id, string name) => users.GetValueOrDefault(id) ?? new User { ID = id, Username = name };

        private void Show(ChallengeState state)
        {
            ChallengeInfo challenge = state.Challenge;
            int me = LoginManager.Instance.GetCurrentUser()?.ID ?? -1;
            uiText.Text = challenge.Text;
            uiHint.Text = challenge.Hint;
            uiHint.IsVisible = challenge.Hint.Length > 0;
            uiLeft.Text = TimeLeft(state.EndsAt.ToUniversalTime() - DateTime.UtcNow);
            uiSetBy.Text = challenge.SetBy is { Length: > 0 } admin ? Loc.F("Chosen by {0}", admin) : "";

            ChallengeRank? mine = state.Board.FirstOrDefault(r => r.UserId == me);
            int goal = Math.Max(1, challenge.Goal);
            uiMine.Value = Math.Min(1, (mine?.Value ?? 0) / (double)goal);
            uiMineText.Text = mine?.DoneAt != null
                ? Loc.F("Completed: {0}", challenge.Amount(mine.Value))
                : Loc.F("{0} of {1}", challenge.Amount(mine?.Value ?? 0), challenge.Amount(challenge.Goal));

            Board = state.Board.Select((rank, i) => new ChallengeRow
            {
                Place = (i + 1).ToString(),
                User = UserOf(rank.UserId, rank.Username),
                Name = rank.Username,
                Value = challenge.Amount(rank.Value),
                Done = rank.DoneAt is DateTime done ? Loc.F("Completed {0}", Converter.RelativeDate.FormatMoment(done)) : "",
                Fraction = Math.Min(1, rank.Value / (double)goal),
                HasBar = true,
                IsMe = rank.UserId == me,
            }).ToList();
            uiBoard.ItemsSource = Board;
            uiBoardEmpty.IsVisible = Board.Count == 0;

            AllTime = state.AllTime.Select((honor, i) => new ChallengeRow
            {
                Place = (i + 1).ToString(),
                User = UserOf(honor.UserId, honor.Username),
                Name = honor.Username,
                Value = Loc.F(honor.Completed == 1 ? "{0} challenge" : "{0} challenges", honor.Completed),
                Detail = honor.First == 0 ? Loc.T("Never the first yet") : Loc.F(honor.First == 1 ? "{0} time the first" : "{0} times the first", honor.First),
                IsMe = honor.UserId == me,
            }).ToList();
            uiAllTime.ItemsSource = AllTime;
            uiAllTimeSection.IsVisible = AllTime.Count > 0;

            Past = state.Past.Select(week => new ChallengePastRow
            {
                Week = Loc.F("Week of {0}", week.StartsAt.ToUniversalTime().ToString("M")),
                Text = week.Challenge.Text,
                Result = week.Winner == null ? Loc.T("Nobody completed it")
                    : week.Completed == 1 ? Loc.F("{0} alone completed it", week.Winner.Username)
                    : Loc.F("{0} first, {1} players completed it", week.Winner.Username, week.Completed),
            }).ToList();
            uiPast.ItemsSource = Past;
            uiPastSection.IsVisible = Past.Count > 0;

            _ = ShowGameAsync(challenge.TheGame);
        }

        /// <summary>"Ends in 3 days", then in hours, then in minutes.</summary>
        internal static string TimeLeft(TimeSpan left) =>
            left.TotalHours >= 48 ? Loc.F("Ends in {0} days", (int)left.TotalDays)
            : left.TotalHours >= 1 ? Loc.F("Ends in {0} h", (int)left.TotalHours)
            : Loc.F("Ends in {0} min", Math.Max(1, (int)left.TotalMinutes));

        /// <summary>The cover of the challenge's game, when it has one.</summary>
        private async Task ShowGameAsync(ChatGame? game)
        {
            uiGame.IsVisible = game != null;
            uiGame.Tag = game;
            uiPcCover.IsVisible = uiRetroCover.IsVisible = false;
            if (game == null)
                return;
            try
            {
                if (game.IsRetro)
                {
                    if (game.Cover == null)
                        return;
                    uiRetroCover.Source = await RetroService.MediaImageAsync(game.Cover, 200, game.CoverVersion);
                    uiRetroCover.IsVisible = ReferenceEquals(uiGame.Tag, game);
                    return;
                }
                if (pcGame?.ID != game.Id)
                    pcGame = InstallViewModel.Instance.InstalledGames.Select(g => g.Key).FirstOrDefault(g => g?.ID == game.Id)
                        ?? MainWindowViewModel.Instance.Library?.Model.GameCards.FirstOrDefault(g => g.ID == game.Id)
                        ?? JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{game.Id}"));
                if (pcGame == null || !ReferenceEquals(uiGame.Tag, game))
                    return;
                uiPcCover.Data = pcGame;
                uiPcCover.IsVisible = true;
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void Game_Click(object? sender, RoutedEventArgs e)
        {
            if (uiGame.Tag is not ChatGame game)
                return;
            if (game.IsRetro)
                _ = MainWindowViewModel.Instance.Retro.ShowGameAsync(game.Platform ?? "", game.Id);
            else
                MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(pcGame ?? new Game { ID = game.Id }));
        }

        private void Player_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ChallengeRow row && users.ContainsKey(row.User.ID))
                MainWindowViewModel.Instance.Community.ShowUser(row.User);
        }

        private void Choose_Click(object? sender, RoutedEventArgs e)
        {
            if (State != null)
                MainWindowViewModel.Instance.OpenPopup(new ChallengeEditPopup(State.Challenge, () => _ = LoadAsync()));
        }
    }
}
