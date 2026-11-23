using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    public class SocialScenarios
    {
        /// <summary>SOCIAL_SHOTS=folder (with SHOWCASE_DIR set, for real frames): the screens as pictures.</summary>
        private static void Shot(TopLevel window, string name)
        {
            if (Environment.GetEnvironmentVariable("SOCIAL_SHOTS") is not { Length: > 0 } folder)
                return;
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(Path.Combine(folder, name + ".png"));
        }

        [AvaloniaFact]
        public async Task AnotherPlayersActivity_TakesTheReactions_AndTheRecommendationsShowAboveIt()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            // Mario (player 2) played a retro game: a line of the activity that is not the player's
            session.Server.RetroMinutes["nes:4300"] = 10;
            string key = SocialService.ItemKey("retro", 2, "nes:4300", DateTime.UtcNow);
            session.Server.SocialReactions[key] = new() { (3, "Luigi", "🔥") };
            lock (session.Server.SocialRecommendations)
                session.Server.SocialRecommendations.Add(new() { ["id"] = "r1", ["from"] = 2, ["fromName"] = "Mario", ["to"] = 1, ["note"] = "À deux c'est top",
                    ["game"] = new { kind = "pc", id = 793, title = "Warhammer Survivors" }, ["at"] = DateTime.UtcNow });
            var page = MainWindowViewModel.Instance.Activity;
            var window = new Window { Width = 1200, Height = 800, Content = page };
            try
            {
                window.Show();
                await page.LoadAsync();
                Dispatcher.UIThread.RunJobs();
                ActivityFeedItem line = Assert.Single(page.Items, i => i.Key == key);
                Assert.True(line.CanReact);
                ReactionChip fire = Assert.Single(line.Reactions);
                Assert.Equal("🔥 1", fire.Text);
                Assert.Equal("Luigi", fire.Names);
                Assert.False(fire.Mine);

                // The player reacts, then changes their mind (one reaction each), then takes it away
                await ActivityUserControl.ReactAsync(line, "GG");
                Shot(window, "1-activity");
                // "Réagir" offers the emojis in a row
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Button add = page.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("add") && b.DataContext == line);
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Shot(window, "2-react-picker");
                var picks = Assert.IsType<StackPanel>(page.Picker!.Content).Children.OfType<Button>().ToList();
                Assert.Equal(SocialService.Emojis, picks.Select(b => (string)b.Content!));
                Assert.Contains("mine", picks[0].Classes);// GG, the player's
                page.Picker.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "GG 1", "🔥 1" }, line.Reactions.Select(r => r.Text));
                Assert.Equal("GG", line.MyEmoji);
                await ActivityUserControl.ReactAsync(line, "🔥");
                Assert.Equal(new[] { "🔥 2" }, line.Reactions.Select(r => r.Text));
                await ActivityUserControl.ReactAsync(line, "🔥");
                Assert.Null(line.MyEmoji);

                // The friend's recommendation, above the activity; put away with the cross
                Assert.True(page.FindControl<StackPanel>("uiRecommendationsSection")!.IsVisible);
                SocialRecommendation recommendation = Assert.Single(page.Recommendations);
                Assert.Contains("Mario", recommendation.Text);
                Assert.Contains("Warhammer Survivors", recommendation.Text);
                var dismiss = new Button { DataContext = recommendation };
                page.GetType().GetMethod("Dismiss_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(page, new object?[] { dismiss, new RoutedEventArgs() });
                await TestSession.WaitUntil(() => page.Recommendations.Count == 0, TimeSpan.FromSeconds(5), "the recommendation put away");
                lock (session.Server.SocialRecommendations)
                    Assert.Empty(session.Server.SocialRecommendations);
                Assert.False(page.FindControl<StackPanel>("uiRecommendationsSection")!.IsVisible);

                // Told at once by the chat's live link
                SocialRecommendation? told = null;
                string? reactedBy = null;
                void Recommended(SocialRecommendation r) => told = r;
                void Reacted(string name, string emoji, string item) => reactedBy = $"{name} {emoji}";
                SocialService.RecommendationReceived += Recommended;
                SocialService.ReactionReceived += Reacted;
                try
                {
                    ChatService.Instance.Take("{\"type\":\"recommendation\",\"recommendation\":{\"id\":\"r2\",\"from\":2,\"fromName\":\"Mario\",\"to\":1,\"game\":{\"kind\":\"retro\",\"id\":5,\"platform\":\"nes\",\"title\":\"Zelda\"}}}");
                    ChatService.Instance.Take("{\"type\":\"reaction\",\"item\":\"pc:1:2:2026-11-20\",\"emoji\":\"GG\",\"userId\":2,\"userName\":\"Mario\"}");
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal("Zelda", told?.Game.Title);
                    Assert.Equal("Mario GG", reactedBy);
                }
                finally
                {
                    SocialService.RecommendationReceived -= Recommended;
                    SocialService.ReactionReceived -= Reacted;
                }
            }
            finally
            {
                window.Content = null;
                window.Close();
                session.Server.RetroMinutes.TryRemove("nes:4300", out _);
                session.Server.SocialReactions.Clear();
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }

        [AvaloniaFact]
        public async Task AGame_IsRecommended_ToTheFriendChosen_WithAWord()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            var friend = new User { ID = 77, Username = "Recommend Friend" };
            bool hadUsers = !session.Server.Users.IsEmpty;
            if (!hadUsers)
                session.Server.Users[1] = FakeGameVaultServer.Admin;
            session.Server.Users[77] = friend;
            var popup = new RecommendPopup(new ChatGame { Kind = "pc", Id = 793, Title = "Warhammer Survivors" });
            var window = new Window { Width = 800, Height = 700, Content = popup };
            try
            {
                window.Show();
                await popup.LoadPlayersAsync();
                var players = popup.FindControl<ListBox>("uiPlayers")!;
                var listed = ((IEnumerable<User>)players.ItemsSource!).ToList();
                Assert.Contains(listed, u => u.ID == 77);
                // Not to oneself
                Assert.DoesNotContain(listed, u => u.ID == 1);
                popup.FindControl<TextBox>("uiSearch")!.Text = "recommend";
                Dispatcher.UIThread.RunJobs();
                players.SelectedItem = Assert.Single((IEnumerable<User>)players.ItemsSource!);
                Dispatcher.UIThread.RunJobs();
                Assert.True(popup.FindControl<Button>("uiSend")!.IsEnabled);
                popup.FindControl<TextBox>("uiNote")!.Text = "  Génial à deux  ";
                Shot(window, "2-recommend");
                await popup.SendAsync();
                Assert.True(session.Server.SocialRecommendationsSent.TryDequeue(out string? sent));
                using var body = System.Text.Json.JsonDocument.Parse(sent!);
                Assert.Equal(77, body.RootElement.GetProperty("to").GetInt32());
                Assert.Equal("Génial à deux", body.RootElement.GetProperty("note").GetString());
                Assert.Equal(793, body.RootElement.GetProperty("game").GetProperty("id").GetInt32());
            }
            finally
            {
                window.Close();
                session.Server.Users.TryRemove(77, out _);
                if (!hadUsers)
                    session.Server.Users.Clear();
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }

        [AvaloniaFact]
        public async Task AReview_IsPublished_OnTheGamesPage_AndTheOthersAreShown()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            const string game = "retro:nes:4310";
            session.Server.SocialReviews[game] = new() { new() { ["game"] = game, ["userId"] = 5, ["username"] = "Peach", ["stars"] = 2, ["text"] = "Trop dur", ["at"] = DateTime.UtcNow } };
            var panel = new ReviewsPanel();
            var window = new Window { Width = 900, Height = 700, Content = panel };
            try
            {
                window.Show();
                panel.Game = game;
                await TestSession.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return panel.IsVisible; }, TimeSpan.FromSeconds(5), "the reviews shown");
                var others = ((IEnumerable<ReviewRow>)panel.FindControl<ItemsControl>("uiReviews")!.ItemsSource!).ToList();
                ReviewRow peach = Assert.Single(others);
                Assert.Equal("★★☆☆☆", peach.Stars);
                // The player is an administrator: they may take it away
                Assert.True(peach.CanRemove);
                Assert.False(panel.FindControl<Button>("uiPublish")!.IsEnabled);

                // Four stars and a word
                var star = panel.FindControl<StackPanel>("uiStars")!.Children.OfType<ToggleButton>().ElementAt(3);
                star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(4, panel.FindControl<StackPanel>("uiStars")!.Children.OfType<ToggleButton>().Count(s => s.IsChecked == true));
                panel.FindControl<TextBox>("uiText")!.Text = "Un classique";
                await panel.PublishAsync();
                lock (session.Server.SocialReviews[game])
                {
                    var mine = Assert.Single(session.Server.SocialReviews[game], r => (int)r["userId"]! == 1);
                    Assert.Equal(4, (int)mine["stars"]!);
                    Assert.Equal("Un classique", (string?)mine["text"]);
                }
                Assert.True(panel.FindControl<Button>("uiRemove")!.IsVisible);
                Shot(window, "3-reviews");
                Assert.Contains("3.0", panel.FindControl<TextBlock>("uiSummary")!.Text!.Replace(',', '.'));
                // The player's own review is in the editor, not in the list
                Assert.Single((IEnumerable<ReviewRow>)panel.FindControl<ItemsControl>("uiReviews")!.ItemsSource!);
            }
            finally
            {
                window.Close();
                session.Server.SocialReviews.TryRemove(game, out _);
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }
    }
}
