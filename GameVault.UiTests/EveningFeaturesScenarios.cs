using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using gamevault;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Library;

namespace GameVault.UiTests
{
    /// <summary>Storage, "What to play tonight?", the players' activity and the cloud save history.</summary>
    public class EveningFeaturesScenarios
    {
        private static Game NewGame(int id, string title, params string[] genres) => new()
        {
            ID = id, Title = title, SortTitle = title.ToLowerInvariant(), Type = GameType.WINDOWS_PORTABLE, Path = $"/files/{title}.zip", Size = "1024", EntityVersion = 1,
            Metadata = new GameMetadata { Title = title, Genres = genres.Select(g => new GenreMetadata { Name = g }).ToList(), AveragePlaytime = 300 },
            Progresses = new List<Progress>(),
        };

        /// <summary>An installed game: its folder with <paramref name="bytes"/> of files.</summary>
        private static string Install(TestSession session, Game game, int bytes)
        {
            string directory = Path.Combine(session.LibraryRoot, "GameVault", "Installations", $"({game.ID}){game.Title}");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "game.bin"), new byte[bytes]);
            InstallViewModel.Instance.InstalledGames.Add(new KeyValuePair<Game, string>(game, directory));
            return directory;
        }

        private static void Uninstall(params int[] ids)
        {
            foreach (var install in InstallViewModel.Instance.InstalledGames.Where(g => ids.Contains(g.Key.ID)).ToList())
                InstallViewModel.Instance.InstalledGames.Remove(install);
            foreach (int id in ids)
                InstallViewModel.Instance.PlayRecords.Remove(id);
        }

        [AvaloniaFact]
        public async Task Storage_ListsTheInstalledGames_BiggestFirst_AndFlagsTheForgottenOnes()
        {
            var session = await TestSession.GetAsync();
            Game big = NewGame(9711, "Storage Giant"), small = NewGame(9712, "Storage Pebble");
            Install(session, big, 3_000_000);
            Install(session, small, 20_000);
            InstallViewModel.Instance.PlayRecords[9711] = new PlayRecord(9711, DateTime.UtcNow.AddDays(-200), 600);
            InstallViewModel.Instance.PlayRecords[9712] = new PlayRecord(9712, DateTime.UtcNow.AddDays(-1), 30);
            try
            {
                var storage = new StorageUserControl();
                await storage.LoadAsync();
                var rows = storage.Rows.Where(r => r.Usage.Id is 9711 or 9712).ToList();
                Assert.Equal(new[] { 9711, 9712 }, rows.Select(r => r.Usage.Id));
                Assert.True(rows[0].Usage.Size >= 3_000_000);
                Assert.True(rows[0].IsIdle);
                Assert.False(rows[1].IsIdle);
            }
            finally
            {
                Uninstall(9711, 9712);
            }
        }

        [AvaloniaFact]
        public async Task Tonight_ProposesOnlyGamesMatchingTheFilters()
        {
            var session = await TestSession.GetAsync();
            Game played = NewGame(9721, "Tonight Played", "Puzzle"), fresh = NewGame(9722, "Tonight Fresh", "Racing");
            session.Server.AddGame(played, new byte[16]);
            session.Server.AddGame(fresh, new byte[16]);
            Install(session, played, 1000);
            Install(session, fresh, 1000);
            InstallViewModel.Instance.PlayRecords[9721] = new PlayRecord(9721, DateTime.UtcNow.AddDays(-3), 120);
            try
            {
                var tonight = new TonightUserControl();
                await tonight.LoadAsync();
                tonight.FindControl<Avalonia.Controls.Primitives.ToggleButton>("uiNeverPlayed")!.IsChecked = true;
                var genre = tonight.FindControl<Avalonia.Controls.ComboBox>("uiGenre")!;
                genre.SelectedItem = "Racing";
                for (int i = 0; i < 10; i++)
                {
                    tonight.Propose();
                    Assert.Equal(9722, tonight.Shown?.ID);
                }
                genre.SelectedItem = "Puzzle";
                tonight.Propose();
                Assert.Null(tonight.Shown);// the puzzle game was played
            }
            finally
            {
                Uninstall(9721, 9722);
            }
        }

        [AvaloniaFact]
        public async Task Community_ShowsWhatThePlayersPlayNow_AndLately()
        {
            var session = await TestSession.GetAsync();
            Game game = NewGame(9731, "Activity Arena");
            session.Server.AddGame(game, new byte[16]);
            var zoe = new User { ID = 2, Username = "Zoé", Role = PERMISSION_ROLE.USER, Activated = true, Progresses = Array.Empty<Progress>() };
            game.Metadata!.Cover = new Media { ID = 97311 };
            // Like the server: the progress brings its game without the metadata
            var bare = new Game { ID = game.ID, Title = game.Title, Type = game.Type, EntityVersion = 1 };
            session.Server.Progresses.Enqueue(new Progress { ID = 97310, Game = bare, User = zoe, MinutesPlayed = 90, State = "COMPLETED", LastPlayedAt = DateTime.UtcNow.AddHours(-2) });

            var community = MainWindowViewModel.Instance.Community;
            var model = (CommunityViewModel)community.DataContext!;
            await community.LoadRecentActivityAsync();
            var line = Assert.Single(model.RecentActivity, a => a.Game.ID == 9731);
            Assert.Contains("Zoé", line.Text);
            Assert.Contains("Activity Arena", line.Text);
            Assert.Equal(97311, line.Game.Metadata?.Cover?.ID);// the cover of the feed

            // Zoé starts the game: the server sends her activity
            ActivityService.Instance.Apply(new[] { new PlayerActivity { UserId = 2, State = "PLAYING", GameId = 9731 } }, replaceAll: true);
            await community.ActivityChangedAsync();
            var presence = new PresenceConverter();
            Assert.Equal(gamevault.Localization.Loc.F("Playing {0}", "Activity Arena"), presence.Convert(new object?[] { zoe, model.ActivityVersion }, typeof(string), "text", System.Globalization.CultureInfo.CurrentCulture));
            Assert.Equal(true, presence.Convert(new object?[] { zoe, 0 }, typeof(bool), "hasdot", System.Globalization.CultureInfo.CurrentCulture));
            ActivityService.Instance.Apply(Array.Empty<PlayerActivity>(), replaceAll: true);
            Assert.Equal(false, presence.Convert(new object?[] { zoe, 0 }, typeof(bool), "hasdot", System.Globalization.CultureInfo.CurrentCulture));
        }

        [AvaloniaFact]
        public async Task GamePage_ShowsTheFriendsWhoPlayedIt_ThoseOnItNowFirst()
        {
            var session = await TestSession.GetAsync();
            Game game = NewGame(9735, "Friends Fest");
            User Player(int id, string name) => new() { ID = id, Username = name, Role = PERMISSION_ROLE.USER, Activated = true };
            game.Progresses = new List<Progress>
            {
                new() { ID = 1, User = Player(2, "Zoé"), MinutesPlayed = 90, State = "COMPLETED", LastPlayedAt = DateTime.UtcNow.AddDays(-2) },
                new() { ID = 2, User = Player(3, "Léo"), MinutesPlayed = 30, State = "PLAYING", LastPlayedAt = DateTime.UtcNow.AddDays(-1) },
                new() { ID = 3, User = Player(4, "Max"), MinutesPlayed = 0, State = "UNPLAYED" },
            };
            session.Server.AddGame(game, new byte[16]);
            ActivityService.Instance.Apply(new[] { new PlayerActivity { UserId = 2, State = "PLAYING", GameId = 9735 } }, replaceAll: true);
            try
            {
                var page = new GameViewUserControl(game, true);
                MainWindowViewModel.Instance.SetActiveControl(page);
                var model = (GameViewViewModel)page.DataContext!;
                await TestSession.WaitUntil(() => model.UserProgresses != null, TimeSpan.FromSeconds(30), "the players of the game");

                // Zoé plays it now, then the most recent; Max never played it
                Assert.Equal(new[] { "Zoé", "Léo" }, model.UserProgresses!.Select(p => p.User!.Username));
                Assert.True(model.UserProgresses![0].PlayingNow);

                // She stops: the most recent first
                ActivityService.Instance.Apply(Array.Empty<PlayerActivity>(), replaceAll: true);
                await TestSession.WaitUntil(() => model.UserProgresses![0].User!.Username == "Léo", TimeSpan.FromSeconds(10), "the order to follow the activity");
            }
            finally
            {
                ActivityService.Instance.Apply(Array.Empty<PlayerActivity>(), replaceAll: true);
            }
        }

        [AvaloniaFact]
        public async Task SaveHistory_ListsTheCloudSaves_NewestAsTheCurrentOne()
        {
            var session = await TestSession.GetAsync();
            Game game = NewGame(9741, "History Quest");
            session.Server.AddGame(game, new byte[16]);
            string directory = Install(session, game, 100);
            Preferences.Set(AppConfigKey.InstallationId, "aaaa-1111", Path.Combine(directory, "gamevault-exec"));
            session.Server.SaveVersions[9741] = new()
            {
                ["1760000000000_aaaa-1111.zip"] = new byte[10],
                ["1760200000000_bbbb-2222.zip"] = new byte[30],
                ["1760100000000_aaaa-1111.zip"] = new byte[20],
            };
            SanctuaryService.Current = session.Server.Url;
            try
            {
                var history = new SaveHistoryUserControl(game, directory);
                await history.LoadAsync();
                Assert.Equal(new[] { "1760200000000_bbbb-2222.zip", "1760100000000_aaaa-1111.zip", "1760000000000_aaaa-1111.zip" }, history.Rows.Select(r => r.Version.Name));
                Assert.True(history.Rows[0].IsCurrent);
                Assert.False(history.Rows[1].IsCurrent);
                Assert.Contains(gamevault.Localization.Loc.T("from another computer"), history.Rows[0].Details);
                Assert.Contains(gamevault.Localization.Loc.T("from this computer"), history.Rows[1].Details);
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                Uninstall(9741);
            }
        }
    }
}
