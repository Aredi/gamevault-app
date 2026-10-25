using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class XemuLayoutTests
    {
        [Fact]
        public void TheXbox_StartsItsDisc_InXemu_WithEachPlayersHardDisk()
        {
            RetroSystem system = RetroSystems.All["xbox"];
            Assert.True(system.IsXemu && system.IsStandalone);
            Assert.Equal("Halo.iso", RetroSystems.LaunchFile(system, new[] { "Halo.nfo", "Halo.iso" }));

            var layout = new XemuLayout("/xemu", windows: false);
            Assert.Equal(new[] { "-dvd_path", "/g/Halo.iso" }, layout.Arguments("5", "/g/Halo.iso"));
            Assert.Equal(Path.Combine("/xemu", "data", "xemu", "xemu", "xemu.toml"), layout.ConfigFile);
            Assert.Equal(Path.Combine("/xemu", "data"), layout.Environment["XDG_DATA_HOME"]);
            Assert.NotEqual(layout.HardDisk("5"), layout.HardDisk("7"));
            Assert.Equal(layout.HardDisk("1"), layout.HardDisk("not a number"));
            Assert.Contains(Path.Combine("/xemu", "hdd"), layout.KeptPaths);
            // Portable on Windows: its settings beside it
            Assert.Equal(Path.Combine(@"C:\sv\xemu", "xemu.toml"), new XemuLayout(@"C:\sv\xemu", windows: true).ConfigFile);
            Assert.True(XemuLayout.IsFlashRomSize(1024 * 1024) && XemuLayout.IsFlashRomSize(256 * 1024) && !XemuLayout.IsFlashRomSize(512));
        }

        [Fact]
        public void ItsSettings_TakeTheConsolesFiles_NoWelcome_TheRestKept()
        {
            string folder = @"C:\Users\O""Neil\xemu";
            var layout = new XemuLayout(folder, windows: true);
            // TOML's strings: a backslash and a quote escaped
            static string Toml(string path) => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            // As xemu writes it back (toml++): the player's own choices stay
            string written = "[general]\nshow_welcome = true\nlast_viewed_menu_index = 3\n\n[input.bindings]\nport1 = '030000005e040000'\n\n[sys.files]\nhdd_path = 'D:\\old.qcow2'\n";
            string config = layout.WithHardDisk(layout.Resolution(layout.Settings(written), 2), "5");
            Assert.Equal("[general]\nshow_welcome = false\nlast_viewed_menu_index = 3\nskip_boot_anim = true\n"
                + "[input.bindings]\nport1 = '030000005e040000'\n"
                + "[sys.files]\n"
                + $"hdd_path = {Toml(Path.Combine(folder, "hdd", "player-5.qcow2"))}\n"
                + $"bootrom_path = {Toml(Path.Combine(folder, "system", "mcpx_1.0.bin"))}\n"
                + $"flashrom_path = {Toml(Path.Combine(folder, "system", "bios.bin"))}\n"
                + $"eeprom_path = {Toml(Path.Combine(folder, "system", "eeprom.bin"))}\n"
                + "[general.updates]\ncheck = false\n"
                + "[display.quality]\nsurface_scale = 2\n",
                config);
            Assert.Contains("C:\\\\Users\\\\O\\\"Neil", config);
            Assert.Equal(config, layout.Settings(config));
        }

        [Fact]
        public void OnAMac_TheEmulatorsApps_TheirSettingsInTheirFolder()
        {
            var xemu = new XemuLayout("/sv/xemu", windows: false, mac: true);
            Assert.Equal("/sv/xemu/app/xemu.app/Contents/MacOS/xemu", xemu.Executable);
            Assert.EndsWith("/v0.8.136/xemu-0.8.136-macos-universal.zip", xemu.PackageUrl);
            // Its folder of settings is in ~/Library: the client's file given at each start, its own window's too
            Assert.Equal("/sv/xemu/xemu.toml", xemu.ConfigFile);
            Assert.Equal(new[] { "-config_path", "/sv/xemu/xemu.toml" }, xemu.StartArguments);
            Assert.Empty(xemu.Environment);
            Assert.Empty(new XemuLayout("/sv/xemu", windows: false).StartArguments);

            var rpcs3 = new Rpcs3Layout("/sv/rpcs3", windows: false, mac: true);
            Assert.Equal("/sv/rpcs3/app/RPCS3.app/Contents/MacOS/rpcs3", rpcs3.Executable);
            Assert.EndsWith("_macos_aarch64.7z", rpcs3.PackageUrl);
            Assert.Equal("RPCS3.app", rpcs3.MacApp);
            // Its ~/Library in its folder (by HOME): its storage, settings and log there
            Assert.Equal("/sv/rpcs3/home", rpcs3.Environment["HOME"]);
            Assert.Equal("/sv/rpcs3/home/Library/Application Support/rpcs3/dev_hdd0/home/00000005/savedata", rpcs3.SavesDirectory("5"));
            Assert.Equal("/sv/rpcs3/home/Library/Caches/rpcs3/RPCS3.log", rpcs3.LogFile);
            Assert.Equal("/sv/rpcs3/home/Library/Application Support/rpcs3/config.yml", rpcs3.ResolutionFiles.Single());

            var eden = new EdenLayout("/sv/eden", windows: false, mac: true);
            Assert.Equal("/sv/eden/app/eden.app/Contents/MacOS/eden", eden.Executable);
            Assert.EndsWith($"/{EdenLayout.Pinned}/Eden-macOS-{EdenLayout.Pinned[(EdenLayout.Pinned.IndexOf('.') + 1)..]}.dmg", eden.PackageUrl);
            Assert.Equal("/sv/eden", eden.WorkingDirectory);
        }

        [Fact]
        public void TheButtonsChosen_AreXemus_ForEachGamepadItKnows_AndThePluggedOne()
        {
            var layout = new XemuLayout("/xemu", windows: false);
            // As xemu writes it back: the gamepad it has seen, by SDL's id
            string written = "[input]\ngamepad_mappings = [\n    { gamepad_id = '03000000aa', controller_mapping = { a = 3}}\n    ]\n\n[input.bindings]\nport1 = '03000000aa'\n";
            var keys = new Dictionary<string, RetroKeyboard.Key> { ["b"] = new("q", "A"), ["l2"] = new("num1", "1"), ["r2"] = new("f13", "F13") };
            // The face buttons turned (the RetroPad's A on the bottom one), a trigger on a button (not read), the left stick upside down
            var pad = new Dictionary<string, RetroPad.Input> { ["b"] = RetroPad.Button(1), ["a"] = RetroPad.Button(0), ["l2"] = RetroPad.Button(9), ["l_y_plus"] = RetroPad.Axis(1, false) };
            string config = layout.Controls(written, keys, pad, "03000000bb");

            Assert.Contains("a = 20\n", config);          // Xbox A: q
            Assert.Contains("b = 27\n", config);          // B keeps RetroArch's x
            Assert.Contains("ltrigger = 30\n", config);   // 1
            Assert.Contains("rtrigger = 104\n", config);  // F13
            Assert.Contains("lstick_up = 0\n", config);   // no key: none, not xemu's E
            Assert.Contains("[input.bindings]\nport1 = '03000000aa'\n", config);
            string mapping = "controller_mapping = { a = 1, b = 0, x = 2, y = 3, back = 4, start = 6, lstick_btn = 7, rstick_btn = 8, lshoulder = 9, rshoulder = 10, "
                + "dpad_up = 11, dpad_down = 12, dpad_left = 13, dpad_right = 14, axis_trigger_left = -1, axis_trigger_right = 5, "
                + "axis_left_x = 0, invert_axis_left_x = false, axis_left_y = 1, invert_axis_left_y = true, axis_right_x = 2, invert_axis_right_x = false, axis_right_y = 3, invert_axis_right_y = false }";
            Assert.Contains($"gamepad_mappings = [{{ gamepad_id = '03000000aa', {mapping} }}, {{ gamepad_id = '03000000bb', {mapping} }}]\n", config);
            Assert.DoesNotContain("a = 3}", config);
            Assert.Equal(config, layout.Controls(config, keys, pad, "03000000BB"));
            // Nothing chosen for the gamepad: xemu's own mapping stays
            Assert.Contains("a = 3}", layout.Controls(written, null, null, null));
            Assert.True(XemuLayout.TakesAxis("r2") && XemuLayout.TakesAxis("l_x_minus") && !XemuLayout.TakesAxis("l") && !XemuLayout.TakesAxis("l3"));
        }

        [Fact]
        public void AFullDump_BecomesItsGamesPartition_AnXisoStays()
        {
            string folder = Path.Combine(Path.GetTempPath(), $"xbox-disc-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                // A full dump: the video partition (sparse), then the game's with its volume 32 sectors in and its last bytes
                string redump = Path.Combine(folder, "Halo.iso");
                byte[] magic = "MICROSOFT*XBOX*MEDIA"u8.ToArray(), end = "the game's last bytes"u8.ToArray();
                long game = 9 * 1024 * 1024 + 123;
                using (var file = new FileStream(redump, FileMode.CreateNew))
                {
                    file.SetLength(XboxDisc.VideoPartition + game);
                    file.Position = XboxDisc.VideoPartition + 32 * 2048;
                    file.Write(magic);
                    file.Position = XboxDisc.VideoPartition + game - end.Length;
                    file.Write(end);
                }
                Assert.True(XboxDisc.IsRedump(redump));

                var progress = new List<double>();
                XboxDisc.ToXiso(redump, progress.Add);
                byte[] xiso = File.ReadAllBytes(redump);
                Assert.Equal(game, xiso.Length);
                Assert.Equal(magic, xiso[(32 * 2048)..(32 * 2048 + magic.Length)]);
                Assert.Equal(end, xiso[^end.Length..]);
                Assert.Equal(1.0, progress[^1]);
                Assert.False(XboxDisc.IsRedump(redump));

                // A small file is no dump
                string other = Path.Combine(folder, "notes.iso");
                File.WriteAllText(other, "not a disc");
                Assert.False(XboxDisc.IsRedump(other));
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
