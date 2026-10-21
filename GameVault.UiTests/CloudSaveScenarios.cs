using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>The same game is played on two computers: their saves must not overwrite each other silently.</summary>
    public class CloudSaveScenarios
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
        private const string OtherComputer = "0b6c7f6e-5f0a-4c55-9a62-6d1f2b8e0001";

        private static async Task<(TestSession Session, Game Game, string Started)> InstalledGameWithLocalSave(int id, string title, string localSave)
        {
            var session = await TestSession.GetAsync();
            FakeLudusavi.Install();
            SettingsViewModel.Instance.CloudSaves = true;
            Game game = session.Server.AddGame(id, title, GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(1000), $"{title} (L_P).tar.gz");
            await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);
            string started = Path.Combine(session.InstallFolder(game), "Files", "started");
            await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.InstallFolder(game), "Files", "start.sh"))
                && InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == id), Timeout, $"{title} to be installed");
            Directory.CreateDirectory(Path.GetDirectoryName(FakeLudusavi.SaveFile(title))!);
            File.WriteAllText(FakeLudusavi.SaveFile(title), localSave);
            return (session, game, started);
        }

        private static void ServerSaveFromOtherComputer(TestSession session, int gameId, string title, string content, DateTime uploadedAt) =>
            session.Server.Saves[gameId] = ($"{new DateTimeOffset(uploadedAt).ToUnixTimeMilliseconds()}_{OtherComputer}.zip", FakeLudusavi.ServerSaveArchive(title, content));

        private static IconButton? DialogButton(TestSession session, string text) =>
            session.Window.GetVisualDescendants().OfType<IconButton>().FirstOrDefault(b => b.IsVisible && b.Text == text);

        private static async Task Choose(TestSession session, string text)
        {
            await TestSession.WaitUntil(() => DialogButton(session, text) != null, Timeout, $"the '{text}' button of the conflict dialog");
            DialogButton(session, text)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        [AvaloniaFact]
        public async Task BothComputersPlayed_KeepingThisComputersSave_UploadsItAndStartsTheGame()
        {
            if (!OperatingSystem.IsLinux())
                return;
            var (session, game, started) = await InstalledGameWithLocalSave(401, "Saved Game A", "this computer: level 9");
            ServerSaveFromOtherComputer(session, game.ID, "Saved Game A", "other computer: level 5", DateTime.UtcNow.AddHours(-1));

            Task play = InstallUserControl.PlayGame(game.ID);
            await Choose(session, "Keep this computer's save");
            await play;

            await TestSession.WaitUntil(() => File.Exists(started), Timeout, "the game to start");
            Assert.Equal("this computer: level 9", File.ReadAllText(FakeLudusavi.SaveFile("Saved Game A")));
            // The server now has this computer's save
            Assert.DoesNotContain(OtherComputer, session.Server.Saves[game.ID].FileName);
        }

        [AvaloniaFact]
        public async Task BothComputersPlayed_UsingTheCloudSave_KeepsACopyOfTheLocalOne()
        {
            if (!OperatingSystem.IsLinux())
                return;
            var (session, game, started) = await InstalledGameWithLocalSave(402, "Saved Game B", "this computer: level 9");
            ServerSaveFromOtherComputer(session, game.ID, "Saved Game B", "other computer: level 12", DateTime.UtcNow.AddHours(-1));

            Task play = InstallUserControl.PlayGame(game.ID);
            await Choose(session, "Use the cloud save");
            await play;

            await TestSession.WaitUntil(() => File.Exists(started), Timeout, "the game to start");
            Assert.Equal("other computer: level 12", File.ReadAllText(FakeLudusavi.SaveFile("Saved Game B")));
            string history = Path.Combine(gamevault.Helper.LoginManager.Instance.GetUserProfile().RootDir, "SaveHistory", game.ID.ToString());
            string copy = Directory.GetFiles(history, "save.dat", SearchOption.AllDirectories).Single();
            Assert.Equal("this computer: level 9", File.ReadAllText(copy));
        }

        [AvaloniaFact]
        public async Task OnlyTheOtherComputerPlayed_TheCloudSaveIsRestoredWithoutAsking()
        {
            if (!OperatingSystem.IsLinux())
                return;
            var (session, game, started) = await InstalledGameWithLocalSave(403, "Saved Game C", "old local save");
            // The local save is older than the server's
            File.SetLastWriteTimeUtc(FakeLudusavi.SaveFile("Saved Game C"), DateTime.UtcNow.AddDays(-2));
            ServerSaveFromOtherComputer(session, game.ID, "Saved Game C", "newer save from the other computer", DateTime.UtcNow.AddHours(-1));

            await InstallUserControl.PlayGame(game.ID);

            await TestSession.WaitUntil(() => File.Exists(started), Timeout, "the game to start");
            Assert.Null(DialogButton(session, "Use the cloud save"));
            Assert.Equal("newer save from the other computer", File.ReadAllText(FakeLudusavi.SaveFile("Saved Game C")));
        }
    
        [AvaloniaFact]
        public async Task AnotherComputerUploadedWhileThisOnePlayed_TheUploadAsksFirst()
        {
            if (!OperatingSystem.IsLinux())
                return;
            var (session, game, _) = await InstalledGameWithLocalSave(404, "Saved Game D", "this computer");
            // In sync once: this computer uploaded
            Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.BackupSuccess, await gamevault.Helper.Integrations.SaveGameHelper.Instance.BackupSaveGame(game.ID, force: true));
            // Then the other computer uploads, while this one plays on
            ServerSaveFromOtherComputer(session, game.ID, "Saved Game D", "other computer", DateTime.UtcNow.AddSeconds(30));

            Task<string> upload = gamevault.Helper.Integrations.SaveGameHelper.Instance.BackupSaveGame(game.ID);
            await Choose(session, "Keep the cloud save");

            Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.KeptCloud, await upload);
            Assert.Contains(OtherComputer, session.Server.Saves[game.ID].FileName);
        }

        [AvaloniaFact]
        public async Task TheRestoreFails_TheOldLocalSaveDoesNotSilentlyReplaceTheCloudOne()
        {
            if (!OperatingSystem.IsLinux())
                return;
            var (session, game, _) = await InstalledGameWithLocalSave(405, "Saved Game E", "old local save");
            File.SetLastWriteTimeUtc(FakeLudusavi.SaveFile("Saved Game E"), DateTime.UtcNow.AddDays(-2));
            ServerSaveFromOtherComputer(session, game.ID, "Saved Game E", "newer save from the other computer", DateTime.UtcNow.AddHours(-1));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(FakeLudusavi.SaveFile("Saved Game E"))!, "restore-fails"), "");

            Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.RestoreFailed, await gamevault.Helper.Integrations.SaveGameHelper.Instance.RestoreBackup(game.ID, session.InstallFolder(game)));

            // Not counted as in sync: uploading the old save asks first
            Task<string> upload = gamevault.Helper.Integrations.SaveGameHelper.Instance.BackupSaveGame(game.ID);
            await Choose(session, "Keep the cloud save");
            Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.KeptCloud, await upload);
            Assert.Contains(OtherComputer, session.Server.Saves[game.ID].FileName);
        }

        [AvaloniaFact]
        public async Task OpeningAGamePage_DuringABackup_DoesNotChangeTheConfigurationOfTheBackup()
        {
            if (!OperatingSystem.IsLinux())
                return;
            var (session, game, _) = await InstalledGameWithLocalSave(406, "Saved Game F", "this computer");
            string folder = Path.GetDirectoryName(FakeLudusavi.SaveFile("Saved Game F"))!;
            File.WriteAllText(Path.Combine(folder, "backup-slow"), "");

            Task<string> upload = gamevault.Helper.Integrations.SaveGameHelper.Instance.BackupSaveGame(game.ID, force: true);
            await Task.Delay(300);
            // A game page opens: it searches the game in Ludusavi, with the configuration of no installation
            await gamevault.Helper.Integrations.SaveGameHelper.Instance.SearchForLudusaviGameTitle("Other Game");

            Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.BackupSuccess, await upload);
            Assert.Contains(Path.GetFileName(session.InstallFolder(game).TrimEnd('/')), File.ReadAllText(Path.Combine(folder, "used-config.yaml")));
        }

        [AvaloniaFact]
        public async Task WithALinkedRommAccount_TheSavesOfAGameRommHasGoToRommOnly_WithItsHistory()
        {
            if (!OperatingSystem.IsLinux())
                return;
            var (session, game, _) = await InstalledGameWithLocalSave(407, "Saved Game G", "this computer: level 3");
            var (_, notInRomm, _) = await InstalledGameWithLocalSave(408, "Saved Game H", "this computer: level 1");
            // RomM reads the library's folder: the game is its Windows game of the same file (a NES game of that name is another)
            session.Server.RommRoms.Enqueue((7001, "nes", "Saved Game G (L_P).tar.gz"));
            session.Server.RommRoms.Enqueue((7002, "win", "Saved Game G (L_P).tar.gz"));
            session.Server.RommLinkable = true;
            gamevault.Helper.SanctuaryService.Current = session.Server.Url;
            gamevault.Helper.RommAccount.ForgetUrl();
            var saves = gamevault.Helper.Integrations.SaveGameHelper.Instance;
            try
            {
                Assert.Null(await gamevault.Helper.RommAccount.LinkAsync((_, _) => { }, CancellationToken.None));

                // Sent to RomM only
                Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.BackupSuccess, await saves.BackupSaveGame(game.ID, force: true));
                var sent = session.Server.RommSaves.Single();
                Assert.Equal((7002, "sanctuaryvault", "ludusavi"), (sent.RomId, sent.Slot!, sent.Emulator!));
                Assert.False(session.Server.Saves.ContainsKey(game.ID));
                // This PC's own save: nothing to take before the next game
                Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.UpToDate, await saves.RestoreBackup(game.ID, session.InstallFolder(game)));

                // Another PC sent a newer one; this one did not play since: it is taken from RomM
                session.Server.RommSaves.Enqueue(new FakeGameVaultServer.RommSave(900, 7002, "Saved Game G (L_P).tar.zip", "sanctuaryvault", "ludusavi", DateTime.UtcNow.AddMinutes(5),
                    FakeLudusavi.ServerSaveArchive("Saved Game G", "other computer: level 4"), "pc-2"));
                File.SetLastWriteTimeUtc(FakeLudusavi.SaveFile("Saved Game G"), DateTime.UtcNow.AddDays(-1));
                Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.RestoreSuccess, await saves.RestoreBackup(game.ID, session.InstallFolder(game)));
                Assert.Equal("other computer: level 4", File.ReadAllText(FakeLudusavi.SaveFile("Saved Game G")));

                // The history is RomM's: both versions, the older one restored and sent again as the newest
                var history = new SaveHistoryUserControl(game, session.InstallFolder(game));
                await history.LoadAsync();
                Assert.Equal(2, history.Rows.Count);
                Assert.Contains("RomM", history.FindControl<TextBlock>("uiGame")!.Text);
                Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.RestoreSuccess, await saves.RestoreVersion(game.ID, session.InstallFolder(game), history.Rows[1].Version.Name));
                Assert.Equal("this computer: level 3", File.ReadAllText(FakeLudusavi.SaveFile("Saved Game G")));
                Assert.Equal(3, session.Server.RommSaves.Count);

                // A game RomM has not: the GameVault server only
                Assert.Equal(gamevault.Helper.Integrations.CloudSaveStatus.BackupSuccess, await saves.BackupSaveGame(notInRomm.ID, force: true));
                Assert.True(session.Server.Saves.ContainsKey(notInRomm.ID));
                Assert.Equal(3, session.Server.RommSaves.Count);
            }
            finally
            {
                gamevault.Helper.RommAccount.Unlink();
                gamevault.Helper.RommAccount.ForgetUrl();
                gamevault.Helper.SanctuaryService.Current = gamevault.Helper.SanctuaryService.Url;
                session.Server.RommLinkable = false;
                session.Server.RommSaves.Clear();
                session.Server.RommRoms.Clear();
                session.Server.RommSyncs.Clear();
            }
        }
    }
}
