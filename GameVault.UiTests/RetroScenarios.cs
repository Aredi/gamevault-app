using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.UserControls;
using gamevault.ViewModels;
using System.Text;

namespace GameVault.UiTests
{
    /// <summary>The games of the other platforms: listed from the SanctuaryVault service, installed into the library folder.</summary>
    public class RetroScenarios
    {
        [Fact]
        public void TheDownloadSpeed_IsShownInFrench()
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                // A speed is not a whole number of bytes: it was shown "ERR/s"
                Assert.Equal("8,39 " + gamevault.Localization.Loc.T("MB"), RetroDownload.Size(8388608.5 / 1000 * 1000, 1000));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        }

        [Fact]
        public async Task SevenZipProgress_IsRead_ThoughItIsRedrawnWithBackspaces()
        {
            // As 7-Zip writes it: no new line until the end
            string output = "\n7-Zip 23.01\n\n  0M Scan\b\b\b\b\b\b\b\b         \b\b\b\b\b\b\b\b\b  12% 3 - assets/a.png\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b 12% 4 - b\b\b\b\b\b\b\b\b\b\b 45% 9 - c\b\b\b\b\b\b\b\b\b\b\b100%\nEverything is Ok\n";
            var seen = new List<int>();
            await SevenZipHelper.ReadProgressAsync(new StringReader(output), seen.Add);
            Assert.Equal(new[] { 12, 45, 100 }, seen);
        }

        [AvaloniaFact]
        public async Task AGamesSaves_FollowThePlayer_FromPcToPc()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            string saves = RetroArchManager.Layout.SavesDirectory(gamevault.Helper.LoginManager.Instance.GetCurrentUser()!.ID.ToString());
            SettingsViewModel.Instance.CloudSaves = true;
            var zelda = new RetroGame { Id = 40, Name = "Zelda", FileName = "Zelda (E).nes", Platform = new RetroPlatform { Slug = "nes", Name = "NES" } };
            try
            {
                // Another PC sent this game's save: it comes here before the game, the others' saves stay
                Directory.CreateDirectory(Path.Combine(saves, "Mesen"));
                File.WriteAllText(Path.Combine(saves, "Mesen", "Metroid.srm"), "another game");
                session.Server.RetroSaves["nes-40"] = (FakeGameVaultServer.Zip(("saves/Mesen/Zelda (E).srm", "from the other PC")), 5000);
                Assert.Null(await RetroCloudSaves.TakeAsync(zelda, _ => { }));
                Assert.Equal("from the other PC", File.ReadAllText(Path.Combine(saves, "Mesen", "Zelda (E).srm")));

                // Played here: the changed saves go up after the game, and only then; the other game is not in them
                File.WriteAllText(Path.Combine(saves, "Mesen", "Zelda (E).srm"), "played here");
                await RetroCloudSaves.SendAsync(zelda);
                Assert.Equal(6000, session.Server.RetroSaves["nes-40"].SavedAt);
                using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(session.Server.RetroSaves["nes-40"].Zip)))
                    Assert.Equal(new[] { "saves/Mesen/Zelda (E).srm" }, zip.Entries.Select(e => e.FullName));
                await RetroCloudSaves.SendAsync(zelda);
                Assert.Equal(6000, session.Server.RetroSaves["nes-40"].SavedAt);
                // "Upload" on the game's page sends anyway
                await RetroCloudSaves.SendAsync(zelda, force: true);
                Assert.Equal(7000, session.Server.RetroSaves["nes-40"].SavedAt);
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                session.Server.RetroSaves.Clear();
                Directory.Delete(RetroArchManager.Layout.Directory, true);
            }
        }

        [AvaloniaFact]
        public async Task AllThePlatforms_TheirSeries_TheFilters_AndTheQuickSearch_ReachEveryConsole()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            RetroUserControl page = MainWindowViewModel.Instance.Retro;
            try
            {
                await page.LoadPlatformsAsync();
                Assert.Equal("nes", page.ViewModel.Platforms.Single(p => p.IsSelected).Slug);
                // The quick search finds the games of the platforms not shown
                var quick = new QuickSearchUserControl();
                quick.Open();
                await TestSession.WaitUntil(() => QuickSearchUserControl.RetroGames().Any(i => i.Title == "Castlevania"), TimeSpan.FromSeconds(10), "the PS1 games in the quick search");
                quick.Close();

                // "All": every platform at once
                await page.SelectAllAsync();
                Assert.True(page.ViewModel.ShowsAll);
                Assert.Contains(page.Shown, g => g.Name == "Zelda");
                Assert.Contains(page.Shown, g => g.Name == "Castlevania");

                // A series of RomM, across the platforms
                await TestSession.WaitUntil(() => page.ViewModel.Collections.Any(c => c.StartsWith("Castlevania")), TimeSpan.FromSeconds(10), "the series");
                page.ViewModel.Collection = page.ViewModel.Collections.FindIndex(c => c.StartsWith("Castlevania"));
                Assert.Equal(new[] { "Castlevania" }, page.Shown.Select(g => g.Name));
                page.ViewModel.Collection = 0;

                // The filters: genre, players, decade, rating
                RetroGame zelda = page.ViewModel.AllGames.First(g => g.Name == "Zelda"), castlevania = page.ViewModel.AllGames.First(g => g.Name == "Castlevania");
                zelda.Genres = new[] { "Adventure" };
                zelda.Players = "1";
                zelda.Rating = 92;
                zelda.Released = new DateTimeOffset(1986, 2, 21, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                castlevania.Genres = new[] { "Platform", "Adventure" };
                castlevania.Players = "1-2";
                castlevania.Rating = 75;
                castlevania.Released = new DateTimeOffset(1997, 3, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                page.ViewModel.AllGames = page.ViewModel.AllGames;
                Assert.Equal("Adventure", page.ViewModel.Genres[1]);
                page.ViewModel.Genre = page.ViewModel.Genres.IndexOf("Platform");
                Assert.Equal(new[] { "Castlevania" }, page.Shown.Select(g => g.Name));
                page.ViewModel.ClearFilters();
                page.ViewModel.Players = 1;
                Assert.Equal(new[] { "Castlevania" }, page.Shown.Select(g => g.Name));
                page.ViewModel.ClearFilters();
                page.ViewModel.Decade = page.ViewModel.Decades.FindIndex(d => d.Contains("1980"));
                Assert.Equal(new[] { "Zelda" }, page.Shown.Select(g => g.Name));
                page.ViewModel.ClearFilters();
                page.ViewModel.Rating = 3;
                Assert.Equal(new[] { "Zelda" }, page.Shown.Select(g => g.Name));
                page.ViewModel.ClearFilters();
                Assert.False(page.ViewModel.HasFilters);

                // One platform again: "All" is left
                await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "nes"));
                Assert.False(page.ViewModel.ShowsAll);
                Assert.All(page.Shown, g => Assert.Equal("nes", g.Platform.Slug));

                // Its chip clicked again: back to "All"
                await page.TogglePlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "nes"));
                Assert.True(page.ViewModel.ShowsAll);
                Assert.Contains(page.Shown, g => g.Name == "Castlevania");
                await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "nes"));
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                page.ViewModel.ClearFilters();
            }
        }

        [AvaloniaFact]
        public async Task AnotherPlatform_ChosenFarDownTheGrid_LeavesNoCardDrawnOverTheOthers()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            try
            {
                MainWindowViewModel.Instance.SetActiveControl(MainControl.Retro);
                RetroUserControl page = MainWindowViewModel.Instance.Retro;
                await page.EnsureLoadedAsync();
                // Square boxes, then 3:4 ones: the grid measured its cards with an old one and left it where it was, bigger, over the others
                var gba = new RetroPlatform { Id = 7, Slug = "gba", Name = "GBA" };
                var wii = new RetroPlatform { Id = 15, Slug = "wii", Name = "Wii" };
                List<RetroGame> Games(RetroPlatform platform) => Enumerable.Range(1, 80).Select(i => new RetroGame { Id = platform.Id * 1000 + i, Name = $"{platform.Name} {i:00}", FileName = "x", Platform = platform }).ToList();
                page.ViewModel.Platforms = new() { gba, wii };
                page.SizeCards(gba);
                page.ViewModel.AllGames = Games(gba);
                var scroll = page.GetLogicalDescendants().OfType<ScrollViewer>().First(s => s.Name == "uiScroll");
                var grid = page.GetLogicalDescendants().OfType<ItemsRepeater>().First(r => r.Name == "uiGames");
                for (int i = 0; i < 4; i++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + 400);
                }
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(scroll.Offset.Y > 1000);

                page.SizeCards(wii);
                page.ViewModel.AllGames = new();
                page.ViewModel.AllGames = Games(wii);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var cards = grid.Children.Where(c => c.Bounds.X >= 0 && c.Bounds.Width > 0).ToList();
                Assert.NotEmpty(cards);
                foreach (var card in cards)
                {
                    Assert.Equal("wii", ((RetroGame)card.DataContext!).Platform.Slug);
                    Assert.DoesNotContain(cards, other => other != card && other.Bounds.Intersects(card.Bounds));
                }
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                MainWindowViewModel.Instance.Retro.ViewModel.AllGames = new();
            }
        }

        [AvaloniaFact]
        public async Task APlatformsGames_AreAskedOnce_TheLastPlatformChosenIsShown_AndARetryDoesNotDownloadAgain()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            RetroArchLayout layout = RetroArchManager.Layout;
            Directory.CreateDirectory(layout.AppDirectory);
            Directory.CreateDirectory(layout.CoresDirectory);
            File.WriteAllText(layout.Executable, "");
            File.WriteAllText(layout.VersionFile, RetroArchLayout.Version);
            File.WriteAllText(layout.CoreFile("mesen"), "");
            session.Server.NesBrokenGame = true;
            int Asked(string path) => session.Server.Requests.Count(r => r == $"GET {path}");
            try
            {
                var page = new RetroUserControl();
                await page.LoadPlatformsAsync();
                RetroPlatform nes = page.ViewModel.Platforms.Single(p => p.Slug == "nes"), psx = page.ViewModel.Platforms.Single(p => p.Slug == "psx");
                int nesAsked = Asked("/retro/platforms/3/games");

                // The PS1 games come late: the NES, chosen meanwhile, stays shown with its games (asked once)
                session.Server.PsxListDelay = TimeSpan.FromSeconds(1.5);
                Task late = page.SelectPlatformAsync(psx);
                await page.SelectPlatformAsync(nes);
                await late;
                Assert.True(nes.IsSelected);
                Assert.All(page.Shown, g => Assert.Equal("nes", g.Platform.Slug));
                Assert.Equal(nesAsked, Asked("/retro/platforms/3/games"));

                // Its preparation fails (no file the emulator starts): tried again without downloading it again
                RetroGame readme = page.Shown.Single(g => g.Id == 41);
                await RetroPlay.InstallAsync(readme);
                RetroDownload download = Assert.Single(DownloadsViewModel.Instance.RetroDownloads);
                await TestSession.WaitUntil(() => download.IsFailed, TimeSpan.FromSeconds(20), "the failed preparation");
                download.Retry();
                await TestSession.WaitUntil(() => download.IsFailed && !download.IsRunning, TimeSpan.FromSeconds(20), "the retry");
                Assert.Equal(1, Asked("/retro/games/41/content"));
            }
            finally
            {
                session.Server.NesBrokenGame = false;
                session.Server.PsxListDelay = TimeSpan.Zero;
                SanctuaryService.Current = SanctuaryService.Url;
                DownloadsViewModel.Instance.RetroDownloads.Clear();
                Directory.Delete(layout.Directory, true);
                string folder = Path.Combine(session.LibraryRoot, "GameVault", "Retro", "nes", "41");
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [AvaloniaFact]
        public async Task ARetroGame_IsListedAndInstalled_OnlyForThePlatformsRetroArchPlays()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            // RetroArch and the NES core as if already downloaded: the test does not fetch 200 MB
            RetroArchLayout layout = RetroArchManager.Layout;
            Directory.CreateDirectory(layout.AppDirectory);
            Directory.CreateDirectory(layout.CoresDirectory);
            File.WriteAllText(layout.Executable, "");
            File.WriteAllText(layout.VersionFile, RetroArchLayout.Version);
            File.WriteAllText(layout.CoreFile("mesen"), "");
            try
            {
                var page = new RetroUserControl();
                await page.LoadPlatformsAsync();
                await TestSession.WaitUntil(() => page.Shown.Any(), TimeSpan.FromSeconds(10), "the NES games");

                Assert.Equal(new[] { "nes", "ps3", "psx", "switch" }, page.ViewModel.Platforms.Select(p => p.Slug).Order());
                RetroGame zelda = Assert.Single(page.Shown);
                Assert.Equal(("Zelda", "nes", false), (zelda.Name, zelda.Platform.Slug, zelda.Installed));

                // Into the Downloads page, as the library's games, and played when it is complete
                await RetroPlay.InstallAsync(zelda);
                RetroDownload download = Assert.Single(DownloadsViewModel.Instance.RetroDownloads);
                await TestSession.WaitUntil(() => download.IsDone || download.IsFailed, TimeSpan.FromSeconds(20), "the NES download");
                Assert.True(zelda.Installed, download.State);
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(zelda.RomFile)!, RetroDownload.InfoFile)));
                Assert.Equal(Path.Combine(session.LibraryRoot, "GameVault", "Retro", "nes", "40", "Zelda (E).nes"), zelda.RomFile);
                Assert.Equal(FakeGameVaultServer.RetroRom, File.ReadAllBytes(zelda.RomFile!));
                Assert.Contains("input_quit_gamepad_combo = \"4\"", File.ReadAllText(layout.ConfigFile));

                // A CD game: its zip unpacked, the cue started, the platform's BIOS fetched from RomM
                File.WriteAllText(layout.CoreFile("swanstation"), "");
                await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "psx"));
                await TestSession.WaitUntil(() => page.Shown.Any(g => g.Name == "Castlevania"), TimeSpan.FromSeconds(10), "the PS1 games");
                RetroGame castlevania = Assert.Single(page.Shown);
                await RetroPlay.InstallAsync(castlevania);
                // The page reloads its games (Reload, then the platform again): the download is not forgotten
                await page.LoadPlatformsAsync();
                await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "psx"));
                RetroGame shownAgain = Assert.Single(page.Shown);
                Assert.NotSame(castlevania, shownAgain);
                RetroDownload cd = DownloadsViewModel.Instance.RetroDownloads.Single(d => d.Game.Name == "Castlevania");
                await TestSession.WaitUntil(() => cd.IsDone || cd.IsFailed, TimeSpan.FromSeconds(20), "the PS1 download");
                Assert.True(shownAgain.Installed, cd.State);
                Assert.False(shownAgain.Busy);
                Assert.Equal(2, DownloadsViewModel.Instance.RetroDownloads.Count);
                Assert.Equal("Castlevania.cue", Path.GetFileName(castlevania.RomFile));
                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(castlevania.RomFile)!, "Castlevania.zip")));
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(layout.SystemDirectory, "scph5501.bin")));
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                DownloadsViewModel.Instance.RetroDownloads.Clear();
                Directory.Delete(layout.Directory, true);
                Directory.Delete(Path.Combine(session.LibraryRoot, "GameVault", "Retro"), true);
            }
        }

        [AvaloniaFact]
        public async Task APs3Game_IsUnpackedFromItsFolder_AndRpcs3StartsItsFolder_WithTheFirmwareFromRomm()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            // RPCS3 as if already downloaded (the test does not fetch 90 MB), its firmware not yet installed
            Rpcs3Layout layout = StandaloneEmulator.Rpcs3.Layout;
            Directory.CreateDirectory(Path.GetDirectoryName(layout.Executable)!);
            File.WriteAllText(layout.Executable, "");
            File.WriteAllText(layout.VersionFile, layout.Version);
            try
            {
                var page = new RetroUserControl();
                await page.LoadPlatformsAsync();
                await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "ps3"));
                await TestSession.WaitUntil(() => page.Shown.Count() == 2, TimeSpan.FromSeconds(10), "the PS3 games");

                // A package needs the console's installation and its licence: refused before it comes down
                RetroGame package = page.Shown.Single(g => g.Name == "Fatal Frame");
                await RetroPlay.InstallAsync(package);
                Assert.Contains(".pkg", package.Status);
                Assert.Empty(DownloadsViewModel.Instance.RetroDownloads);

                // The folder comes zipped and is unpacked; without the firmware in RomM, the game waits for it
                RetroGame infamous = page.Shown.Single(g => g.Name == "inFamous");
                await RetroPlay.InstallAsync(infamous);
                RetroDownload download = Assert.Single(DownloadsViewModel.Instance.RetroDownloads);
                await TestSession.WaitUntil(() => download.IsDone || download.IsFailed, TimeSpan.FromSeconds(20), "the PS3 download");
                Assert.True(infamous.Installed, download.State);
                Assert.Contains("PS3UPDAT.PUP", download.State);
                string folder = Path.Combine(session.LibraryRoot, "GameVault", "Retro", "ps3", "60", "Infamous");
                Assert.Equal(Path.Combine(folder, "PS3_GAME", "USRDIR", "EBOOT.BIN"), infamous.RomFile);
                Assert.Equal(new[] { "--no-gui", "--user-id", Rpcs3Layout.UserId(RetroArchManager.Player), folder }, layout.Arguments(RetroArchManager.Player, infamous.RomFile!));
                Assert.Contains("infoBoxEnabledWelcome=false", File.ReadAllText(layout.SettingsFiles.First()));

                // Its saves are what it writes in the console's storage, for the player
                var saves = RetroCloudSaves.Of(infamous);
                var before = saves.Snapshot();
                string save = Path.Combine(layout.SavesDirectory(RetroArchManager.Player), "BCUS98119-SAVE", "SYS-DATA");
                Directory.CreateDirectory(Path.GetDirectoryName(save)!);
                File.WriteAllText(save, "chapter 2");
                saves.Track(before);
                Assert.Contains(save, saves.Files());

                // Uninstalled with the other emulators: the console's storage, with the saves, stays
                await RetroArchManager.UninstallAsync();
                Assert.False(File.Exists(layout.Executable));
                Assert.True(File.Exists(save));
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                DownloadsViewModel.Instance.RetroDownloads.Clear();
                Directory.Delete(layout.Directory, true);
                Directory.Delete(Path.Combine(session.LibraryRoot, "GameVault", "Retro"), true);
            }
        }

        [AvaloniaFact]
        public async Task ASwitchGame_IsUnpackedFromItsFolder_AndEdenGetsTheKeysAndFirmwareFromRomm_NothingToAnswer()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            // Eden as if already downloaded (the test does not fetch 70 MB), its keys and firmware not yet there
            EdenLayout layout = StandaloneEmulator.Eden.Layout;
            Directory.CreateDirectory(Path.GetDirectoryName(layout.Executable)!);
            File.WriteAllText(layout.Executable, "");
            File.WriteAllText(layout.VersionFile, layout.Version);
            try
            {
                var page = new RetroUserControl();
                await page.LoadPlatformsAsync();
                await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "switch"));
                await TestSession.WaitUntil(() => page.Shown.Count() == 3, TimeSpan.FromSeconds(10), "the Switch games");

                // The folder comes zipped and is unpacked; without the keys in RomM, the game waits for them
                RetroGame game = page.Shown.Single(g => g.Name == "1-2-Switch");
                await RetroPlay.InstallAsync(game);
                RetroDownload download = Assert.Single(DownloadsViewModel.Instance.RetroDownloads);
                await TestSession.WaitUntil(() => download.IsDone || download.IsFailed, TimeSpan.FromSeconds(20), "the Switch download");
                Assert.True(game.Installed, download.State);

                // A game compressed by nsz: an .nsp again once downloaded, the .nsz gone
                RetroGame compressed = page.Shown.Single(g => g.Name == "Compressed Game");
                await RetroPlay.InstallAsync(compressed);
                RetroDownload compressedDownload = DownloadsViewModel.Instance.RetroDownloads.Single(d => d.Game.Id == compressed.Id);
                await TestSession.WaitUntil(() => compressedDownload.IsDone || compressedDownload.IsFailed, TimeSpan.FromSeconds(20), "the compressed game");
                Assert.True(compressed.Installed, compressedDownload.State);
                Assert.Equal("Compressed Game.nsp", Path.GetFileName(compressed.RomFile));
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(compressed.RomFile)!, "*.nsz"));

                // A DLC is installed as a game, and not started: Eden takes it with its game
                RetroGame dlc = page.Shown.Single(g => g.Name.Contains("DLC"));
                await RetroPlay.InstallAsync(dlc);
                RetroDownload dlcDownload = DownloadsViewModel.Instance.RetroDownloads.Single(d => d.Game.Id == dlc.Id);
                await TestSession.WaitUntil(() => dlcDownload.IsDone || dlcDownload.IsFailed, TimeSpan.FromSeconds(20), "the DLC download");
                Assert.True(dlc.Installed, dlcDownload.State);
                await RetroPlay.PlayAsync(dlc);
                Assert.Contains("DLC", dlc.Status);
                Assert.False(dlc.Busy);
                Assert.Contains("prod.keys", download.State);
                Assert.Equal("1-2-Switch.xci", Path.GetFileName(game.RomFile));
                Assert.Equal(new[] { "-u", "0", "-g", game.RomFile! }, layout.Arguments(RetroArchManager.Player, game.RomFile!));
                // No question at its start: no update check, no "stop?", and its settings there before it first runs (no migration from yuzu)
                Assert.Contains("check_for_updates=false", File.ReadAllText(layout.SettingsFiles.Single()));

                // Once RomM has them: the keys in its keys folder, the firmware's .nca in the console's storage
                session.Server.SwitchFirmware = (Encoding.ASCII.GetBytes("master_key_00 = 00"), FakeGameVaultServer.Zip(("0a1b.nca", "nca"), ("0c2d.cnmt.nca", "cnmt")));
                await RetroArchManager.EnsureReadyAsync(game.Platform, _ => { });
                Assert.True(layout.HasFirmware);
                Assert.Equal("master_key_00 = 00", File.ReadAllText(Path.Combine(layout.KeysDirectory, "prod.keys")));
                Assert.Equal(2, Directory.GetFiles(layout.FirmwareDirectory, "*.nca").Length);

                // The player is the console's first user when the game starts (Eden itself is not run here), the updates and DLC read from the games' folders
                Assert.ThrowsAny<Exception>(() => StandaloneEmulator.Eden.Launch(RetroArchManager.Player, game.RomFile!));
                Assert.Equal(EdenLayout.UserId(RetroArchManager.Player), File.ReadAllBytes(layout.ProfilesFile)[0x10..0x20]);
                string config = File.ReadAllText(layout.SettingsFiles.Single());
                Assert.Contains("Paths\\external_content_dirs\\size=3", config);
                Assert.Contains(Path.GetDirectoryName(dlc.RomFile)!.Replace('\\', '/'), config);

                // Its saves are what it writes in the player's user folder
                var saves = RetroCloudSaves.Of(game);
                var before = saves.Snapshot();
                string save = Path.Combine(layout.SavesDirectory(RetroArchManager.Player), "01000320000CC000", "save.dat");
                Directory.CreateDirectory(Path.GetDirectoryName(save)!);
                File.WriteAllText(save, "round 3");
                saves.Track(before);
                Assert.Contains(save, saves.Files());

                // Uninstalled with the other emulators: the saves and the users stay
                await RetroArchManager.UninstallAsync();
                Assert.False(File.Exists(layout.Executable));
                Assert.False(layout.HasFirmware);
                Assert.True(File.Exists(save));
                Assert.True(File.Exists(layout.ProfilesFile));
            }
            finally
            {
                session.Server.SwitchFirmware = null;
                SanctuaryService.Current = SanctuaryService.Url;
                DownloadsViewModel.Instance.RetroDownloads.Clear();
                Directory.Delete(layout.Directory, true);
                Directory.Delete(Path.Combine(session.LibraryRoot, "GameVault", "Retro"), true);
            }
        }
    }
}
