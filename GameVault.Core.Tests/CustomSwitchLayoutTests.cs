using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class CustomSwitchLayoutTests
    {
        private const string Zip = "https://github.com/pretend7979/Eglantine/releases/download/starfox-2026.11.04/Eglantine-Windows-x64-starfox-2026.11.04.zip";

        [Fact]
        public void AStarFoxFork_IsWindowsOnly_InItsFolder_WithEglantinesUserFolder()
        {
            var eglantine = new EglantineLayout("/sv/eglantine", windows: true);
            var starfox = new CustomSwitchLayout("/sv/emulators/starfox", eglantine, Zip, null, null, windows: true);
            Assert.True(starfox.IsAvailable);
            Assert.Equal(Zip, starfox.PackageUrl);
            Assert.Equal(Zip, starfox.Version);
            Assert.True(starfox.PackageInFolder);
            Assert.Equal(Path.Combine("/sv/emulators/starfox", "eglantine.exe"), starfox.Executable);
            // The same storage, players, settings and controller: Eglantine's "user" folder, linked beside it
            Assert.Equal(eglantine.UserDirectory, starfox.UserDirectory);
            Assert.Equal(eglantine.SavesDirectory("3"), starfox.SavesDirectory("3"));
            Assert.Equal(eglantine.SettingsFiles, starfox.SettingsFiles);
            Assert.Equal(new[] { (Path.Combine("/sv/emulators/starfox", "user"), eglantine.UserDirectory) }, starfox.Links);
            Assert.Empty(starfox.KeptPaths);
            Assert.True(starfox.HasFirmware);
            // Started and set as Eglantine: its dumps' folders, the Switch 2's memory
            string npdm = Path.Combine("g", "SF", "exefs", "main.npdm");
            Assert.Equal(eglantine.Arguments("1", npdm), starfox.Arguments("1", npdm));
            Assert.Contains("memory_layout_mode=4", starfox.Settings(""));

            Assert.False(new CustomSwitchLayout("/sv/emulators/starfox", new EglantineLayout("/sv/eglantine", windows: false), Zip, null, null, windows: false).IsAvailable);
            Assert.False(new CustomSwitchLayout("/sv/emulators/starfox", new EglantineLayout("/sv/eglantine", windows: false, mac: true), Zip, " ", null, windows: false, mac: true).IsAvailable);
        }

        [Fact]
        public void OnLinuxAndAMac_TheUserFolderIsLinkedWhereTheForkLooks()
        {
            var linux = new CustomSwitchLayout("/sv/emulators/sf", new EglantineLayout("/sv/eglantine", windows: false), null, "https://x/e.AppImage", null, windows: false);
            Assert.Equal("https://x/e.AppImage", linux.PackageUrl);
            Assert.Equal(Path.Combine("/sv/emulators/sf", "app", "AppRun"), linux.Executable);
            Assert.Equal("/sv/emulators/sf", linux.WorkingDirectory);
            Assert.Equal(new[] { (Path.Combine("/sv/emulators/sf", "user"), Path.Combine("/sv/eglantine", "user")) }, linux.Links);

            var eden = new EdenLayout("/sv/eden", windows: false, mac: true);
            var mac = new CustomSwitchLayout("/sv/emulators/sf", eden, null, null, "https://x/e.zip", windows: false, mac: true);
            Assert.Equal(Path.Combine("/sv/emulators/sf", "app", "eden.app", "Contents", "MacOS", "eden"), mac.Executable);
            Assert.Equal(new[] { (Path.Combine("/sv/emulators/sf", "app", "user"), Path.Combine("/sv/eden", "user")) }, mac.Links);
            Assert.Equal(eden.KeysDirectory, mac.KeysDirectory);
        }
    }
}
