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
