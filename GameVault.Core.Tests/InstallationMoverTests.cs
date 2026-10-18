using GameVault.Core.Storage;

namespace GameVault.Core.Tests
{
    public class InstallationMoverTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-move-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        private string MakeGame()
        {
            string game = Path.Combine(root, "a", "GameVault", "Installations", "(42)Celeste");
            Directory.CreateDirectory(Path.Combine(game, "data"));
            File.WriteAllBytes(Path.Combine(game, "Celeste.exe"), new byte[3000]);
            File.WriteAllText(Path.Combine(game, "data", "save.txt"), "level 7");
            File.WriteAllText(Path.Combine(game, "gamevault-exec"), "");
            return game;
        }

        private static void AssertGameAt(string game)
        {
            Assert.Equal(3000, new FileInfo(Path.Combine(game, "Celeste.exe")).Length);
            Assert.Equal("level 7", File.ReadAllText(Path.Combine(game, "data", "save.txt")));
            Assert.True(File.Exists(Path.Combine(game, "gamevault-exec")));
        }

        [Fact]
        public async Task MoveAsync_PutsTheGameInTheInstallationsOfTheOtherFolder()
        {
            string game = MakeGame();
            double last = 0;

            string moved = await InstallationMover.MoveAsync(game, Path.Combine(root, "b"), new SynchronousProgress(p => last = p));

            Assert.Equal(Path.Combine(root, "b", "GameVault", "Installations", "(42)Celeste"), moved);
            AssertGameAt(moved);
            Assert.False(Directory.Exists(game));
            Assert.Equal(1, last);
        }

        [Fact]
        public async Task CopyThenSwap_OtherDisk_CopiesEverythingAndRemovesTheOldFolder()
        {
            string game = MakeGame();
            string installations = Path.Combine(root, "b", "GameVault", "Installations");
            Directory.CreateDirectory(installations);
            var reported = new List<double>();

            await InstallationMover.CopyThenSwapAsync(game, installations, "(42)Celeste", new SynchronousProgress(reported.Add), CancellationToken.None);

            AssertGameAt(Path.Combine(installations, "(42)Celeste"));
            Assert.False(Directory.Exists(game));
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(game)!));
            Assert.Single(Directory.GetDirectories(installations));
            Assert.Equal(1, reported[^1]);
            Assert.Equal(reported.OrderBy(p => p), reported);
        }

        [Fact]
        public async Task CopyThenSwap_Cancelled_LeavesTheGameWhereItWas()
        {
            string game = MakeGame();
            string installations = Path.Combine(root, "b", "GameVault", "Installations");
            Directory.CreateDirectory(installations);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                InstallationMover.CopyThenSwapAsync(game, installations, "(42)Celeste", null, new CancellationToken(true)));

            AssertGameAt(game);
            Assert.Empty(Directory.GetDirectories(installations));
        }

        [Fact]
        public async Task MoveAsync_GameAlreadyInTheTarget_ChangesNothing()
        {
            string game = MakeGame();
            string other = Path.Combine(root, "b", "GameVault", "Installations", "(42)Celeste");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "x"), "");

            await Assert.ThrowsAsync<IOException>(() => InstallationMover.MoveAsync(game, Path.Combine(root, "b")));
            await Assert.ThrowsAsync<IOException>(() => InstallationMover.MoveAsync(game, Path.Combine(root, "a")));

            AssertGameAt(game);
        }

        private sealed class SynchronousProgress : IProgress<double>
        {
            private readonly Action<double> report;
            public SynchronousProgress(Action<double> report) => this.report = report;
            public void Report(double value) => report(value);
        }
    }
}
