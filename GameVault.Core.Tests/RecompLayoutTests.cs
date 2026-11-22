using GameVault.Core.Retro;
using Xunit;

namespace GameVault.Core.Tests
{
    public class RecompLayoutTests
    {
        private static RecompLayout Layout(string directory, string? executable = "fefw-launcher.exe", string? arguments = null, bool windows = true, string? linux = null) =>
            new(directory, "https://github.com/x/fefw/releases/download/v1/fefw.zip", linux, null, windows, false, executable, arguments);

        [Fact]
        public void ARecomp_IsPlayedWhereItHasABuildAndAProgram()
        {
            string directory = Path.Combine(Path.GetTempPath(), "gv-recomp-" + Guid.NewGuid().ToString("N"));
            Assert.True(Layout(directory).IsAvailable);
            Assert.Equal(Path.Combine(directory, "bin", "game.exe"), Layout(directory, "bin\\game.exe").Executable);
            // No Linux build, no program, or a program outside its folder: the console's emulator plays the game
            Assert.False(Layout(directory, windows: false).IsAvailable);
            Assert.False(Layout(directory, executable: null).IsAvailable);
            Assert.False(Layout(directory, executable: "../evil.exe").IsAvailable);
            Assert.False(Layout(directory, executable: "C:\\evil.exe").IsAvailable);
            // An AppImage starts itself
            var appImage = Layout(directory, executable: null, windows: false, linux: "https://x/Zelda64-x86_64.AppImage");
            Assert.True(appImage.IsAvailable && appImage.IsAppImage);
            Assert.Equal(Path.Combine(directory, "Zelda64-x86_64.AppImage"), appImage.Executable);
        }

        [Fact]
        public void TheGamesPaths_TakeTheirPlaceInTheArguments()
        {
            RecompLayout asks = Layout("x");
            Assert.True(asks.AsksForTheGame);
            Assert.Empty(asks.Arguments("/roms/a b.z64", "/roms"));
            RecompLayout told = Layout("x", arguments: "--rom {rom} --data \"{folder}/data dir\"  -f");
            Assert.False(told.AsksForTheGame);
            Assert.Equal(new[] { "--rom", "/roms/a b.z64", "--data", "/roms/data dir", "-f" }, told.Arguments("/roms/a b.z64", "/roms"));
            Assert.True(Layout("x", arguments: "--fullscreen").AsksForTheGame);
        }

        [Fact]
        public void ANewBuild_GoesOverTheOldOne_AndTheSavesStay()
        {
            string root = Path.Combine(Path.GetTempPath(), "gv-recomp-" + Guid.NewGuid().ToString("N"));
            try
            {
                string installed = Path.Combine(root, "installed"), unpacked = Path.Combine(root, "unpacked");
                Directory.CreateDirectory(Path.Combine(installed, "save"));
                Directory.CreateDirectory(Path.Combine(installed, "runtime"));
                File.WriteAllText(Path.Combine(installed, "save", "slot1.sav"), "mine");
                File.WriteAllText(Path.Combine(installed, "fefw-launcher.exe"), "old");
                File.WriteAllText(Path.Combine(installed, "runtime", "kept.bin"), "built");
                Directory.CreateDirectory(Path.Combine(unpacked, "fefw-0.1.6", "runtime"));
                File.WriteAllText(Path.Combine(unpacked, "fefw-0.1.6", "fefw-launcher.exe"), "new");
                File.WriteAllText(Path.Combine(unpacked, "fefw-0.1.6", "runtime", "a.dll"), "new");

                RecompLayout layout = Layout(installed);
                Assert.False(layout.IsInstalled);
                // The release's one folder is not kept
                Assert.Equal(Path.Combine(unpacked, "fefw-0.1.6"), RecompLayout.Root(unpacked));
                RecompLayout.Merge(RecompLayout.Root(unpacked), installed);
                File.WriteAllText(layout.VersionFile, layout.PackageUrl);

                Assert.True(layout.IsInstalled);
                Assert.Equal("new", File.ReadAllText(layout.Executable));
                Assert.Equal("mine", File.ReadAllText(Path.Combine(installed, "save", "slot1.sav")));
                Assert.True(File.Exists(Path.Combine(installed, "runtime", "kept.bin")) && File.Exists(Path.Combine(installed, "runtime", "a.dll")));
                // Another link given: installed again
                Assert.False(new RecompLayout(installed, "https://x/v2.zip", null, null, true, false, "fefw-launcher.exe").IsInstalled);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
