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

        private static byte[] Noise(int size, int seed)
        {
            byte[] bytes = new byte[size];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        private static byte[] Zip(params (string Path, byte[] Content)[] files)
        {
            using var memory = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                foreach (var (path, content) in files)
                {
                    using Stream entry = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.Fastest).Open();
                    entry.Write(content);
                }
            }
            return memory.ToArray();
        }

        private static byte[] Text(string text) => System.Text.Encoding.UTF8.GetBytes(text);

        /// <summary>Installs version 1 (start.sh, data/level.pak, data/textures.pak, data/only-v1.pak) and a save.</summary>
        private static async Task<(Game Game, string Installation)> InstallV1(TestSession session, int id, string title, byte[] textures)
        {
            Game v1 = session.Server.AddGame(id, title, GameType.LINUX_PORTABLE, Zip(
                ("start.sh", Text("#!/bin/sh\necho v1\n")),
                ("data/level.pak", Noise(100_000, 1)),
                ("data/textures.pak", textures),
                ("data/only-v1.pak", Text("old level"))), $"{title} (v1.0) (L_P).zip", "v1.0");
            await MainWindowViewModel.Instance.Downloads.TryStartDownload(v1);
            string installation = session.InstallFolder(v1);
            string files = Path.Combine(installation, "Files");
            await TestSession.WaitUntil(() => File.Exists(Path.Combine(files, "start.sh")) && InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == v1.ID),
                Timeout, "version 1.0 to be installed");
            File.SetUnixFileMode(Path.Combine(files, "start.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(Path.Combine(files, "saves"));
            File.WriteAllText(Path.Combine(files, "saves", "slot1.sav"), "level 7");
            return (v1, installation);
        }

        [AvaloniaFact]
        public async Task AnUpdateOfAZip_FetchesOnlyTheFilesThatChanged()
        {
            var session = await TestSession.GetAsync();
            // Larger than the end of the zip the client always reads
            byte[] textures = Noise(9_000_000, 2);
            var (v1, installation) = await InstallV1(session, 202, "Pieces", textures);
            string files = Path.Combine(installation, "Files");

            byte[] level = Noise(120_000, 3);
            byte[] zip = Zip(
                ("start.sh", Text("#!/bin/sh\necho v2\n")),
                ("data/level.pak", level),
                ("data/new-in-v2.pak", Text("new level")),
                ("data/textures.pak", textures));
            Game v2 = session.Server.AddGame(202, "Pieces", GameType.LINUX_PORTABLE, zip, "Pieces (v1.1) (L_P).zip", "v1.1");
            await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
            int rangesBefore = session.Server.RangeHeaders.Count;

            await MainWindowViewModel.Instance.Downloads.UpdateGame(v2);
            await TestSession.WaitUntil(() => InstalledGameState.Read(installation).FilePath == v2.Path, Timeout, "the update to be installed");

            // The zip was not downloaded: its end (the list of files), then the changed files from its start
            var ranges = session.Server.RangeHeaders.Skip(rangesBefore).ToList();
            Assert.Equal(new[] { $"bytes={zip.Length - 4 * 1024 * 1024}-", "bytes=0-" }, ranges);
            Assert.Contains("v2", File.ReadAllText(Path.Combine(files, "start.sh")));
            Assert.True(File.GetUnixFileMode(Path.Combine(files, "start.sh")).HasFlag(UnixFileMode.UserExecute), "the start script is still a program");
            Assert.Equal(level, File.ReadAllBytes(Path.Combine(files, "data", "level.pak")));
            Assert.Equal("new level", File.ReadAllText(Path.Combine(files, "data", "new-in-v2.pak")));
            Assert.Equal(textures, File.ReadAllBytes(Path.Combine(files, "data", "textures.pak")));
            Assert.False(File.Exists(Path.Combine(files, "data", "only-v1.pak")));
            Assert.Equal("level 7", File.ReadAllText(Path.Combine(files, "saves", "slot1.sav")));
            Assert.Equal("v1.1", InstalledGameState.Read(installation).Version);

            Assert.False(File.Exists(Path.Combine(session.DownloadFolder(v2), "Pieces (v1.1) (L_P).zip")));
            await TestSession.WaitUntil(() => MainWindowViewModel.Instance.AppBarText.Contains("downloaded instead of"), TimeSpan.FromSeconds(5), "the saving to be told");
            Assert.False(Directory.Exists(session.DownloadFolder(v2)), "nothing is left in the downloads");
        }

        [AvaloniaFact]
        public async Task AnUpdateThatChangesAlmostEverything_DownloadsTheWholeArchive()
        {
            var session = await TestSession.GetAsync();
            var (v1, installation) = await InstallV1(session, 203, "Rebuilt", Noise(300_000, 2));
            string files = Path.Combine(installation, "Files");

            byte[] textures = Noise(300_000, 7);
            Game v2 = session.Server.AddGame(203, "Rebuilt", GameType.LINUX_PORTABLE, Zip(
                ("start.sh", Text("#!/bin/sh\necho v2\n")),
                ("data/level.pak", Noise(100_000, 1)),
                ("data/textures.pak", textures)), "Rebuilt (v2.0) (L_P).zip", "v2.0");
            await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();

            await MainWindowViewModel.Instance.Downloads.UpdateGame(v2);
            await TestSession.WaitUntil(() => InstalledGameState.Read(installation).FilePath == v2.Path, Timeout, "the update to be installed");

            Assert.Equal(textures, File.ReadAllBytes(Path.Combine(files, "data", "textures.pak")));
            Assert.Contains("v2", File.ReadAllText(Path.Combine(files, "start.sh")));
            Assert.False(File.Exists(Path.Combine(files, "data", "only-v1.pak")));
            Assert.Equal("level 7", File.ReadAllText(Path.Combine(files, "saves", "slot1.sav")));
            Assert.DoesNotContain("downloaded instead of", MainWindowViewModel.Instance.AppBarText);
        }
    }
}
