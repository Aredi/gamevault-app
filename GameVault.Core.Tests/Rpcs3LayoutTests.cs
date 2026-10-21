using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class Rpcs3LayoutTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "rpcs3-layout");

        [Fact]
        public void AGameInAFolder_StartsByItsFolder_AnIsoAsItIs()
        {
            RetroSystem ps3 = RetroSystems.All["ps3"];
            Assert.True(ps3.IsRpcs3);
            string game = Path.Combine(Root, "Infamous");
            string eboot = Path.Combine(game, "PS3_GAME", "USRDIR", "EBOOT.BIN");
            var files = new[] { Path.Combine(game, "PS3_GAME", "USRDIR", "bin", "EBOOT.BIN"), eboot, Path.Combine(game, "PS3_GAME", "USRDIR", "data.bin"), Path.Combine(game, "PS3_DISC.SFB") };
            Assert.Equal(eboot, RetroSystems.LaunchFile(ps3, files));
            Assert.Equal(game, Rpcs3Layout.BootTarget(eboot));

            string iso = Path.Combine(Root, "Lollipop Chainsaw (Europe).iso");
            Assert.Equal(iso, RetroSystems.LaunchFile(ps3, files.Append(iso).Append(Path.Combine(Root, "README.TXT"))));
            Assert.Equal(iso, Rpcs3Layout.BootTarget(iso));
        }

        [Fact]
        public void EachPlayer_IsAUserOfTheConsole_AndLinuxKeepsItAllInItsFolder()
        {
            var linux = new Rpcs3Layout(Root, windows: false);
            Assert.Equal(new[] { "--no-gui", "--user-id", "00000005", "game.iso" }, linux.Arguments("5", "game.iso"));
            Assert.Equal("00000001", Rpcs3Layout.UserId("0"));
            Assert.Equal(Path.Combine(Root, "config", "rpcs3", "dev_hdd0", "home", "00000005", "savedata"), linux.SavesDirectory("5"));
            Assert.All(linux.Environment.Where(e => e.Key.StartsWith("XDG")), e => Assert.StartsWith(Root, e.Value));
            Assert.StartsWith(Root, linux.LogFile);
            Assert.Contains(Path.Combine(Root, "config", "rpcs3", "dev_hdd0"), linux.KeptPaths);
            Assert.EndsWith("_linux64.AppImage", linux.PackageUrl);

            var windows = new Rpcs3Layout(Root, windows: true);
            Assert.Equal(Path.Combine(Root, "dev_hdd0", "home", "00000005", "savedata"), windows.SavesDirectory("5"));
            Assert.Empty(windows.Environment);
            Assert.EndsWith("_win64_msvc.7z", windows.PackageUrl);
        }

        [Fact]
        public void ItsWindowSettings_GetTheClients_AndKeepTheOthers()
        {
            string existing = "[GameList]\nmarginFactor=0.09\n\n[main_window]\nlastExplorePathPUP=/tmp\ninfoBoxEnabledWelcome=true\n[Meta]\nattachCommandLine=false\n";
            var layout = new Rpcs3Layout(Root, windows: false);
            string merged = layout.Settings(existing);
            Assert.Contains("marginFactor=0.09", merged);
            Assert.Contains("lastExplorePathPUP=/tmp", merged);
            Assert.Contains("infoBoxEnabledWelcome=false", merged);
            Assert.DoesNotContain("infoBoxEnabledWelcome=true", merged);
            Assert.Contains("checkUpdateStart=false", merged);
            // In their section, before the next one
            Assert.True(merged.IndexOf("checkUpdateStart", StringComparison.Ordinal) < merged.IndexOf("[Meta]", StringComparison.Ordinal));
            Assert.Equal(merged, layout.Settings(merged));
            Assert.StartsWith("[main_window]\ninfoBoxEnabledWelcome=false", layout.Settings(""));
        }
    }
}
