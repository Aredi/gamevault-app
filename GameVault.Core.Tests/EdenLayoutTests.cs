using GameVault.Core.Retro;
using System.IO.Compression;
using System.Text;

namespace GameVault.Core.Tests
{
    public class EdenLayoutTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "eden-layout");

        [Fact]
        public void TheSwitch_StartsItsGames_NotTheirUpdatesAndDlc()
        {
            RetroSystem system = RetroSystems.All["switch"];
            Assert.True(system.IsEden && system.IsStandalone);
            Assert.Equal("1-2-Switch.xci", Path.GetFileName(RetroSystems.LaunchFile(system, new[] { "1-2-Switch (Certificate).bin", "1-2-Switch.xci", "1-2-Switch (Initial Data).bin" })));
            // A game's folder in RomM, its update and DLC beside it; an add-on alone is its own file
            Assert.Equal("g/Zelda.nsp", RetroSystems.LaunchFile(system, new[] { "g/update/A.nsp", "g/Zelda.nsp", "g/Zelda [01007EF00011E800].nsp", "g/dlc/B.nsp" }));
            Assert.Equal("d/X [0100670014483001].nsp", RetroSystems.LaunchFile(system, new[] { "d/X [0100670014483001].nsp" }));
            Assert.True(RetroSystems.IsSwitchAddOn(Path.Combine("g", "dlc", "B.nsp")));
            Assert.False(RetroSystems.IsSwitchAddOn("Assassins Creed The Ezio Collection [0100670014482000].nsp"));
            Assert.False(RetroSystems.IsSwitchAddOn("Captain Toad Treasure Tracker [01009BF0072D4000][v393216].nsp"));
            Assert.False(RetroSystems.IsSwitchAddOn("Bayonetta 2.xci"));
            Assert.True(RetroSystems.IsSwitchAddOn("Assassins Creed The Ezio Collection - Brotherhood [DLC][0100670014483001].nsp"));
            Assert.True(RetroSystems.IsSwitchAddOn("Zelda [01007EF00011E800][v65536].nsp"));
        }

        [Fact]
        public void TheUpdatesAndDlc_AreReadFromTheGamesFolders_TheOtherPathsStay()
        {
            // As Eden writes it
            string ini = EdenLayout.ContentFolders("[UI]\nPaths\\gamedirs\\size=1\nPaths\\external_content_dirs\\size=0\n[Core]\n", new[] { @"C:\Jeux\GameVault\Retro\switch\70", "/home/a/switch/71" });
            Assert.Equal("[UI]\nPaths\\gamedirs\\size=1\nPaths\\external_content_dirs\\size=2\n"
                + "Paths\\external_content_dirs\\1\\path=\"C:/Jeux/GameVault/Retro/switch/70\"\nPaths\\external_content_dirs\\2\\path=\"/home/a/switch/71\"\n[Core]\n", ini);
        }

        [Fact]
        public void EachPlayer_IsTheConsolesFirstUser_TheOthersStay()
        {
            byte[] first = EdenLayout.Profiles(null, "5", "Alexis");
            Assert.Equal(0x650, first.Length);
            // Its id twice, then its name, as Eden reads profiles.dat
            Assert.Equal(EdenLayout.UserId("5"), first[0x10..0x20]);
            Assert.Equal(EdenLayout.UserId("5"), first[0x20..0x30]);
            Assert.Equal("Alexis", Encoding.UTF8.GetString(first, 0x10 + 40, 32).TrimEnd('\0'));
            Assert.All(first[(0x10 + 0xC8)..], b => Assert.Equal(0, b));

            byte[] second = EdenLayout.Profiles(first, "7", "Zoé Éléonore Ünïcödé-Name");
            Assert.Equal(EdenLayout.UserId("7"), second[0x10..0x20]);
            Assert.Equal(EdenLayout.UserId("5"), second[(0x10 + 0xC8)..(0x20 + 0xC8)]);
            string name = Encoding.UTF8.GetString(second, 0x10 + 40, 32).TrimEnd('\0');
            Assert.StartsWith("Zoé Éléonore", name);
            Assert.DoesNotContain('�', name);
            // Back to the first: first again, once
            byte[] third = EdenLayout.Profiles(second, "5", "Alexis");
            Assert.Equal(EdenLayout.UserId("5"), third[0x10..0x20]);
            Assert.Equal(EdenLayout.UserId("7"), third[(0x10 + 0xC8)..(0x20 + 0xC8)]);
            Assert.All(third[(0x10 + 2 * 0xC8)..], b => Assert.Equal(0, b));
        }

        [Fact]
        public void ItsSaves_AreInTheFolderOfThePlayersUser_AndStayAtAnUninstall()
        {
            var linux = new EdenLayout(Root, windows: false);
            // Eden reads the id as two little-endian numbers and writes the high one first
            string saves = linux.SavesDirectory("5");
            Assert.Equal(Path.Combine(Root, "user", "nand", "user", "save", "0000000000000000", "53414E43545541520000000000000005"), saves);
            Assert.Contains(linux.KeptPaths, kept => saves.StartsWith(kept + Path.DirectorySeparatorChar));
            Assert.Contains(linux.KeptPaths, kept => linux.ProfilesFile.StartsWith(kept + Path.DirectorySeparatorChar));
            Assert.Equal(Root, linux.WorkingDirectory);
            Assert.Equal(Path.Combine(Root, "app", "AppRun"), linux.Executable);
            Assert.EndsWith("amd64-clang-pgo.AppImage", linux.PackageUrl);
            Assert.Equal(new[] { "-u", "0", "-g", "game.xci" }, linux.Arguments("5", "game.xci"));

            var windows = new EdenLayout(Root, windows: true);
            Assert.Equal(Path.Combine(Root, "eden.exe"), windows.Executable);
            Assert.EndsWith(".zip", windows.PackageUrl);
        }

        [Fact]
        public void ANewerNightly_IsOffered_WithThisPcsBuild_AndKeptOnceInstalled()
        {
            var layout = new EdenLayout(Path.Combine(Root, Guid.NewGuid().ToString("N")), windows: false);
            string Feed(string tag, string file) => $"{{\"tag_name\":\"{tag}\",\"assets\":[{{\"name\":\"{file}\"}}]}}";
            const string newer = "v1792500000.989768aa46";
            Assert.Equal(newer, layout.NewerNightly(Feed(newer, "Eden-Linux-989768aa46-amd64-clang-pgo.AppImage")));
            // Not newer, or without the build this PC takes: nothing offered
            Assert.Null(layout.NewerNightly(Feed(EdenLayout.Pinned, "Eden-Linux-de7e6e01d4-amd64-clang-pgo.AppImage")));
            Assert.Null(layout.NewerNightly(Feed(newer, "Eden-Windows-989768aa46-amd64-clang-pgo.zip")));

            // Installed before 1.26.3: the pinned commit in the version file, nothing reinstalled
            Directory.CreateDirectory(Path.Combine(layout.Directory, "app"));
            File.WriteAllText(layout.Executable, "");
            File.WriteAllText(layout.VersionFile, "de7e6e01d4");
            Assert.True(layout.IsInstalled);
            // Taken: installed over it, then kept by the next starts (until the client's own is newer)
            layout.Tag = newer;
            Assert.False(layout.IsInstalled);
            Assert.Equal("https://nightly.eden-emu.dev/v1792500000.989768aa46/Eden-Linux-989768aa46-amd64-clang-pgo.AppImage", layout.PackageUrl);
            File.WriteAllText(layout.VersionFile, layout.Version);
            Assert.True(new EdenLayout(layout.Directory, windows: false).IsInstalled);
            Assert.Equal(newer, new EdenLayout(layout.Directory, windows: false).Tag);
            File.WriteAllText(layout.VersionFile, "v1700000000.0123456789");
            Assert.Equal(EdenLayout.Pinned, new EdenLayout(layout.Directory, windows: false).Tag);
        }

        [Fact]
        public void ItsSettings_AreReadByEden_AndKeepItsOwn()
        {
            var layout = new EdenLayout(Root, windows: false);
            string merged = layout.Settings("[UI]\ntheme=colorful\ncheck_for_updates\\default=true\ncheck_for_updates=true\n[System]\nlanguage_index=2\n");
            Assert.Contains("theme=colorful", merged);
            Assert.Contains("check_for_updates\\default=false\ncheck_for_updates=false", merged);
            Assert.Contains("confirmStop\\default=false\nconfirmStop=2", merged);
            Assert.True(merged.IndexOf("confirmStop", StringComparison.Ordinal) < merged.IndexOf("[System]", StringComparison.Ordinal));
            Assert.Equal(merged, layout.Settings(merged));
        }

        [Fact]
        public void TheFirmware_IsItsNcaFiles_InTheConsolesStorage()
        {
            var layout = new EdenLayout(Path.Combine(Root, Guid.NewGuid().ToString("N")), windows: false);
            try
            {
                Assert.False(layout.HasFirmware);
                var zip = new MemoryStream();
                using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
                    foreach (string name in new[] { "Firmware 20.1.0/0a1b.nca", "Firmware 20.1.0/0c2d.cnmt.nca", "readme.txt", "../escape.nca" })
                        using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
                            writer.Write(name);
                zip.Position = 0;
                layout.InstallFirmware(zip);
                Assert.Equal(new[] { "0a1b.nca", "0c2d.cnmt.nca", "escape.nca" }, Directory.GetFiles(layout.FirmwareDirectory).Select(Path.GetFileName).Order());
                Assert.False(layout.HasFirmware);
                Directory.CreateDirectory(layout.KeysDirectory);
                File.WriteAllText(Path.Combine(layout.KeysDirectory, EdenLayout.KeysFile), "keys");
                Assert.True(layout.HasFirmware);
            }
            finally
            {
                if (Directory.Exists(layout.Directory))
                    Directory.Delete(layout.Directory, true);
            }
        }
    }
}
