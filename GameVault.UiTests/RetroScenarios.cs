using Avalonia.Headless.XUnit;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    /// <summary>The games of the other platforms: listed from the SanctuaryVault service, installed into the library folder.</summary>
    public class RetroScenarios
    {
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

                RetroGame zelda = Assert.Single(page.Shown);
                Assert.Equal(("Zelda", "nes", false), (zelda.Name, zelda.Platform.Slug, zelda.Installed));

                await page.InstallAsync(zelda);
                Assert.True(zelda.Installed, zelda.Status);
                Assert.Equal(Path.Combine(session.LibraryRoot, "GameVault", "Retro", "nes", "40", "Zelda (E).nes"), zelda.RomFile);
                Assert.Equal(FakeGameVaultServer.RetroRom, File.ReadAllBytes(zelda.RomFile!));
                Assert.Contains("input_quit_gamepad_combo = \"4\"", File.ReadAllText(layout.ConfigFile));
            }
            finally
            {
                SanctuaryService.Current = SanctuaryService.Url;
                Directory.Delete(layout.Directory, true);
                Directory.Delete(Path.Combine(session.LibraryRoot, "GameVault", "Retro"), true);
            }
        }
    }
}
