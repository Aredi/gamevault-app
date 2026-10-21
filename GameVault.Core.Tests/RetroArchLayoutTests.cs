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
            string config = layout.Config("12");
            Assert.Contains($"joypad_autoconfig_dir = \"{home}/autoconfig\"\n", config);
            Assert.Contains($"libretro_info_path = \"{home}/cores\"\n", config);
            Assert.Contains("savefile_directory = \"/data/retroarch/saves/12\"\n", config);
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
            Assert.Contains($"libretro_info_path = \"{Path.Combine(app, "info")}\"\n", layout.Config("1"));
        }

        [Fact]
        public void TheAchievements_AreTheAccountsToken_NotItsPassword()
        {
            var layout = new RetroArchLayout("/data/retroarch", windows: false);
            Assert.Contains("cheevos_enable = \"false\"\n", layout.Config("1"));
            string config = layout.Config("1", ("Alexis", "t0k3n"));
            Assert.Contains("cheevos_enable = \"true\"\n", config);
            Assert.Contains("cheevos_username = \"Alexis\"\n", config);
            Assert.Contains("cheevos_token = \"t0k3n\"\n", config);
            Assert.DoesNotContain("cheevos_password", config);
            // The save states stay allowed, with their picture
            Assert.Contains("cheevos_hardcore_mode_enable = \"false\"\n", config);
            Assert.Contains("savestate_thumbnail_enable = \"true\"\n", config);
        }

        [Fact]
        public void TheCrtLook_IsTheGamesOwnPreset_InBothShaderKinds()
        {
            var layout = new RetroArchLayout("/data/retroarch", windows: false);
            Assert.Contains("video_shader_dir = \"/data/retroarch/shaders\"\n", layout.Config("1"));
            Assert.Equal("/data/retroarch/shaders/presets/mesen/Zelda (USA).glslp", layout.GamePreset("mesen", "/games/Zelda (USA).nes", "glsl"));
            Assert.Equal("shaders = \"1\"\nshader0 = \"/data/retroarch/shaders/crt/crt-easymode.slang\"\nfilter_linear0 = \"false\"\nscale_type0 = \"viewport\"\n", layout.CrtPreset("slang"));
            Assert.All(RetroArchLayout.CrtShaderUrls, s => Assert.EndsWith("crt-easymode." + s.Extension, s.Url));
        }

        [Fact]
        public void TheSaveStates_AreTheGamesSlots_NewestFirst_WithTheirPicture()
        {
            string root = Path.Combine(Path.GetTempPath(), "retroarch-states-" + Guid.NewGuid().ToString("N"));
            var layout = new RetroArchLayout(root, windows: false);
            string folder = Path.Combine(layout.StatesDirectory("3"), "mesen");
            Directory.CreateDirectory(folder);
            void State(string name, int minutesAgo)
            {
                File.WriteAllText(Path.Combine(folder, name), "");
                File.SetLastWriteTimeUtc(Path.Combine(folder, name), DateTime.UtcNow.AddMinutes(-minutesAgo));
            }
            State("Zelda.state", 30);
            State("Zelda.state.png", 30);
            State("Zelda.state4", 5);
            State("Zelda.state.auto", 1);
            State("Zelda II.state", 2);
            var states = layout.SaveStates("3", "mesen", "/games/Zelda.nes");
            Assert.Equal(new[] { 4, 0 }, states.Select(s => s.Slot));
            Assert.Null(states[0].Picture);
            Assert.EndsWith("Zelda.state.png", states[1].Picture);
            Assert.Equal(new[] { "-L", Path.Combine(root, "cores", "mesen_libretro.so"), "--config", layout.ConfigFile, "--entryslot", "4", "/games/Zelda.nes" }, layout.Arguments("mesen", "/games/Zelda.nes", 4));
            Assert.Empty(layout.SaveStates("4", "mesen", "/games/Zelda.nes"));
            Directory.Delete(root, true);
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
