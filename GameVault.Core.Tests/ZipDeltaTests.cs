using System.IO.Compression;
using GameVault.Core.Downloads;
using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    /// <summary>An update fetches only the files of the new zip that changed.</summary>
    public class ZipDeltaTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-delta-{Guid.NewGuid():N}");
        private string Installation => Path.Combine(root, "installation");
        private string Files => Path.Combine(Installation, "Files");
        private string Staging => Path.Combine(root, "staging");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        private static byte[] Noise(int size, int seed)
        {
            byte[] bytes = new byte[size];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        private static byte[] Zip(params (string Path, byte[] Content)[] files)
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                foreach (var (path, content) in files)
                {
                    using Stream entry = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                    entry.Write(content);
                }
            }
            return memory.ToArray();
        }

        private void Install(params (string Path, byte[] Content)[] files)
        {
            foreach (var (path, content) in files)
            {
                string file = Path.Combine(Files, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, content);
            }
            InstallManifest.Write(Installation, InstallManifest.List(Files));
        }

        /// <summary>The zip as the server gives it, by parts; <paramref name="requests"/> counts the connections.</summary>
        private static RangeStream Served(byte[] zip, List<long>? requests = null) =>
            new(zip.Length, from =>
            {
                requests?.Add(from);
                return new MemoryStream(zip, (int)from, zip.Length - (int)from, false);
            }, tailSize: 16384);

        private List<string> Update(byte[] zip, RangeStream served)
        {
            using var archive = new ZipArchive(served, ZipArchiveMode.Read);
            DeltaPlan plan = ZipDelta.Plan(archive, Files, FileSums.Read(Installation))!;
            ZipDelta.Fetch(plan, Staging, Files);
            var files = InstallManifest.ApplyUpdate(Staging, Files, InstallManifest.Read(Installation), plan.Files.Select(file => file.Path).ToList());
            InstallManifest.Write(Installation, files);
            FileSums.Write(Installation, Files, plan.Files);
            return plan.Changed.Select(entry => entry.FullName).ToList();
        }

        [Fact]
        public void OnlyTheChangedFiles_AreFetched_AndTheGameIsTheNewVersion()
        {
            byte[] textures = Noise(3_000_000, 1);
            byte[] music = Noise(2_000_000, 2);
            Install(("game.exe", Noise(50_000, 3)), ("data/textures.pak", textures), ("data/music.pak", music), ("data/only-v1.pak", Noise(1000, 4)));
            File.WriteAllText(Path.Combine(Files, "save.dat"), "level 7");

            byte[] newExe = Noise(50_000, 5);
            byte[] newLevel = Noise(20_000, 6);
            byte[] zip = Zip(("game.exe", newExe), ("data/textures.pak", textures), ("data/music.pak", music), ("data/new-level.pak", newLevel));
            RangeStream served = Served(zip);

            List<string> changed = Update(zip, served);

            Assert.Equal(new[] { "game.exe", "data/new-level.pak" }, changed);
            Assert.True(served.Fetched < 400_000, $"{served.Fetched} bytes fetched of a zip of {zip.Length}");
            Assert.Equal(newExe, File.ReadAllBytes(Path.Combine(Files, "game.exe")));
            Assert.Equal(newLevel, File.ReadAllBytes(Path.Combine(Files, "data", "new-level.pak")));
            Assert.Equal(textures, File.ReadAllBytes(Path.Combine(Files, "data", "textures.pak")));
            Assert.False(File.Exists(Path.Combine(Files, "data", "only-v1.pak")));
            Assert.Equal("level 7", File.ReadAllText(Path.Combine(Files, "save.dat")));
            Assert.Equal(new[] { "data/music.pak", "data/new-level.pak", "data/textures.pak", "game.exe" }, InstallManifest.Read(Installation));
        }

        [Fact]
        public void AFileOfTheSameSize_ButAnotherContent_IsFetched()
        {
            Install(("a.pak", Noise(10_000, 1)), ("b.pak", Noise(10_000, 2)));
            byte[] zip = Zip(("a.pak", Noise(10_000, 1)), ("b.pak", Noise(10_000, 9)));

            Assert.Equal(new[] { "b.pak" }, Update(zip, Served(zip)));
            Assert.Equal(Noise(10_000, 9), File.ReadAllBytes(Path.Combine(Files, "b.pak")));
        }

        [Fact]
        public void TheSecondUpdate_DoesNotReadTheInstalledFilesAgain()
        {
            Install(("a.pak", Noise(10_000, 1)), ("b.pak", Noise(10_000, 2)));
            byte[] v2 = Zip(("a.pak", Noise(10_000, 1)), ("b.pak", Noise(10_000, 3)));
            Update(v2, Served(v2));

            // Same size and date as recorded, another content: only a file read again would show it
            string a = Path.Combine(Files, "a.pak");
            DateTime written = File.GetLastWriteTimeUtc(a);
            File.WriteAllBytes(a, Noise(10_000, 8));
            File.SetLastWriteTimeUtc(a, written);
            Assert.Empty(Update(v2, Served(v2)));

            // Changed since (another date): read again, and fetched
            File.SetLastWriteTimeUtc(a, written.AddSeconds(5));
            Assert.Equal(new[] { "a.pak" }, Update(v2, Served(v2)));
            Assert.Equal(Noise(10_000, 1), File.ReadAllBytes(a));
        }

        [Fact]
        public void AnInterruptedUpdate_LeavesTheGameAsItWas_AndGoesOnWhereItStopped()
        {
            byte[] oldA = Noise(300_000, 1), oldB = Noise(300_000, 2);
            Install(("a.pak", oldA), ("b.pak", oldB));
            byte[] newA = Noise(300_000, 3), newB = Noise(300_000, 4);
            byte[] zip = Zip(("a.pak", newA), ("b.pak", newB));

            // The connection breaks in the middle of the second file
            var broken = new RangeStream(zip.Length, from => new MemoryStream(zip, (int)from, from >= zip.Length - 16384 ? zip.Length - (int)from : Math.Max(0, 450_000 - (int)from), false), tailSize: 16384);
            Assert.ThrowsAny<IOException>(() => Update(zip, broken));
            Assert.Equal(oldA, File.ReadAllBytes(Path.Combine(Files, "a.pak")));
            Assert.Equal(oldB, File.ReadAllBytes(Path.Combine(Files, "b.pak")));

            RangeStream served = Served(zip);
            Update(zip, served);
            Assert.True(served.Fetched < 400_000, $"{served.Fetched} bytes fetched: the first file was there already");
            Assert.Equal(newA, File.ReadAllBytes(Path.Combine(Files, "a.pak")));
            Assert.Equal(newB, File.ReadAllBytes(Path.Combine(Files, "b.pak")));
        }

        [Fact]
        public void FilesThatFollowEachOther_AreReadFromOneConnection()
        {
            Install(("keep.pak", Noise(500_000, 1)));
            byte[] zip = Zip(("keep.pak", Noise(500_000, 1)), ("a.pak", Noise(40_000, 2)), ("b.pak", Noise(40_000, 3)), ("c.pak", Noise(40_000, 4)));
            var requests = new List<long>();

            Assert.Equal(3, Update(zip, Served(zip, requests)).Count);
            // The end of the zip (its directory), then the three files in a row
            Assert.Equal(2, requests.Count);
        }

        [Fact]
        public void AReplacedProgram_StaysAProgram()
        {
            if (OperatingSystem.IsWindows())
                return;
            Install(("start.sh", Noise(100, 1)), ("data.pak", Noise(100, 2)));
            File.SetUnixFileMode(Path.Combine(Files, "start.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            byte[] zip = Zip(("start.sh", Noise(100, 3)), ("data.pak", Noise(100, 4)));

            Update(zip, Served(zip));

            Assert.True(File.GetUnixFileMode(Path.Combine(Files, "start.sh")).HasFlag(UnixFileMode.UserExecute));
            Assert.False(File.GetUnixFileMode(Path.Combine(Files, "data.pak")).HasFlag(UnixFileMode.UserExecute));
        }

        [Fact]
        public void ADamagedFile_IsRefused()
        {
            Install(("a.pak", Noise(10_000, 1)));
            byte[] zip = Zip(("a.pak", Noise(100_000, 2)));
            byte[] damaged = (byte[])zip.Clone();
            for (int i = 50_000; i < 50_100; i++)
                damaged[i] ^= 0x5A;

            Assert.ThrowsAny<InvalidDataException>(() => Update(damaged, Served(damaged)));
            Assert.Equal(Noise(10_000, 1), File.ReadAllBytes(Path.Combine(Files, "a.pak")));
            Assert.False(File.Exists(Path.Combine(Staging, "a.pak")));
        }

        [Theory]
        [InlineData("../outside.txt")]
        [InlineData("data/../../outside.txt")]
        [InlineData("/etc/passwd")]
        [InlineData("C:/Windows/x.dll")]
        [InlineData("data\\x.pak")]
        public void AZipWithNamesThatLeaveTheFolder_IsNotAppliedByPieces(string name)
        {
            Install(("a.pak", Noise(100, 1)));
            byte[] zip = Zip(("a.pak", Noise(100, 1)), (name, Noise(100, 2)));
            using var archive = new ZipArchive(Served(zip), ZipArchiveMode.Read);

            Assert.Null(ZipDelta.Plan(archive, Files, FileSums.Read(Installation)));
        }
    }
}
