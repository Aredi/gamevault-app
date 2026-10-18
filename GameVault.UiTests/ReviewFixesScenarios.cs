using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using Markdown.Avalonia;

namespace GameVault.UiTests
{
    /// <summary>Problems found reading the whole code.</summary>
    public class ReviewFixesScenarios
    {
        private static GameDownloadUserControl Download(int id) => DownloadsViewModel.Instance.DownloadedGames.First(d => d.GetGameId() == id);

        [AvaloniaFact]
        public async Task ReadingTheDownloadsAgain_KeepsTheRunningAndTheQueuedOnes()
        {
            var session = await TestSession.GetAsync();
            Game first = session.Server.AddGame(140, "Restore Running", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(1_500_000), "Restore Running (L_P).tar.gz");
            Game second = session.Server.AddGame(141, "Restore Queued", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(100_000), "Restore Queued (L_P).tar.gz");
            SettingsViewModel.Instance.MaxConcurrentDownloadsIndex = 1;
            session.Server.ChunkDelay = TimeSpan.FromMilliseconds(50);
            try
            {
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(first);
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(second);
                Assert.True(Download(first.ID).IsDownloading());

                // As after "Free up disk space" or a new root directory
                await MainWindowViewModel.Instance.Downloads.RestoreDownloadedGames();

                Assert.True(Download(first.ID).IsDownloading());
                Assert.True(DownloadQueue.IsWaiting(Download(second.ID)));
                session.Server.ChunkDelay = TimeSpan.Zero;
                await TestSession.WaitUntil(() => File.Exists(Path.Combine(session.DownloadFolder(second), "Extract", "start.sh")), TimeSpan.FromSeconds(120), "the queued game to be downloaded");
            }
            finally
            {
                session.Server.ChunkDelay = TimeSpan.Zero;
                SettingsViewModel.Instance.MaxConcurrentDownloadsIndex = 0;
            }
        }

        [AvaloniaFact]
        public void OnlyWebPages_AreOpened_FromLinks()
        {
            Assert.True(PlatformInfo.IsWebAddress("https://store.example.com/game"));
            Assert.True(PlatformInfo.IsWebAddress("http://example.com"));
            // A link of a description or a website field could start a program through the shell
            Assert.False(PlatformInfo.IsWebAddress(@"C:\Windows\System32\calc.exe"));
            Assert.False(PlatformInfo.IsWebAddress("file:///C:/Windows/System32/calc.exe"));
            Assert.False(PlatformInfo.IsWebAddress(@"\\attacker\share\setup.exe"));
            Assert.False(PlatformInfo.IsWebAddress("javascript:alert(1)"));
            Assert.False(PlatformInfo.IsWebAddress(""));
            Assert.False(PlatformInfo.IsWebAddress(null));
        }

        [AvaloniaFact]
        public async Task TheLinks_OfAGamePage_GoThroughTheCheck()
        {
            await TestSession.GetAsync();
            var page = new GameViewUserControl(new Game { ID = 1, Title = "Links" }, false);
            Assert.True(MarkdownLinks.IsSafe(page.FindControl<MarkdownScrollViewer>("uiDescription")!));
            Assert.True(MarkdownLinks.IsSafe(page.FindControl<MarkdownScrollViewer>("uiNotes")!));
        }

        [AvaloniaFact]
        public void AnIsoPath_StaysOneString_ForPowerShell()
        {
            Assert.Equal(@"'D:\Games\(1)Assassin''s Creed\game.iso'", GameDownloadUserControl.PowerShellQuote(@"D:\Games\(1)Assassin's Creed\game.iso"));
            // PowerShell ends a string at a typographic quote too
            Assert.Equal("'x’’; calc; ’’y'", GameDownloadUserControl.PowerShellQuote("x’; calc; ’y"));
        }

        [AvaloniaFact]
        public void ALineBreakInATitle_AddsNoKeyToADesktopFile()
        {
            string entry = DesktopEntry.Create("Game\nExec=rm -rf ~", "gamevault start --gameid=1", "Play\nExec=evil", "icon.png");
            Assert.Single(entry.Split('\n'), line => line.StartsWith("Exec="));
            Assert.Contains("Name=GameExec=rm -rf ~", entry);
        }

        [AvaloniaFact]
        public void ATextMissing_IsTranslatedToNothing_InsteadOfFailing()
        {
            Assert.Equal("", gamevault.Localization.Loc.T(null));
        }

        [AvaloniaFact]
        public async Task TheSettingsOfAProfile_DoNotKeepTheOnesOfThePreviousOne()
        {
            await TestSession.GetAsync();
            string configFile = LoginManager.Instance.GetUserProfile().UserConfigFile;
            string before = Preferences.Get(AppConfigKey.AutoInstallPortable, configFile);
            try
            {
                Preferences.Set(AppConfigKey.AutoInstallPortable, "1", configFile);
                SettingsViewModel.Instance.Init();
                Assert.True(SettingsViewModel.Instance.AutoInstallPortable);
                Preferences.Set(AppConfigKey.AutoInstallPortable, "0", configFile);
                SettingsViewModel.Instance.Init();
                Assert.False(SettingsViewModel.Instance.AutoInstallPortable);
            }
            finally
            {
                Preferences.Set(AppConfigKey.AutoInstallPortable, before, configFile);
                SettingsViewModel.Instance.Init();
            }
        }
    }
}
