using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class EglantineLayoutTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "eglantine-layout");

        [Fact]
        public void TheSwitch2_IsEglantines_ItsDumpsStarted_NotTheirAddOns()
        {
            RetroSystem system = RetroSystems.All["switch-2"];
            Assert.True(system.IsEglantine && system.IsSwitch && system.IsStandalone && !system.IsEden);
            Assert.Equal("g/Pokopia.nup", RetroSystems.LaunchFile(system, new[] { "g/Pokopia.nsp", "g/Pokopia.nup" }));
            Assert.Equal("g/MKW.nsp", RetroSystems.LaunchFile(system, new[] { "g/update/A.nsp", "g/MKW.nsp", "g/MKW [0400C3F00006E800].nsp" }));
            Assert.True(RetroSystems.IsSwitchAddOn("MKW [0400C3F00006E800].nsp"));
            Assert.False(RetroSystems.IsSwitchAddOn("MKW [0400C3F00006E000].nsp"));
            // A dump's folder, unzipped: started by its exefs/main
            string npdm = Path.Combine("g", "MKW", "exefs", "main.npdm");
            Assert.Equal(npdm, RetroSystems.LaunchFile(system, new[] { Path.Combine("g", "MKW", "romfs", "a.bin"), npdm, Path.Combine("g", "MKW", "exefs", "main") }));
            // A dump whose base has no romfs (Pokopia's, in RomM): its update is the whole game
            string update = Path.Combine("P", "updates", "655360", "exefs", "main.npdm");
            Assert.Equal(update, RetroSystems.LaunchFile(system, new[] { Path.Combine("P", "base", "exefs", "main.npdm"), Path.Combine("P", "updates", "655360", "romfs", "LD_C0_R.BIN"), update }));
            // Both with their romfs: the newest update
            string newest = Path.Combine("Q", "updates", "720896", "exefs", "main.npdm");
            Assert.Equal(newest, RetroSystems.LaunchFile(system, new[] { Path.Combine("Q", "base", "exefs", "main.npdm"), Path.Combine("Q", "base", "romfs", "a"),
                Path.Combine("Q", "updates", "65536", "exefs", "main.npdm"), Path.Combine("Q", "updates", "65536", "romfs", "a"), newest, Path.Combine("Q", "updates", "720896", "romfs", "a") }));
            var layout = new EglantineLayout(Root, windows: true);
            Assert.Equal(new[] { "-u", "0", "-g", Path.Combine("g", "MKW", "exefs", "main") }, layout.Arguments("1", npdm));
            Assert.Equal(new[] { "-u", "0", "-g", "g/MKW.nup" }, layout.Arguments("1", "g/MKW.nup"));
        }

        [Fact]
        public void ItsBuilds_AreThePinnedReleases_InTheirFolder()
        {
            const string releases = "https://git.eden-emu.dev/Franseven/PokeDinner-a-Eglantine-Fork/releases/download/2.5.0/";
            var windows = new EglantineLayout("/sv/eglantine", windows: true);
            Assert.Equal(releases + "PokeDinner-Eglantine-2.5.0-win64.zip", windows.PackageUrl);
            Assert.True(windows.PackageInFolder);
            Assert.Equal(Path.Combine("/sv/eglantine", "eglantine.exe"), windows.Executable);
            var mac = new EglantineLayout("/sv/eglantine", windows: false, mac: true);
            Assert.Equal(releases + "PokeDinner-macOS-arm64-216b503.zip", mac.PackageUrl);
            Assert.Equal(Path.Combine("/sv/eglantine", "app", "PokeDinner.app", "Contents", "MacOS", "eglantine"), mac.Executable);
            Assert.Equal(new[] { (Path.Combine("/sv/eglantine", "app", "user"), Path.Combine("/sv/eglantine", "user")) }, mac.Links);
            var linux = new EglantineLayout("/sv/eglantine", windows: false);
            Assert.Equal(releases + "Eglantine-PokeDinner-2.5.0-linux-x86_64.AppImage", linux.PackageUrl);
            Assert.False(linux.PackageInFolder);
            Assert.Equal("/sv/eglantine", linux.WorkingDirectory);
            // Its games need nothing from RomM
            Assert.True(linux.HasFirmware);
        }

        [Fact]
        public void ANewerRelease_IsOffered_WithThisPcsFile_AndKeptOnceInstalled()
        {
            var layout = new EglantineLayout(Path.Combine(Root, Guid.NewGuid().ToString("N")), windows: false);
            try
            {
                // As the forge answers, newest first: a pre-release without the Linux file, then one with it
                string Release(string tag, params string[] files) => $"{{\"tag_name\":\"{tag}\",\"prerelease\":true,\"assets\":["
                    + string.Join(",", files.Select(f => $"{{\"name\":\"{f}\",\"browser_download_url\":\"https://forge/{tag}/{f}\"}}")) + "]}";
                string feed = "[" + string.Join(",", Release("2.7.0a", "PokeDinner-Eglantine-2.7.0-win64.zip"),
                    Release("2.6.0", "Eglantine-PokeDinner-2.6.0-linux-x86_64.AppImage", "PokeDinner-Eglantine-2.6.0-exe-only.zip", "PokeDinner-Eglantine-2.6.0-win64.zip"),
                    Release(EglantineLayout.Pinned, "Eglantine-PokeDinner-2.5.0-linux-x86_64.AppImage")) + "]";
                Assert.Equal(("2.6.0", "https://forge/2.6.0/Eglantine-PokeDinner-2.6.0-linux-x86_64.AppImage"), layout.NewerRelease(feed));
                Assert.Equal(("2.7.0a", "https://forge/2.7.0a/PokeDinner-Eglantine-2.7.0-win64.zip"), new EglantineLayout(layout.Directory, windows: true).NewerRelease(feed));
                Assert.Null(layout.NewerRelease("[" + Release(EglantineLayout.Pinned, "Eglantine-PokeDinner-2.5.0-linux-x86_64.AppImage") + "]"));

                Directory.CreateDirectory(Path.Combine(layout.Directory, "app"));
                File.WriteAllText(layout.Executable, "");
                File.WriteAllText(layout.VersionFile, EglantineLayout.Pinned);
                Assert.True(layout.IsInstalled);
                // Taken: installed over it, then kept by the next starts (until the client's own is newer)
                layout.Update("2.6.0", "https://forge/2.6.0/e.AppImage");
                Assert.False(layout.IsInstalled);
                Assert.Equal("https://forge/2.6.0/e.AppImage", layout.PackageUrl);
                File.WriteAllText(layout.VersionFile, layout.Version);
                Assert.True(new EglantineLayout(layout.Directory, windows: false).IsInstalled);
                Assert.Equal("2.6.0", new EglantineLayout(layout.Directory, windows: false).Tag);
                File.WriteAllText(layout.VersionFile, "1.5.0");
                Assert.Equal(EglantineLayout.Pinned, new EglantineLayout(layout.Directory, windows: false).Tag);
            }
            finally { if (Directory.Exists(layout.Directory)) Directory.Delete(layout.Directory, true); }
        }

        [Theory]
        [InlineData("2.1.0a", "2.1.0")]
        [InlineData("v3", "3.0")]
        [InlineData("baseline", "0.0")]
        public void ItsTags_AreReadAsVersions(string tag, string version) => Assert.Equal(Version.Parse(version), EglantineLayout.VersionOf(tag));

        [Fact]
        public void ItsSettings_AreEdens_WithTheSwitch2sMemory()
        {
            var layout = new EglantineLayout(Root, windows: false);
            string merged = layout.Settings("[Core]\nmemory_layout_mode\\default=true\nmemory_layout_mode=0\n");
            Assert.Contains("memory_layout_mode\\default=false\nmemory_layout_mode=4", merged);
            Assert.Contains("check_for_updates\\default=false\ncheck_for_updates=false", merged);
            Assert.Equal(merged, layout.Settings(merged));
        }
    }
}
