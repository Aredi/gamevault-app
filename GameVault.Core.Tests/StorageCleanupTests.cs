using GameVault.Core.Storage;

namespace GameVault.Core.Tests
{
    public class StorageCleanupTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-cleanup-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        private string MakeFile(string relative, int bytes)
        {
            string file = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, new byte[bytes]);
            return file;
        }

        [Fact]
        public void Scan_FindsArchivesDownloadsPrefixesAndUnusedTools()
        {
            MakeFile("lib/GameVault/Downloads/(1)Celeste/Celeste.zip", 1000);
            MakeFile("lib/GameVault/Downloads/(2)Tar Game/game.tar.gz", 500);
            MakeFile("lib/GameVault/Downloads/(3)Downloading/part.zip", 10);
            MakeFile("lib/GameVault/Downloads/not-a-game/x", 10);
            MakeFile("prefixes/1/drive_c/a", 100);
            MakeFile("prefixes/9/drive_c/a", 200);
            MakeFile("tools/GE-Proton11-7/proton", 50);
            MakeFile("tools/wine-11.19/bin/wine", 60);
            MakeFile("tools/.download-123/x", 60);

            var candidates = StorageCleanup.Scan(new CleanupInputs
            {
                RootDirectories = new[] { Path.Combine(root, "lib") },
                InstalledGameIds = new HashSet<int> { 1 },
                ActiveDownloadIds = new HashSet<int> { 3 },
                PrefixesDirectory = Path.Combine(root, "prefixes"),
                ManagedToolsDirectory = Path.Combine(root, "tools"),
                UsedToolDirectories = new HashSet<string> { Path.Combine(root, "tools", "GE-Proton11-7") + "/" },
            });

            Assert.Equal(4, candidates.Count);
            var archive = candidates.Single(c => c.Kind == CleanupKind.InstalledGameArchive);
            Assert.Equal("Celeste", archive.Title);
            Assert.Equal(1000, archive.Size);
            Assert.True(archive.SelectedByDefault);
            var download = candidates.Single(c => c.Kind == CleanupKind.NotInstalledDownload);
            Assert.Equal("Tar Game", download.Title);
            Assert.False(download.SelectedByDefault);
            Assert.Equal(Path.Combine(root, "prefixes", "9"), candidates.Single(c => c.Kind == CleanupKind.OrphanPrefix).Path);
            Assert.Equal("wine-11.19", candidates.Single(c => c.Kind == CleanupKind.UnusedCompatibilityTool).Title);
        }

        [Fact]
        public void FormatSize_UsesBinaryUnits()
        {
            Assert.Equal("512 B", StorageCleanup.FormatSize(512));
            Assert.Equal($"{1.5:0.#} KB", StorageCleanup.FormatSize(1536));// shown in the user's culture
            Assert.Equal("2 GB", StorageCleanup.FormatSize(2L * 1024 * 1024 * 1024));
        }
    }
}
