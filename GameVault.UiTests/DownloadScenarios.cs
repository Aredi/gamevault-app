using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>What a user does with downloads, from the click to the running game.</summary>
    public class DownloadScenarios
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

        private static GameDownloadUserControl Download(int gameId) =>
            DownloadsViewModel.Instance.DownloadedGames.First(d => d.GetGameId() == gameId);

        [AvaloniaFact]
        public async Task InstallAndPlay_DownloadsExtractsInstallsAndStartsTheGame()
        {
            if (!OperatingSystem.IsLinux())
                return;// the game is a shell script
            var session = await TestSession.GetAsync();
            Game game = session.Server.AddGame(101, "Tiny Game", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(200_000), "Tiny Game (L_P).tar.gz");

            await MainWindowViewModel.Instance.Downloads.InstallAndPlay(game);

            string started = Path.Combine(session.InstallFolder(game), "Files", "started");
            await TestSession.WaitUntil(() => File.Exists(started), Timeout, "the game to start");
            Assert.Contains(InstallViewModel.Instance.InstalledGames, g => g.Key.ID == game.ID);
        }

        [AvaloniaFact]
        public async Task ADownloadCutByTheServer_ResumesWhereItStopped()
        {
            var session = await TestSession.GetAsync();
            byte[] archive = TestSession.LinuxGameArchive(3_000_000);
            Game game = session.Server.AddGame(102, "Cut Game", GameType.LINUX_PORTABLE, archive, "Cut Game (L_P).tar.gz");
            session.Server.CutNextDownloadAfter[game.ID] = 1_000_000;

            await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);

            string file = Path.Combine(session.DownloadFolder(game), "Cut Game (L_P).tar.gz");
            await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.DownloadFolder(game), "Extract", "start.sh")), Timeout, "the resumed download to be extracted");
            Assert.Equal(archive, File.ReadAllBytes(file));
            // The retry asked for the rest only
            Assert.Contains(session.Server.RangeHeaders, r => r.StartsWith("bytes=") && !r.StartsWith("bytes=0-"));
        }

        [AvaloniaFact]
        public async Task AServerWithoutResumeSupport_RestartsTheDownloadInsteadOfCorruptingIt()
        {
            var session = await TestSession.GetAsync();
            byte[] archive = TestSession.LinuxGameArchive(3_000_000);
            Game game = session.Server.AddGame(103, "No Range Game", GameType.LINUX_PORTABLE, archive, "No Range Game (L_P).tar.gz");
            session.Server.CutNextDownloadAfter[game.ID] = 1_000_000;
            session.Server.IgnoreRange = true;
            try
            {
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);

                await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.DownloadFolder(game), "Extract", "start.sh")), Timeout, "the restarted download to be extracted");
                Assert.Equal(archive, File.ReadAllBytes(Path.Combine(session.DownloadFolder(game), "No Range Game (L_P).tar.gz")));
            }
            finally
            {
                session.Server.IgnoreRange = false;
            }
        }

        [AvaloniaFact]
        public async Task WithOneDownloadAtATime_TheSecondGameWaitsInTheQueue()
        {
            var session = await TestSession.GetAsync();
            Game first = session.Server.AddGame(104, "Queue First", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(1_500_000), "Queue First (L_P).tar.gz");
            Game second = session.Server.AddGame(105, "Queue Second", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(100_000), "Queue Second (L_P).tar.gz");
            SettingsViewModel.Instance.MaxConcurrentDownloadsIndex = 1;
            session.Server.ChunkDelay = TimeSpan.FromMilliseconds(50);
            try
            {
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(first);
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(second);

                Assert.True(Download(first.ID).IsDownloading());
                Assert.True(DownloadQueue.IsWaiting(Download(second.ID)));

                session.Server.ChunkDelay = TimeSpan.Zero;
                await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.DownloadFolder(second), "Extract", "start.sh")), Timeout, "the queued game to be downloaded after the first one");
                Assert.True(File.Exists(Path.Combine(session.DownloadFolder(first), "Extract", "start.sh")));
            }
            finally
            {
                session.Server.ChunkDelay = TimeSpan.Zero;
                SettingsViewModel.Instance.MaxConcurrentDownloadsIndex = 0;
            }
        }
    }
}

namespace GameVault.UiTests
{
    /// <summary>Big archives come in several parts at once; damaged ones are downloaded again.</summary>
    public class ParallelDownloadScenarios
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

        [AvaloniaFact]
        public async Task ABigArchive_IsDownloadedInSeveralRangesAtOnce()
        {
            var session = await TestSession.GetAsync();
            byte[] archive = TestSession.LinuxGameArchive(70 * 1024 * 1024);
            Game game = session.Server.AddGame(301, "Big Game", GameType.LINUX_PORTABLE, archive, "Big Game (L_P).tar.gz");
            int rangesBefore = session.Server.RangeHeaders.Count;

            await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);

            await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.DownloadFolder(game), "Extract", "start.sh")), Timeout, "the parallel download to be extracted");
            Assert.Equal(archive, File.ReadAllBytes(Path.Combine(session.DownloadFolder(game), "Big Game (L_P).tar.gz")));
            var ranges = session.Server.RangeHeaders.Skip(rangesBefore).ToList();
            Assert.Contains("bytes=0-", ranges);
            Assert.Contains(ranges, r => System.Text.RegularExpressions.Regex.IsMatch(r, @"^bytes=[1-9]\d*-\d+$"));
        }

        [AvaloniaFact]
        public async Task ABigArchive_FromAStandardHttpServer_IsAlsoDownloadedInRanges()
        {
            var session = await TestSession.GetAsync();
            byte[] archive = TestSession.LinuxGameArchive(70 * 1024 * 1024);
            Game game = session.Server.AddGame(304, "Big Standard Game", GameType.LINUX_PORTABLE, archive, "Big Standard Game (L_P).tar.gz");
            session.Server.StandardRanges = true;
            int rangesBefore = session.Server.RangeHeaders.Count;
            try
            {
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);

                await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.DownloadFolder(game), "Extract", "start.sh")), Timeout, "the parallel download to be extracted");
                Assert.Equal(archive, File.ReadAllBytes(Path.Combine(session.DownloadFolder(game), "Big Standard Game (L_P).tar.gz")));
                Assert.Contains(session.Server.RangeHeaders.Skip(rangesBefore), r => System.Text.RegularExpressions.Regex.IsMatch(r, @"^bytes=[1-9]\d*-\d+$"));
            }
            finally
            {
                session.Server.StandardRanges = false;
            }
        }

        [AvaloniaFact]
        public async Task AnInterruptedParallelDownload_ContinuesEachRange()
        {
            var session = await TestSession.GetAsync();
            byte[] archive = TestSession.LinuxGameArchive(70 * 1024 * 1024);
            Game game = session.Server.AddGame(302, "Big Cut Game", GameType.LINUX_PORTABLE, archive, "Big Cut Game (L_P).tar.gz");
            // The first connection (first range) breaks after 5 MB
            session.Server.CutNextDownloadAfter[game.ID] = 5 * 1024 * 1024;
            int rangesBefore = session.Server.RangeHeaders.Count;

            await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);

            await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.DownloadFolder(game), "Extract", "start.sh")), Timeout, "the resumed parallel download to be extracted");
            Assert.Equal(archive, File.ReadAllBytes(Path.Combine(session.DownloadFolder(game), "Big Cut Game (L_P).tar.gz")));
            // The first range was continued, not started again
            var ranges = session.Server.RangeHeaders.Skip(rangesBefore).ToList();
            Assert.Contains(ranges, r => System.Text.RegularExpressions.Regex.IsMatch(r, @"^bytes=[1-9]\d*-\d+$") && long.Parse(r[6..r.IndexOf('-')]) < archive.Length / 2);
        }

        [AvaloniaFact]
        public async Task ADamagedArchive_IsDownloadedAgainAndInstalled()
        {
            var session = await TestSession.GetAsync();
            byte[] archive = TestSession.LinuxGameArchive(2 * 1024 * 1024);
            Game game = session.Server.AddGame(303, "Damaged Game", GameType.LINUX_PORTABLE, archive, "Damaged Game (L_P).tar.gz");
            session.Server.DamageNextDownload[game.ID] = true;
            int downloadsBefore = session.Server.Requests.Count(r => r.StartsWith($"GET /api/games/{game.ID}/download"));

            await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);

            await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.InstallFolder(game), "Files", "start.sh")), Timeout, "the game to be installed from a good archive");
            Assert.Equal(archive, File.ReadAllBytes(Path.Combine(session.DownloadFolder(game), "Damaged Game (L_P).tar.gz")));
            Assert.Equal(2, session.Server.Requests.Count(r => r.StartsWith($"GET /api/games/{game.ID}/download")) - downloadsBefore);
        }
    }
}
