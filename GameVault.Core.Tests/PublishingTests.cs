using GameVault.Core.Publishing;
using System.IO.Compression;
using System.Text;

namespace GameVault.Core.Tests
{
    public class PublishingTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gv-publish-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch { }
        }

        private string Make(string relative, int bytes = 10, string? content = null)
        {
            string file = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (content != null)
                File.WriteAllText(file, content);
            else
                File.WriteAllBytes(file, new byte[bytes]);
            return file;
        }

        [Fact]
        public void FileName_FollowsTheServerConvention()
        {
            Assert.Equal("Hades (v1.38) (W_P) (2020).zip", GameFileName.Build("Hades", "1.38", PublishedGameType.WindowsPortable, 2020, false));
            Assert.Equal("Elden Ring (v1.10) (EA) (W_S) (2022).zip", GameFileName.Build("Elden Ring", "v1.10", PublishedGameType.WindowsSetup, 2022, true));
            Assert.Equal("Celeste (L_P).zip", GameFileName.Build("Celeste", null, PublishedGameType.LinuxPortable, null, false));
            Assert.Equal("Death on the Nile [Remake] Part 2 (W_P).zip", GameFileName.Build("Death on the Nile (Remake): Part 2?", "", PublishedGameType.WindowsPortable, 1800, false));
            Assert.Throws<ArgumentException>(() => GameFileName.Build("???", null, PublishedGameType.WindowsPortable, null, false));
        }

        [Fact]
        public void GuessTitle_CleansFolderNames()
        {
            Assert.Equal("Hades", GameFileName.GuessTitle("Hades_v1.38.2-GOG"));
            Assert.Equal("A Short Hike", GameFileName.GuessTitle("A.Short.Hike [Portable]"));
            Assert.Equal("Celeste", GameFileName.GuessTitle("Celeste (2018).zip"));
            Assert.Equal("Nile Adventure", GameFileName.GuessTitle("/games/Nile Adventure Setup (2021)/setup.exe"));
            Assert.Equal("Nile Adventure Setup (2021)", GameFileName.DescriptiveName("/games/Nile Adventure Setup (2021)/Setup.exe"));
            Assert.Equal("Hades.exe", GameFileName.DescriptiveName("/games/x/Hades.exe"));
        }

        [Fact]
        public void Installers_AreRecognized()
        {
            Assert.Equal(InstallerKind.InnoSetup, InstallerDetector.Detect(Make("a/setup.exe", content: "MZ....Inno Setup Setup Data (6.2.0)....")).Kind);
            Assert.Contains("%INSTALLDIR%", InstallerDetector.Detect(Make("b/setup.exe", content: "MZ Inno Setup")).SilentParameters);
            var nsis = InstallerDetector.Detect(Make("c/install.exe", content: "MZ...NullsoftInst..."));
            Assert.Equal(InstallerKind.Nsis, nsis.Kind);
            Assert.EndsWith("/D=%INSTALLDIR%", nsis.SilentParameters);
            Assert.Equal(InstallerKind.Msi, InstallerDetector.Detect(Make("d/game.msi")).Kind);
            Assert.Equal(InstallerKind.Unknown, InstallerDetector.Detect(Make("e/game.exe", content: "MZ plain")).Kind);
        }

        [Fact]
        public void MainExecutable_IsRankedFirst()
        {
            Make("game/Hades/x64/Hades.exe", 40_000_000);
            Make("game/Hades/x64/CrashReporter.exe", 50_000_000);
            Make("game/_CommonRedist/vcredist/vc_redist.x64.exe", 60_000_000);
            Make("game/unins000.exe", 1_000_000);
            Make("game/Launcher.exe", 2_000_000);
            var ranked = ExecutableFinder.Rank(Path.Combine(root, "game"), "Hades", new[] { "unins000", "vc_redist.x64" });
            Assert.Equal("Hades/x64/Hades.exe", ranked[0].RelativePath);
            Assert.Equal(5, ranked.Count);
        }

        [Fact]
        public void Installers_AreFoundAtTheTop()
        {
            Make("setupgame/setup.exe", content: "MZ Inno Setup");
            Make("setupgame/setup-1.bin", 100);
            Make("setupgame/readme.txt", 1);
            Assert.Equal(new[] { "setup.exe" }, ExecutableFinder.FindInstallers(Path.Combine(root, "setupgame")));
        }

        [Fact]
        public async Task Archive_ContainsTheFolder_AndAppearsOnlyWhenComplete()
        {
            Make("pack/Game.exe", 1000);
            Make("pack/data/level1.dat", 5000);
            string target = Path.Combine(root, "out", "Game (W_P).zip");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            double last = 0;
            await GamePackager.CreateArchiveAsync(Path.Combine(root, "pack"), target, compress: false, new SyncProgress(p => last = p));
            Assert.True(File.Exists(target));
            Assert.False(File.Exists(target + ".partial"));
            Assert.Equal(1.0, last, 3);
            using var zip = ZipFile.OpenRead(target);
            Assert.Equal(new[] { "Game.exe", "data/level1.dat" }.OrderBy(n => n, StringComparer.Ordinal), zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
        }

        private sealed class SyncProgress : IProgress<double>
        {
            private readonly Action<double> action;
            public SyncProgress(Action<double> action) => this.action = action;
            public void Report(double value) => action(value);
        }
    }
}
