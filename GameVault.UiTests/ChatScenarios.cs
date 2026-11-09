using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using SkiaSharp;

namespace GameVault.UiTests
{
    public class ChatScenarios
    {
        /// <summary>With SHOWCASE_DIR (Skia renders then): the panel as the player sees it, in SHOWCASE_DIR/chat.</summary>
        private static void Shot(TestSession session, string name)
        {
            if (!ShowcaseScenarios.Enabled)
                return;
            string folder = Path.Combine(ShowcaseScenarios.Folder!, "chat");
            Directory.CreateDirectory(folder);
            for (int i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            session.Window.CaptureRenderedFrame()!.Save(Path.Combine(folder, name + ".png"));
        }

        [AvaloniaFact]
        public async Task AFriendWrites_ThePlayerReadsAndAnswers_WithAnEmojiAndAScreenshot()
        {
            var session = await TestSession.GetAsync();
            var added = new List<int>();
            foreach (var (id, name) in new[] { (2, "Zoé"), (3, "Malik") })
                if (session.Server.Users.TryAdd(id, new User { ID = id, Username = name, Role = PERMISSION_ROLE.USER, Activated = true, Progresses = Array.Empty<Progress>() }))
                    added.Add(id);
            session.Server.Users.TryAdd(1, FakeGameVaultServer.Admin);
            string png = Path.Combine(Path.GetTempPath(), $"gv-chat-{Guid.NewGuid():N}.png");
            var panel = session.Window.FindControl<ChatPanel>("uiChat")!;
            // The fake server is the service too
            SanctuaryService.Current = session.Server.Url;
            try
            {
                // Zoé plays and writes; the chat is there and says it
                ActivityService.Instance.Apply(new[] { new PlayerActivity { UserId = 2, State = "PLAYING", GameId = 9790 } }, replaceAll: true);
                session.Server.ChatFrom(2, "Zoé", "salut, tu viens ?");
                await ChatService.Instance.RefreshAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.True(MainWindowViewModel.Instance.IsChatAvailable);
                Assert.Equal("1", MainWindowViewModel.Instance.ChatBadge);

                MainWindowViewModel.Instance.IsChatOpen = true;
                await panel.LoadUsersAsync(again: true);
                panel.Rebuild();
                Assert.True(panel.IsEffectivelyVisible);
                ChatEntry zoe = panel.Model.Friends.First();
                Assert.Equal(("Zoé", 1, "salut, tu viens ?"), (zoe.Title, zoe.Unread, zoe.Subtitle));
                Assert.NotNull(zoe.Dot);// playing
                Assert.DoesNotContain(panel.Model.Friends, f => f.User?.ID == 1);// not oneself

                // Malik plays a retro game: GameVault does not know it, the service says it
                ActivityService.Instance.ApplyRetro(new[] { new RetroActivity { UserId = 3, Platform = "nes", RomId = 40, Name = "The Legend of Zelda", PlatformName = "NES" } }, replaceAll: true);
                Dispatcher.UIThread.RunJobs();
                panel.Rebuild();
                ChatEntry malik = panel.Model.Friends.First(f => f.User?.ID == 3);
                Assert.Equal((gamevault.Localization.Loc.F("Playing {0}", "The Legend of Zelda (NES)"), 0), (malik.Subtitle, malik.Rank));
                Assert.NotNull(malik.Dot);
                Assert.Equal(gamevault.Localization.Loc.F("Playing {0}", "The Legend of Zelda (NES)"), gamevault.Converter.PresenceConverter.PlayingText(3));// the community page too
                ActivityService.Instance.ApplyRetro(null, replaceAll: false, stoppedBy: 3);
                Dispatcher.UIThread.RunJobs();
                panel.Rebuild();
                Assert.Equal(gamevault.Localization.Loc.T("Offline"), panel.Model.Friends.First(f => f.User?.ID == 3).Subtitle);
                Shot(session, "1-friends");

                // Read: the badge goes, the service knows it
                await panel.OpenAsync(zoe);
                Assert.Equal("salut, tu viens ?", Assert.Single(panel.Model.Messages).Text);
                Assert.Equal("", MainWindowViewModel.Instance.ChatBadge);
                Assert.True(session.Server.ChatReadAt.ContainsKey("d-1-2"));

                // An answer with an emoji
                var draft = panel.FindControl<TextBox>("uiDraft")!;
                draft.Text = "j'arrive ";
                draft.CaretIndex = draft.Text.Length;
                panel.FindControl<ItemsControl>("uiEmojis")!.ItemsSource = ChatPanel.Emojis;
                draft.Text += "🎮";
                await panel.SendAsync();
                Assert.Equal("j'arrive 🎮", session.Server.Chat["d-1-2"][^1].Text);
                Assert.Equal("", draft.Text);
                ChatMessageRow mine = panel.Model.Messages[^1];
                Assert.True(mine.IsMine);
                Assert.Equal(2, panel.Model.Messages.Count);

                // A screenshot, with a word
                using (var bitmap = new SKBitmap(64, 36))
                {
                    bitmap.Erase(SKColors.Orange);
                    using var image = SKImage.FromBitmap(bitmap);
                    File.WriteAllBytes(png, image.Encode(SKEncodedImageFormat.Png, 90).ToArray());
                }
                await panel.TakeFileAsync(png);
                Assert.NotNull(panel.Model.DraftPicture);
                draft.Text = "regarde";
                await panel.SendAsync();
                Assert.Null(panel.Model.DraftPicture);
                var sent = session.Server.Chat["d-1-2"][^1];
                Assert.Equal(("regarde", true), (sent.Text, sent.Picture != null));
                ChatMessageRow withPicture = panel.Model.Messages[^1];
                await TestSession.WaitUntil(() => withPicture.Picture != null, TimeSpan.FromSeconds(10), "the picture sent");

                // A file that is no picture is refused
                string page = Path.ChangeExtension(png, ".txt");
                File.WriteAllText(page, "<html></html>");
                await panel.TakeFileAsync(page);
                Assert.Null(panel.Model.DraftPicture);
                File.Delete(page);

                // Live: Zoé's next message shows in the open conversation, already read
                var next = session.Server.ChatFrom(2, "Zoé", "top 👍");
                ChatService.Instance.Take(System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "message",
                    message = new { id = next.Id, conversationId = "d-1-2", senderId = 2, senderName = "Zoé", text = "top 👍", sentAt = next.SentAt },
                }));
                Dispatcher.UIThread.RunJobs();
                await TestSession.WaitUntil(() => panel.Model.Messages.Count == 4, TimeSpan.FromSeconds(5), "the live message");
                Assert.Equal("top 👍", panel.Model.Messages[^1].Text);
                Shot(session, "2-conversation");
                Assert.False(panel.Model.Messages[^1].IsMine);

                // A message from someone else, while Zoé's conversation is open: a notice and a badge
                session.Server.ChatFrom(3, "Malik", "GG");
                await ChatService.Instance.RefreshAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Malik : GG", MainWindowViewModel.Instance.AppBarText);
                Assert.Equal("1", MainWindowViewModel.Instance.ChatBadge);
                Assert.Equal(1, panel.Model.Friends.First(f => f.User?.ID == 3).Unread);
            }
            finally
            {
                MainWindowViewModel.Instance.IsChatOpen = false;
                SanctuaryService.Current = SanctuaryService.Url;
                ActivityService.Instance.Apply(Array.Empty<PlayerActivity>(), replaceAll: true);
                ActivityService.Instance.ApplyRetro(null, replaceAll: true);
                gamevault.Converter.PresenceConverter.GameTitles.Remove(9790);
                session.Server.Chat.Clear();
                session.Server.ChatReadAt.Clear();
                foreach (int id in added)
                    session.Server.Users.TryRemove(id, out _);
                File.Delete(png);
            }
        }

        [AvaloniaFact]
        public async Task AServiceWithoutTheChat_ShowsNoChat()
        {
            var session = await TestSession.GetAsync();
            session.Server.ChatOff = true;
            SanctuaryService.Current = session.Server.Url;
            try
            {
                await ChatService.Instance.RefreshAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.False(ChatService.Instance.IsAvailable);
                Assert.False(MainWindowViewModel.Instance.IsChatAvailable);
                MainWindowViewModel.Instance.IsChatOpen = true;
                Assert.False(session.Window.FindControl<ChatPanel>("uiChat")!.IsEffectivelyVisible);
            }
            finally
            {
                MainWindowViewModel.Instance.IsChatOpen = false;
                session.Server.ChatOff = false;
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }
    }
}
