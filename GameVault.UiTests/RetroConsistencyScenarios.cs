using Avalonia.Input;
using Avalonia.Headless.XUnit;
using GameVault.Core.Retro;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.UserControls.SettingsComponents;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>The retro games where the library's games are: downloads, time played, favorites, collections, search,
    /// storage, activity, offline, keyboard, new games, saves history.</summary>
    public class RetroConsistencyScenarios
    {
        /// <summary>RetroArch as if downloaded (the tests do not fetch 200 MB), then Zelda installed from the Retro page.</summary>
        private static async Task<(TestSession Session, RetroUserControl Page, RetroGame Zelda)> InstallZeldaAsync()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            RetroArchLayout layout = RetroArchManager.Layout;
            Directory.CreateDirectory(layout.AppDirectory);
            Directory.CreateDirectory(layout.CoresDirectory);
            File.WriteAllText(layout.Executable, "retroarch");
            File.WriteAllText(layout.VersionFile, RetroArchLayout.Version);
            File.WriteAllText(layout.CoreFile("mesen"), "core");
            var page = new RetroUserControl();
            await page.LoadPlatformsAsync();
            await TestSession.WaitUntil(() => page.Shown.Any(), TimeSpan.FromSeconds(10), "the NES games");
            RetroGame zelda = page.Shown.Single(g => g.Name == "Zelda");
            await RetroPlay.InstallAsync(zelda);
            RetroDownload download = DownloadsViewModel.Instance.RetroDownloads.Single();
            await TestSession.WaitUntil(() => download.IsDone || download.IsFailed, TimeSpan.FromSeconds(20), "the NES download");
            Assert.True(zelda.Installed, download.State);
            return (session, page, zelda);
        }

        private static void CleanUp(TestSession session)
        {
            SanctuaryService.Current = SanctuaryService.Url;
            DownloadsViewModel.Instance.RetroDownloads.Clear();
            session.Server.RetroMinutes.Clear();
            session.Server.RetroFavorites = "[]";
            session.Server.RetroSaves.Clear();
            session.Server.RetroSavesHistory.Clear();
            File.Delete(Path.Combine(Path.GetDirectoryName(LoginManager.Instance.GetUserProfile().CollectionsFile)!, "retro-collections.json"));
            try { Directory.Delete(RetroArchManager.Layout.Directory, true); } catch { }
            try { Directory.Delete(Path.Combine(session.LibraryRoot, "GameVault", "Retro"), true); } catch { }
        }

        [AvaloniaFact]
        public async Task ARetroGame_HasItsTime_Favorite_Collections_AndShowsInSearchStorageAndActivity()
        {
            var (session, page, zelda) = await InstallZeldaAsync();
            try
            {
                // Time played: counted at once, and kept by the service (profile, activity)
                await RetroLibrary.RecordPlayAsync(zelda, 42);
                Assert.Equal(42, session.Server.RetroMinutes["nes:40"]);
                Assert.Equal(42, zelda.MinutesPlayed);

                // Favorite: kept by the service, and a filter of the page
                zelda.IsFavorite = true;
                await TestSession.WaitUntil(() => session.Server.RetroFavorites.Contains("nes:40"), TimeSpan.FromSeconds(5), "the favorite sent");
                page.ViewModel.OnlyFavorites = true;
                Assert.Contains(zelda, page.Shown);
                zelda.IsFavorite = false;
                page.ViewModel.ApplyFilter();
                Assert.Empty(page.Shown);
                page.ViewModel.OnlyFavorites = false;

                // A collection of the retro games, as the library's
                RetroLibrary.Collections.Create("Co-op");
                RetroLibrary.Collections.SetMembership("Co-op", zelda.Id, true);
                page.ViewModel.LoadCollections();
                page.ViewModel.Collection = page.ViewModel.Collections.IndexOf("Co-op");
                Assert.Equal(new[] { zelda }, page.Shown);
                page.ViewModel.Collection = 0;

                // Quick search, Storage, recent activity, the player's profile
                Assert.Contains(QuickSearchUserControl.RetroGames(), i => i.Title == "Zelda");
                var storage = new StorageUserControl();
                await storage.LoadAsync();
                Assert.Contains(storage.Rows, r => r.Retro?.Name == "Zelda" && r.Usage.MinutesPlayed == 42);
                Assert.Contains(storage.Rows, r => r.IsEmulator);
                var activity = new ActivityUserControl();
                await activity.LoadAsync();
                Assert.Contains(activity.Items, i => i.Retro?.Name == "Zelda" && i.Text.Contains("Mario"));
                Assert.Single(await RetroLibrary.PlaysOfAsync(2));
            }
            finally { CleanUp(session); }
        }

        [AvaloniaFact]
        public async Task Offline_TheInstalledRetroGamesStay_AndTheKeyboardMovesTheSelection()
        {
            var (session, page, zelda) = await InstallZeldaAsync();
            try
            {
                // The keys of the library's shelf
                page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, Source = page });
                Assert.Same(zelda, page.ViewModel.SelectedGame);
                Assert.True(zelda.IsSelected);
                // Enter opens the game's page, as a click on its card
                page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = page });
                var shown = Assert.IsType<RetroGameViewUserControl>(MainWindowViewModel.Instance.ActiveControl);
                Assert.Same(zelda, shown.ViewModel.Game);
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Retro);
                page.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = page });
                Assert.Null(page.ViewModel.SelectedGame);

                // The service out of reach: the games installed here are listed from the disk
                SanctuaryService.Current = "http://127.0.0.1:9";
                await page.LoadPlatformsAsync();
                Assert.Equal("nes", Assert.Single(page.ViewModel.Platforms).Slug);
                RetroGame offline = Assert.Single(page.Shown);
                Assert.Equal(("Zelda", true), (offline.Name, offline.Installed));
                Assert.Equal(gamevault.Localization.Loc.T("Offline: only the retro games installed on this PC are shown."), page.ViewModel.Message);
            }
            finally { CleanUp(session); }
        }

        [AvaloniaFact]
        public async Task NewRetroGames_AreTold_AsTheLibrarysNewGames()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            bool notify = SettingsViewModel.Instance.NotifyNewGames;
            SettingsViewModel.Instance.NotifyNewGames = true;
            Preferences.DeleteKey(AppConfigKey.LastSeenRetroGameId, LoginManager.Instance.GetUserProfile().UserConfigFile);
            try
            {
                session.Server.RetroLatest.Add(new { id = 40, name = "Zelda", platform = "nes", platformName = "NES" });
                MainWindowViewModel.Instance.AppBarText = "";
                await RetroNewGamesNotifier.CheckAsync();// first check: remembered, not told
                Assert.Equal("", MainWindowViewModel.Instance.AppBarText);

                session.Server.RetroLatest.Insert(0, new { id = 41, name = "Metroid", platform = "nes", platformName = "NES" });
                session.Server.RetroLatest.Insert(0, new { id = 42, name = "Halo", platform = "xbox", platformName = "Xbox" });
                await RetroNewGamesNotifier.CheckAsync();
                Assert.Contains("Metroid (NES)", MainWindowViewModel.Instance.AppBarText);
                Assert.DoesNotContain("Halo", MainWindowViewModel.Instance.AppBarText);
            }
            finally
            {
                SettingsViewModel.Instance.NotifyNewGames = notify;
                session.Server.RetroLatest.Clear();
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }

        [AvaloniaFact]
        public async Task TheGamesPage_ShowsItsFriends_AndRestoresAnOlderVersionOfItsSaves()
        {
            var (session, page, zelda) = await InstallZeldaAsync();
            bool cloud = SettingsViewModel.Instance.CloudSaves;
            string saves = RetroArchManager.SavesFolder;
            try
            {
                await RetroLibrary.RecordPlayAsync(zelda, 42);
                Directory.CreateDirectory(Path.Combine(saves, "Mesen"));
                File.WriteAllText(Path.Combine(saves, "Mesen", "Zelda (E).srm"), "today");

                // Cloud saves off: nothing taken from the service before the game
                SettingsViewModel.Instance.CloudSaves = false;
                session.Server.RetroSaves["nes-40"] = (FakeGameVaultServer.Zip(("saves/Mesen/Zelda (E).srm", "other PC")), 9000);
                Assert.Null(await RetroCloudSaves.TakeAsync(zelda, _ => { }));
                Assert.Equal("today", File.ReadAllText(Path.Combine(saves, "Mesen", "Zelda (E).srm")));

                // The game's page: its friends, its saves
                var view = new RetroGameViewUserControl(zelda);
                await view.LoadAsync();
                RetroPlayerRow friend = Assert.Single(view.ViewModel.Players);
                Assert.Equal("Mario", friend.Username);
                Assert.Contains("1", view.ViewModel.SavesText);

                // Its history: an older version put back, and sent as the current one
                session.Server.RetroSavesHistory["nes-40-1000.zip"] = (1000, FakeGameVaultServer.Zip(("saves/Mesen/Zelda (E).srm", "last week")));
                var history = new RetroSaveHistoryUserControl(zelda, null);
                await history.LoadAsync();
                Assert.Equal(new[] { true, false }, history.Rows.Select(r => r.IsCurrent));
                await history.RestoreAsync(history.Rows[1]);
                Assert.Equal("last week", File.ReadAllText(Path.Combine(saves, "Mesen", "Zelda (E).srm")));
                using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(session.Server.RetroSaves["nes-40"].Zip)))
                    Assert.Equal("last week", new StreamReader(zip.GetEntry("saves/Mesen/Zelda (E).srm")!.Open()).ReadToEnd());
                Assert.Single(Directory.GetFiles(RetroArchManager.SaveBackupsFolder, "nes-40-*.zip"));
            }
            finally
            {
                SettingsViewModel.Instance.CloudSaves = cloud;
                CleanUp(session);
            }
        }

        [AvaloniaFact]
        public async Task WithALinkedRommAccount_TheSavesGoToRomm_AndRommsOwnSavesComeBack()
        {
            var (session, page, zelda) = await InstallZeldaAsync();
            bool cloud = SettingsViewModel.Instance.CloudSaves;
            SettingsViewModel.Instance.CloudSaves = true;
            session.Server.RommLinkable = true;
            RommAccount.ForgetUrl();
            string saves = RetroArchManager.SavesFolder;
            string states = RetroArchManager.Layout.StatesDirectory(RetroArchManager.Player);
            try
            {
                // Linked as RomM links its devices: a code approved on RomM's page (pending once here)
                var settings = new RetroSettingsUserControl();
                await settings.LinkRommAsync(open: false);
                Assert.True(RommAccount.IsLinked);
                Assert.Equal("admin", RommAccount.Username);

                // A save made on RomM's web page: this PC has none of the game, it is taken before the game
                session.Server.RommSaves.Enqueue(new FakeGameVaultServer.RommSave(100, 40, "Zelda (E).srm", null, "mesen", DateTime.UtcNow.AddDays(-1), "from RomM's web page"u8.ToArray()));
                await RetroCloudSaves.TakeAsync(zelda, _ => { });
                string main = Path.Combine(saves, RetroArchManager.Layout.SaveFolderName("mesen"), "Zelda (E).srm");
                Assert.Equal("from RomM's web page", File.ReadAllText(main));

                // Played here: the main save goes on its own (RomM's emulator reads it), the rest zipped, in SanctuaryVault's slots
                File.WriteAllText(main, "played here");
                Directory.CreateDirectory(Path.Combine(states, "Mesen"));
                File.WriteAllText(Path.Combine(states, "Mesen", "Zelda (E).state1"), "a state");
                await RetroCloudSaves.SendAsync(zelda);
                var sent = session.Server.RommSaves.Where(s => s.Slot != null).ToList();
                Assert.Equal(new[] { "sanctuaryvault", "sanctuaryvault-data" }, sent.Select(s => s.Slot));
                Assert.Equal("played here", System.Text.Encoding.ASCII.GetString(sent[0].Content));
                Assert.Equal(("Zelda (E).srm", "mesen"), (sent[0].FileName, sent[0].Emulator));

                // The history: every save of the game in RomM; the web page's one restored, the state kept, then sent as the current one
                var history = new RetroSaveHistoryUserControl(zelda, null);
                await history.LoadAsync();
                Assert.Equal(3, history.Rows.Count);
                RetroSaveVersionRow web = history.Rows.Single(r => r.Version.Romm!.Slot == null);
                await history.RestoreAsync(web);
                Assert.Equal("from RomM's web page", File.ReadAllText(main));
                Assert.Equal("a state", File.ReadAllText(Path.Combine(states, "Mesen", "Zelda (E).state1")));
                Assert.Equal("from RomM's web page", System.Text.Encoding.ASCII.GetString(session.Server.RommSaves.Last(s => s.Slot == "sanctuaryvault").Content));

                // Unlinked: back to SanctuaryVault, which gets them all at the next sync
                RommAccount.Unlink();
                Assert.Equal("SanctuaryVault", RetroCloudSaves.Destination);
                await RetroCloudSaves.SendAsync(zelda);
                Assert.True(session.Server.RetroSaves.ContainsKey("nes-40"));
            }
            finally
            {
                RommAccount.Unlink();
                RommAccount.ForgetUrl();
                session.Server.RommLinkable = false;
                session.Server.RommSaves.Clear();
                SettingsViewModel.Instance.CloudSaves = cloud;
                CleanUp(session);
            }
        }
    }
}
