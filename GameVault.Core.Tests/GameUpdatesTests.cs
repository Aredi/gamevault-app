using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class GameUpdatesTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-update-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        private void Write(string relative, string content = "x")
        {
            string file = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        [Fact]
        public void ANewVersionFile_IsAnUpdate()
        {
            var installed = new InstalledVersion("v1.38", "/files/Hades (v1.38) (W_P).zip", "100");
            Assert.True(GameUpdates.IsUpdateAvailable(installed, "v1.40", "/files/Hades (v1.40) (W_P).zip", "120"));
            Assert.False(GameUpdates.IsUpdateAvailable(installed, "v1.38", "/files/Hades (v1.38) (W_P).zip", "100"));
        }

        [Fact]
        public void TheSameFileReplaced_IsAnUpdate()
        {
            var installed = new InstalledVersion(null, "/files/Celeste (L_P).zip", "100");
            Assert.True(GameUpdates.IsUpdateAvailable(installed, null, "/files/Celeste (L_P).zip", "101"));
        }

        [Fact]
        public void OlderInstallations_CompareTheVersionText()
        {
            Assert.True(GameUpdates.IsUpdateAvailable(new InstalledVersion("v1.0", null, null), "v1.1", "/files/a.zip", "1"));
            Assert.False(GameUpdates.IsUpdateAvailable(new InstalledVersion("V1.0 ", null, null), "v1.0", "/files/a.zip", "1"));
            // Nothing known: no badge
            Assert.False(GameUpdates.IsUpdateAvailable(new InstalledVersion(null, null, null), "v1.1", "/files/a.zip", "1"));
        }

        [Fact]
        public void ApplyUpdate_ReplacesGameFiles_RemovesObsoleteOnes_AndKeepsSaves()
        {
            Write("installed/game.exe", "old");
            Write("installed/data/level1.pak", "old");
            Write("installed/data/old/only-v1.pak", "old");
            Write("installed/saves/slot1.sav", "my save");// written by the game
            var oldFiles = new[] { "game.exe", "data/level1.pak", "data/old/only-v1.pak" };
            Write("new/game.exe", "new");
            Write("new/data/level1.pak", "new");
            Write("new/data/level2.pak", "new");

            var files = InstallManifest.ApplyUpdate(Path.Combine(root, "new"), Path.Combine(root, "installed"), oldFiles);

            Assert.Equal(new[] { "data/level1.pak", "data/level2.pak", "game.exe" }, files);
            Assert.Equal("new", File.ReadAllText(Path.Combine(root, "installed/game.exe")));
            Assert.True(File.Exists(Path.Combine(root, "installed/data/level2.pak")));
            Assert.False(File.Exists(Path.Combine(root, "installed/data/old/only-v1.pak")));
            Assert.False(Directory.Exists(Path.Combine(root, "installed/data/old")));
            Assert.Equal("my save", File.ReadAllText(Path.Combine(root, "installed/saves/slot1.sav")));
        }

        [Fact]
        public void ApplyUpdate_WithoutAnOldFileList_DeletesNothing_AndNeverLeavesTheInstallation()
        {
            Write("installed/game.exe", "old");
            Write("installed/leftover.dll", "old");
            Write("outside.txt", "keep");
            Write("new/game.exe", "new");

            InstallManifest.ApplyUpdate(Path.Combine(root, "new"), Path.Combine(root, "installed"), null);
            Assert.True(File.Exists(Path.Combine(root, "installed/leftover.dll")));

            Write("new/game.exe", "newer");
            InstallManifest.ApplyUpdate(Path.Combine(root, "new"), Path.Combine(root, "installed"), new[] { "../outside.txt" });
            Assert.True(File.Exists(Path.Combine(root, "outside.txt")));
        }

        [Fact]
        public void Manifest_RoundTrips()
        {
            Write("inst/Files/a/b.txt");
            Write("inst/Files/c.txt");
            var list = InstallManifest.List(Path.Combine(root, "inst", "Files"));
            InstallManifest.Write(Path.Combine(root, "inst"), list);
            Assert.Equal(new[] { "a/b.txt", "c.txt" }, InstallManifest.Read(Path.Combine(root, "inst")));
            Assert.Null(InstallManifest.Read(Path.Combine(root, "nothing")));
        }
    }
}
