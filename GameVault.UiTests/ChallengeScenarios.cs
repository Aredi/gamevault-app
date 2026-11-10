using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.UserControls;
using gamevault.ViewModels;
using System.Text.Json;

namespace GameVault.UiTests
{
    /// <summary>The challenge of the week: its page with the rankings, the administrator choosing it, the player told when they complete it.</summary>
    public class ChallengeScenarios
    {
        private static object Week(object challenge, params object[] board) => new
        {
            week = "2026-W46",
            startsAt = DateTime.UtcNow.AddDays(-2),
            endsAt = DateTime.UtcNow.AddDays(5).AddHours(1),
            challenge,
            board,
            allTime = new object[]
            {
                new { userId = 2, username = "Léa", completed = 3, first = 2 },
                new { userId = 1, username = "admin", completed = 1, first = 0 },
            },
            past = new object[]
            {
                new { week = "2026-W45", startsAt = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), challenge = new { kind = "games", goal = 3 }, winner = new { userId = 2, username = "Léa", value = 3, doneAt = DateTime.UtcNow.AddDays(-6) }, completed = 2 },
                new { week = "2026-W44", startsAt = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), challenge = new { kind = "retro", goal = 120 }, winner = (object?)null, completed = 0 },
            },
        };

        private static void CleanUp(TestSession session)
        {
            SanctuaryService.Current = SanctuaryService.Url;
            ChallengesService.OffUntil = default;
            session.Server.ChallengeState = null;
            session.Server.ChallengeDoneNext = false;
            session.Server.ChallengePlaysSent.Clear();
            session.Server.ChallengesSet.Clear();
            MainWindowViewModel.Instance.ClosePopup();
        }

        [AvaloniaFact]
        public async Task ThePage_ShowsTheChallenge_MyProgress_AndTheRankings()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            try
            {
                session.Server.ChallengeState = Week(new { kind = "hours", goal = 300, setBy = "admin" },
                    new { userId = 2, username = "Léa", value = 320, doneAt = (DateTime?)DateTime.UtcNow.AddHours(-3) },
                    new { userId = 1, username = "admin", value = 90, doneAt = (DateTime?)null });
                var page = new ChallengesUserControl();
                await page.LoadAsync();

                Assert.True(page.uiContent.IsVisible);
                Assert.False(page.uiStatus.IsVisible);
                Assert.True(page.uiChoose.IsVisible);
                Assert.Equal(Loc.F("Play {0} this week", "5 h"), page.uiText.Text);
                Assert.Equal(Loc.F("Ends in {0} days", 5), page.uiLeft.Text);
                Assert.Equal(Loc.F("Chosen by {0}", "admin"), page.uiSetBy.Text);
                // Mine: 1 h 30 of 5 h
                Assert.Equal(Loc.F("{0} of {1}", "1 h 30", "5 h"), page.uiMineText.Text);
                Assert.Equal(0.3, page.uiMine.Value, 3);

                Assert.Equal(new[] { "Léa", "admin" }, page.Board.Select(r => r.Name));
                Assert.StartsWith(Loc.F("Completed {0}", "").TrimEnd(), page.Board[0].Done);
                Assert.Equal(1, page.Board[0].Fraction);
                Assert.Equal("", page.Board[1].Done);
                Assert.True(page.Board[1].IsMe);
                Assert.False(page.uiBoardEmpty.IsVisible);

                Assert.Equal(Loc.F("{0} challenges", 3), page.AllTime[0].Value);
                Assert.Equal(Loc.F("{0} times the first", 2), page.AllTime[0].Detail);
                Assert.Equal(Loc.F("{0} challenge", 1), page.AllTime[1].Value);
                Assert.Equal(Loc.T("Never the first yet"), page.AllTime[1].Detail);

                Assert.Equal(Loc.F("Play {0} different games this week", 3), page.Past[0].Text);
                Assert.Equal(Loc.F("{0} first, {1} players completed it", "Léa", 2), page.Past[0].Result);
                Assert.Equal(Loc.T("Nobody completed it"), page.Past[1].Result);

                // Nobody started: the page says so, and my bar is empty
                session.Server.ChallengeState = Week(new { kind = "achievements", goal = 3, game = new { kind = "retro", id = 40, platform = "nes", platformName = "NES", title = "Zelda" } });
                await page.LoadAsync();
                Assert.Equal(Loc.F("Earn {0} retro achievements in {1}", 3, "Zelda"), page.uiText.Text);
                Assert.True(page.uiBoardEmpty.IsVisible);
                Assert.Equal(Loc.F("{0} of {1}", "0", "3"), page.uiMineText.Text);
                Assert.True(page.uiGame.IsVisible);

                // A server that keeps no challenges
                session.Server.ChallengeState = null;
                await page.LoadAsync();
                Assert.False(page.uiContent.IsVisible);
                Assert.False(page.uiChoose.IsVisible);
                Assert.Equal(Loc.T("The challenges are not available on this server."), page.uiStatus.Text);
            }
            finally { CleanUp(session); }
        }

        [AvaloniaFact]
        public async Task AnAdministrator_ChoosesTheChallenge_WithItsGoalAndItsGame()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            try
            {
                session.Server.ChallengeState = Week(new { kind = "hours", goal = 300 });
                bool saved = false;
                var popup = new ChallengeEditPopup(new ChallengeInfo { Kind = ChallengeInfo.Hours, Goal = 300 }, () => saved = true);
                Assert.Equal(ChallengeInfo.Hours, popup.Kind);
                Assert.Equal(300, popup.Goal);
                Assert.False(popup.uiGameSection.IsVisible);

                // A number of games: the goal is a count
                popup.uiKind.SelectedIndex = 1;
                Assert.Equal(ChallengeInfo.Games, popup.Kind);
                Assert.Equal(3, popup.Goal);
                Assert.Equal(Loc.F("Play {0} different games this week", 3), popup.uiPreview.Text);

                // The time on a chosen game: it needs its game
                popup.uiKind.SelectedIndex = 5;
                Assert.Equal(ChallengeInfo.Game, popup.Kind);
                Assert.True(popup.uiGameSection.IsVisible);
                await popup.SaveAsync();
                Assert.Equal(Loc.T("Choose the game of the challenge."), popup.uiStatus.Text);
                Assert.Empty(session.Server.ChallengesSet);

                // The Retro page lists the games again from this server (other scenarios left it on theirs)
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Retro);
                await MainWindowViewModel.Instance.Retro.EnsureLoadedAsync();
                await popup.LoadRetroGamesAsync();
                await popup.SearchAsync("zelda");
                var found = Assert.IsAssignableFrom<IEnumerable<ChallengeGameChoice>>(popup.uiResults.ItemsSource).ToList();
                ChallengeGameChoice zelda = Assert.Single(found, c => c.Game.IsRetro && c.Game.Id == 40);
                Assert.Equal("NES", zelda.Subtitle);
                popup.Choose(zelda.Game);
                popup.uiGoal.Value = 1.5m;
                Assert.Equal(Loc.F("Play {1} for {0}", "1 h 30", "Zelda"), popup.uiPreview.Text);

                await popup.SaveAsync();
                Assert.True(saved);
                using JsonDocument sent = JsonDocument.Parse(Assert.Single(session.Server.ChallengesSet));
                Assert.Equal("game", sent.RootElement.GetProperty("kind").GetString());
                Assert.Equal(90, sent.RootElement.GetProperty("goal").GetInt32());
                JsonElement game = sent.RootElement.GetProperty("game");
                Assert.Equal("retro", game.GetProperty("kind").GetString());
                Assert.Equal(40, game.GetProperty("id").GetInt32());
                Assert.Equal("nes", game.GetProperty("platform").GetString());
            }
            finally { CleanUp(session); }
        }

        [AvaloniaFact]
        public async Task TheMinutesPlayed_AreSaidToTheService_AndThePlayerIsToldWhenTheyCompleteTheChallenge()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            try
            {
                session.Server.ChallengeState = Week(new { kind = "hours", goal = 300 });
                ChallengesService.OffUntil = default;
                MainWindowViewModel.Instance.AppBarText = "";
                await ChallengesService.PlayedAsync(new[] { 5, 6 });
                Assert.Equal("{\"gameIds\":[5,6]}", Assert.Single(session.Server.ChallengePlaysSent));
                Assert.Equal("", MainWindowViewModel.Instance.AppBarText);

                // The minute that completes it
                int completed = 0;
                Action count = () => completed++;
                ChallengesService.Completed += count;
                try
                {
                    session.Server.ChallengeDoneNext = true;
                    await ChallengesService.PlayedAsync(new[] { 5 });
                    await TestSession.WaitUntil(() => completed == 1, TimeSpan.FromSeconds(10), "the player to be told");
                    Assert.Equal(Loc.T("Challenge of the week completed!"), MainWindowViewModel.Instance.AppBarText);
                }
                finally { ChallengesService.Completed -= count; }

                // A server without challenges is left alone for a while
                session.Server.ChallengeState = null;
                int Asked() => session.Server.Requests.Count(r => r.Contains("/challenges/me/plays"));
                int before = Asked();
                await ChallengesService.PlayedAsync(new[] { 5 });
                await ChallengesService.PlayedAsync(new[] { 5 });
                Assert.Equal(before + 1, Asked());
                Assert.True(ChallengesService.OffUntil > DateTime.UtcNow.AddMinutes(20));
            }
            finally
            {
                MainWindowViewModel.Instance.AppBarText = "";
                CleanUp(session);
            }
        }
    }
}
