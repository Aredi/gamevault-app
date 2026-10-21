using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroArchLayoutTests
    {
        [Fact]
        public void OnLinux_TheAppImageAndItsPortableHomeAreUsed()
        {
            var layout = new RetroArchLayout("/data/retroarch", windows: false);
            string home = "/data/retroarch/RetroArch-Linux-x86_64/RetroArch-Linux-x86_64.AppImage.home/.config/retroarch";

            Assert.Equal("/data/retroarch/RetroArch-Linux-x86_64/RetroArch-Linux-x86_64.AppImage", layout.Executable);
            Assert.Equal($"https://buildbot.libretro.com/stable/{RetroArchLayout.Version}/linux/x86_64/RetroArch.7z", layout.PackageUrl);
            Assert.Equal("https://buildbot.libretro.com/nightly/linux/x86_64/latest/mgba_libretro.so.zip", layout.CoreUrl("mgba"));
            string config = layout.Config();
            Assert.Contains($"joypad_autoconfig_dir = \"{home}/autoconfig\"\n", config);
            Assert.Contains($"libretro_info_path = \"{home}/cores\"\n", config);
            Assert.Contains("savefile_directory = \"/data/retroarch/saves\"\n", config);
            Assert.Contains("input_quit_gamepad_combo = \"4\"\n", config);
            // The only menu theme unpacked
            Assert.Contains("menu_driver = \"ozone\"\n", config);
            Assert.DoesNotContain("ozone", RetroArchLayout.UnusedPackageFolders);
            Assert.Equal(new[] { "-L", "/data/retroarch/cores/mgba_libretro.so", "--config", "/data/retroarch/sanctuaryvault.cfg", "/games/a.gba" }, layout.Arguments("mgba", "/games/a.gba"));
        }

        [Fact]
        public void OnWindows_TheFoldersAreNextToRetroArch()
        {
            var layout = new RetroArchLayout(Path.Combine("C:", "rd"), windows: true);
            string app = Path.Combine("C:", "rd", "RetroArch-Win64");

            Assert.Equal(Path.Combine(app, "retroarch.exe"), layout.Executable);
            Assert.EndsWith("/windows/x86_64/latest/snes9x_libretro.dll.zip", layout.CoreUrl("snes9x"));
            Assert.Contains($"libretro_info_path = \"{Path.Combine(app, "info")}\"\n", layout.Config());
        }

        [Fact]
        public void IsInstalled_OnlyForThePinnedVersion()
        {
            string root = Path.Combine(Path.GetTempPath(), $"gv-retro-{Guid.NewGuid():N}");
            try
            {
                var layout = new RetroArchLayout(root, windows: false);
                Directory.CreateDirectory(layout.AppDirectory);
                File.WriteAllText(layout.Executable, "");
                Assert.False(layout.IsInstalled);
                File.WriteAllText(layout.VersionFile, "1.0.0");
                Assert.False(layout.IsInstalled);
                File.WriteAllText(layout.VersionFile, RetroArchLayout.Version);
                Assert.True(layout.IsInstalled);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
