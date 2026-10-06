using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>A new version of an installed game is published on the server.</summary>
    public class UpdateScenarios
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

        [AvaloniaFact]
        public async Task ANewVersion_IsAnnounced_AndTheUpdateKeepsSaves()
        {
            var session = await TestSession.GetAsync();
            Game v1 = session.Server.AddGame(201, "Updatable", GameType.LINUX_PORTABLE, TestSession.GameArchive(
                ("start.sh", "#!/bin/sh\necho v1\n"),
                ("data/only-v1.pak", "old level"),
                ("data/shared.pak", "v1 data")), "Updatable (v1.0) (L_P).tar.gz", "v1.0");

            await MainWindowViewModel.Instance.Downloads.TryStartDownload(v1);
            string installation = session.InstallFolder(v1);
            string files = Path.Combine(installation, "Files");
            await TestSession.WaitUntil(() => File.Exists(Path.Combine(files, "start.sh")) && InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == v1.ID),
                Timeout, "version 1.0 to be installed");
            Assert.Equal("/files/Updatable (v1.0) (L_P).tar.gz", InstalledGameState.Read(installation).FilePath);
            // Written by the game while playing
            Directory.CreateDirectory(Path.Combine(files, "saves"));
            File.WriteAllText(Path.Combine(files, "saves", "slot1.sav"), "level 7");

            Game v2 = session.Server.AddGame(201, "Updatable", GameType.LINUX_PORTABLE, TestSession.GameArchive(
                ("start.sh", "#!/bin/sh\necho v2\n"),
                ("data/shared.pak", "v2 data"),
                ("data/new-in-v2.pak", "new level")), "Updatable (v1.1) (L_P).tar.gz", "v1.1");
            // The client learns about it like at the next start (or F5)
            await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
            Assert.Contains(InstalledGameState.GamesWithUpdates(), g => g.ID == v2.ID);
            Assert.Contains("Updatable v1.1", MainWindowViewModel.Instance.AppBarText);

            await MainWindowViewModel.Instance.Downloads.UpdateGame(v2);
            await TestSession.WaitUntil(() => InstalledGameState.Read(installation).FilePath == v2.Path, Timeout, "the update to be installed");

            Assert.Contains("v2", File.ReadAllText(Path.Combine(files, "start.sh")));
            Assert.Equal("v2 data", File.ReadAllText(Path.Combine(files, "data", "shared.pak")));
            Assert.True(File.Exists(Path.Combine(files, "data", "new-in-v2.pak")));
            Assert.False(File.Exists(Path.Combine(files, "data", "only-v1.pak")));
            Assert.Equal("level 7", File.ReadAllText(Path.Combine(files, "saves", "slot1.sav")));
            Assert.Equal("v1.1", InstalledGameState.Read(installation).Version);
            await TestSession.WaitUntil(() => InstalledGameState.GamesWithUpdates().All(g => g.ID != v2.ID), TimeSpan.FromSeconds(5), "the update badge to disappear");
        }
    }
}
