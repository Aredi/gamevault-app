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
        public async Task AConversation_IsAnswered_Reacted_Edited_WithAGame_AndSeen()
        {
            var session = await TestSession.GetAsync();
            bool added = session.Server.Users.TryAdd(2, new User { ID = 2, Username = "Zoé", Role = PERMISSION_ROLE.USER, Activated = true, Progresses = Array.Empty<Progress>() });
            session.Server.Users.TryAdd(1, FakeGameVaultServer.Admin);
            var panel = session.Window.FindControl<ChatPanel>("uiChat")!;
            SanctuaryService.Current = session.Server.Url;
            try
            {
                session.Server.ChatFrom(2, "Zoé", "on lance quoi ?");
                await ChatService.Instance.RefreshAsync();
                MainWindowViewModel.Instance.IsChatOpen = true;
                await panel.LoadUsersAsync(again: true);
                panel.Rebuild();
                await panel.OpenAsync(panel.Model.Friends.First(f => f.User?.ID == 2));
                ChatMessageRow question = Assert.Single(panel.Model.Messages);

                // Zoé writes: it shows in the conversation and in the list, and goes when her message comes
                ChatService.Instance.Take(System.Text.Json.JsonSerializer.Serialize(new { type = "typing", conversationId = "d-1-2", userId = 2, userName = "Zoé" }));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(gamevault.Localization.Loc.F("{0} is writing…", "Zoé"), panel.Model.TypingText);
                Assert.Equal(gamevault.Localization.Loc.T("Writing…"), panel.Model.Friends.First(f => f.User?.ID == 2).Subtitle);

                // An answer to her message
                var draft = panel.FindControl<TextBox>("uiDraft")!;
                panel.Answer(question);
                Assert.True(panel.Model.HasDraftNote);
                draft.Text = "Hades ?";
                await panel.SendAsync();
                ChatMessageRow answer = panel.Model.Messages[^1];
                Assert.True(answer.HasReply);
                Assert.Equal(("Zoé", "on lance quoi ?"), (answer.ReplySender, answer.ReplyText));
                Assert.False(panel.Model.HasDraftNote);

                // An emoji on her message, then the answer written again
                await panel.ReactAsync(panel.Model.Messages[0], "👍");
                Dispatcher.UIThread.RunJobs();
                ChatReactionChip chip = Assert.Single(panel.Model.Messages[0].Reactions);
                Assert.Equal(("👍 1", true), (chip.Text, chip.IsMine));
                panel.Edit(panel.Model.Messages[^1]);
                Assert.Equal("Hades ?", draft.Text);
                draft.Text = "Hades II ?";
                await panel.SendAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Hades II ?", session.Server.Chat["d-1-2"][^1].Text);
                Assert.Equal(2, panel.Model.Messages.Count);
                Assert.Contains(gamevault.Localization.Loc.T("edited"), panel.Model.Messages[^1].Footer);

                // She read it: "Seen" under the last message of the player's
                ChatService.Instance.Take(System.Text.Json.JsonSerializer.Serialize(new { type = "read", conversationId = "d-1-2", userId = 2, readAt = DateTime.UtcNow.AddSeconds(5) }));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(gamevault.Localization.Loc.T("Seen"), panel.Model.Messages[^1].Seen);

                // An invitation to a retro game: a card, and its game's page a click away
                var zelda = new RetroGame { Id = 40, Name = "The Legend of Zelda", Platform = new RetroPlatform { Id = 3, Slug = "nes", Name = "NES" } };
                panel.OpenGamePicker(invite: true);
                Assert.True(panel.Model.IsPickingGame);
                await panel.PickAsync(new ChatGamePick { Title = zelda.Name, Retro = zelda });
                Dispatcher.UIThread.RunJobs();
                Assert.False(panel.Model.IsPickingGame);
                var sent = session.Server.Chat["d-1-2"][^1];
                Assert.Equal(("retro", "nes", true), (sent.Game!.Value.GetProperty("kind").GetString(), sent.Game.Value.GetProperty("platform").GetString(), sent.Game.Value.GetProperty("invite").GetBoolean()));
                ChatMessageRow card = panel.Model.Messages[^1];
                Assert.True(card.HasGame);
                Assert.Equal(gamevault.Localization.Loc.T("🎮 Invitation sent"), card.GameHeader);
                Shot(session, "3-answers-games");

                // Taken back: its place stays, without its game
                await panel.DeleteAsync(card, confirm: false);
                Dispatcher.UIThread.RunJobs();
                Assert.True(panel.Model.Messages[^1].IsDeleted);
                Assert.False(panel.Model.Messages[^1].HasGame);
                Assert.Equal(gamevault.Localization.Loc.T("Message deleted"), panel.Model.Messages[^1].Text);
            }
            finally
            {
                panel.Model.Open = null;
                MainWindowViewModel.Instance.IsChatOpen = false;
                SanctuaryService.Current = SanctuaryService.Url;
                session.Server.Chat.Clear();
                session.Server.ChatReadAt.Clear();
                if (added)
                    session.Server.Users.TryRemove(2, out _);
            }
        }

        [AvaloniaFact]
        public async Task TheStatuses_TheTimePlayed_TheAnnouncements_AndTheNotifications()
        {
            var session = await TestSession.GetAsync();
            var ids = new List<int>();
            foreach (var (id, name) in new[] { (2, "Zoé"), (3, "Malik"), (4, "Inès") })
                if (session.Server.Users.TryAdd(id, new User { ID = id, Username = name, Role = PERMISSION_ROLE.USER, Activated = true, Progresses = Array.Empty<Progress>() }))
                    ids.Add(id);
            session.Server.Users.TryAdd(1, FakeGameVaultServer.Admin);
            var panel = session.Window.FindControl<ChatPanel>("uiChat")!;
            SanctuaryService.Current = session.Server.Url;
            try
            {
                MainWindowViewModel.Instance.IsChatOpen = true;
                await ChatService.Instance.RefreshAsync();
                await panel.LoadUsersAsync(again: true);
                // Zoé away, Malik busy in a retro game for 25 min, Inès invisible though GameVault says she plays
                ActivityService.Instance.Apply(new[]
                {
                    new PlayerActivity { UserId = 2, State = "ONLINE" }, new PlayerActivity { UserId = 3, State = "ONLINE" }, new PlayerActivity { UserId = 4, State = "PLAYING", GameId = 9790 },
                }, replaceAll: true);
                ActivityService.Instance.ApplyRetro(new[] { new RetroActivity { UserId = 3, Platform = "nes", RomId = 40, Name = "The Legend of Zelda", PlatformName = "NES", Since = DateTimeOffset.UtcNow.AddMinutes(-25) } }, replaceAll: true);
                // As the service says them (it is read again with the list)
                var statuses = new[] { new PlayerStatus { UserId = 2, State = "away" }, new PlayerStatus { UserId = 3, State = "busy" }, new PlayerStatus { UserId = 4, State = "offline" } };
                session.Server.ChatStatuses.AddRange(statuses);
                ActivityService.Instance.ApplyStatuses(statuses, replaceAll: true);
                // A reading of the list still on its way would bring the statuses before them
                await ActivityService.Instance.ReadStatusesAsync();
                await TestSession.WaitUntil(() => ActivityService.Instance.StateOf(2) == "away", TimeSpan.FromSeconds(5), "the statuses");
                Dispatcher.UIThread.RunJobs();
                panel.Rebuild();
                ChatEntry Friend(int id) => panel.Model.Friends.First(f => f.User?.ID == id);
                Assert.Equal((gamevault.Localization.Loc.T("Away"), 2), (Friend(2).Subtitle, Friend(2).Rank));
                Assert.Equal(gamevault.Localization.Loc.F("Playing {0} for {1}", "The Legend of Zelda (NES)", "25 min"), Friend(3).Subtitle);
                Assert.Same(gamevault.Converter.PresenceConverter.Busy, Friend(3).Dot);
                Assert.Equal((gamevault.Localization.Loc.T("Offline"), (Avalonia.Media.IBrush?)null), (Friend(4).Subtitle, Friend(4).Dot));
                Assert.Equal("1 h 05", gamevault.Converter.PresenceConverter.Duration(TimeSpan.FromMinutes(65)));
                Assert.Equal("2 h", gamevault.Converter.PresenceConverter.Duration(TimeSpan.FromMinutes(120)));

                // The player's own status: said to the service; "Do not disturb" stops the notifications
                Assert.True(ChatPanel.WantsNotifications());
                panel.SetStatus("busy");
                await TestSession.WaitUntil(() => session.Server.ChatPresenceSaid?.Contains("busy") == true, TimeSpan.FromSeconds(5), "the status said");
                Assert.Equal(gamevault.Localization.Loc.T("Do not disturb"), panel.Model.MyStatus);
                Assert.False(ChatPanel.WantsNotifications());
                panel.SetStatus("online");
                Assert.True(ChatPanel.WantsNotifications());

                Shot(session, "4-statuses");
                // The announcements: an administrator sees them, and their client tells a new game there
                Assert.True(panel.Model.HasNews);
                await panel.OpenAsync(panel.Model.News!);
                Assert.True(panel.Model.CanWrite);
                await ChatService.Instance.AnnounceAsync(new ChatGame { Kind = "pc", Id = 9790, Title = "Hades" });
                Assert.Contains(session.Server.ChatAnnounced, a => a.Contains("Hades"));
                Shot(session, "5-news");
            }
            finally
            {
                panel.Model.Open = null;
                ActivityService.ChosenState = "online";
                MainWindowViewModel.Instance.IsChatOpen = false;
                SanctuaryService.Current = SanctuaryService.Url;
                ActivityService.Instance.Apply(Array.Empty<PlayerActivity>(), replaceAll: true);
                ActivityService.Instance.ApplyRetro(null, replaceAll: true);
                session.Server.ChatStatuses.Clear();
                ActivityService.Instance.ApplyStatuses(null, replaceAll: true);
                gamevault.Converter.PresenceConverter.GameTitles.Remove(9790);
                foreach (int id in ids)
                    session.Server.Users.TryRemove(id, out _);
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
