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
