using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>The games of the other platforms: listed from the SanctuaryVault service, installed into the library folder.</summary>
    public class RetroScenarios
    {
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
        public async Task RetroSaves_FollowThePlayer_FromPcToPc()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            string saves = RetroArchManager.Layout.SavesDirectory(gamevault.Helper.LoginManager.Instance.GetCurrentUser()!.ID.ToString());
            try
            {
                // Another PC sent a save: it comes here before a game
                session.Server.RetroSaves = (FakeGameVaultServer.Zip(("mGBA/zelda.srm", "from the other PC")), 5000);
                Assert.Null(await RetroArchManager.TakeCloudSavesAsync(_ => { }));
                Assert.Equal("from the other PC", File.ReadAllText(Path.Combine(saves, "mGBA", "zelda.srm")));

                // Played here: the changed saves go up after the game, and only then
                File.WriteAllText(Path.Combine(saves, "mGBA", "zelda.srm"), "played here");
                await RetroArchManager.SendCloudSavesAsync();
                Assert.Equal(6000, session.Server.RetroSaves!.Value.SavedAt);
                using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(session.Server.RetroSaves.Value.Zip)))
                    Assert.Equal("played here", new StreamReader(zip.GetEntry("mGBA/zelda.srm")!.Open()).ReadToEnd());
                await RetroArchManager.SendCloudSavesAsync();
                Assert.Equal(6000, session.Server.RetroSaves!.Value.SavedAt);
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                session.Server.RetroSaves = null;
                Directory.Delete(RetroArchManager.Layout.Directory, true);
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

                Assert.Equal(new[] { "nes", "psx" }, page.ViewModel.Platforms.Select(p => p.Slug).Order());
                RetroGame zelda = Assert.Single(page.Shown);
                Assert.Equal(("Zelda", "nes", false), (zelda.Name, zelda.Platform.Slug, zelda.Installed));

                // Into the Downloads page, as the library's games, and played when it is complete
                await page.InstallAsync(zelda);
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
                await page.InstallAsync(castlevania);
                // The page reloads its games (another platform and back): the download is not forgotten
                await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "nes"));
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
    }
}
