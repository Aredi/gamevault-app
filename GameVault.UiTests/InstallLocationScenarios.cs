using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>Each install asks where the game goes: a game folder, or another folder that becomes one.</summary>
    public class InstallLocationScenarios
    {
        private static async Task<InstallLocationUserControl> Asked()
        {
            await TestSession.WaitUntil(() => MainWindowViewModel.Instance.Popup is InstallLocationUserControl, TimeSpan.FromSeconds(10), "the install location question");
            return (InstallLocationUserControl)MainWindowViewModel.Instance.Popup;
        }

        [AvaloniaFact]
        public async Task AnotherFolder_BecomesAGameFolder_AndTheGameIsInstalledThere()
        {
            var session = await TestSession.GetAsync();
            string otherDisk = Path.Combine(IsolatedHome.Root, "other-disk");
            Directory.CreateDirectory(otherDisk);
            Game game = session.Server.AddGame(170, "Elsewhere", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(1000), "Elsewhere (L_P).tar.gz");
            SettingsViewModel.Instance.AskInstallLocation = true;
            try
            {
                Task<string?> start = MainWindowViewModel.Instance.Downloads.TryStartDownload(game);
                var question = await Asked();
                // The game folder of the profile is offered, with its free space
                Assert.Contains(question.Rows, r => r.Path == session.LibraryRoot && r.HasRoom);

                await question.UseNewFolderAsync(otherDisk);

                Assert.Null(await start);
                Assert.Contains(SettingsViewModel.Instance.RootDirectories, r => r.Uri == otherDisk);
                await TestSession.WaitUntil(() => InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == game.ID && g.Value.StartsWith(otherDisk)), TimeSpan.FromSeconds(90), "the game to be installed on the other disk");
                // Still asked next time
                Assert.True(SettingsViewModel.Instance.AskInstallLocation);
            }
            finally
            {
                SettingsViewModel.Instance.AskInstallLocation = false;
                var added = SettingsViewModel.Instance.RootDirectories.FirstOrDefault(r => r.Uri == otherDisk);
                if (added != null)
                    SettingsViewModel.Instance.RootDirectories.Remove(added);
                Preferences.Set(AppConfigKey.RootDirectories, session.LibraryRoot, gamevault.Helper.LoginManager.Instance.GetUserProfile().UserConfigFile);
                Preferences.Set(AppConfigKey.LastSelectedRootDirectory, session.LibraryRoot, gamevault.Helper.LoginManager.Instance.GetUserProfile().UserConfigFile);
            }
        }

        [AvaloniaFact]
        public async Task DoNotAskAgain_InstallsInTheChosenFolder_WithoutAsking()
        {
            var session = await TestSession.GetAsync();
            Game first = session.Server.AddGame(171, "Asked Once", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(1000), "Asked Once (L_P).tar.gz");
            SettingsViewModel.Instance.AskInstallLocation = true;
            try
            {
                Task<string?> start = MainWindowViewModel.Instance.Downloads.TryStartDownload(first);
                var question = await Asked();
                question.FindControl<Avalonia.Controls.CheckBox>("uiDontAsk")!.IsChecked = true;
                Button? Install() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(question).OfType<Button>()
                    .FirstOrDefault(b => b.DataContext is InstallLocationRow row && row.Path == session.LibraryRoot);
                await TestSession.WaitUntil(() => Install() != null, TimeSpan.FromSeconds(10), "the Install here button");
                Install()!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

                Assert.Null(await start);
                Assert.False(SettingsViewModel.Instance.AskInstallLocation);
                Game second = session.Server.AddGame(172, "Not Asked", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(1000), "Not Asked (L_P).tar.gz");
                Assert.Null(await MainWindowViewModel.Instance.Downloads.TryStartDownload(second));
            }
            finally
            {
                SettingsViewModel.Instance.AskInstallLocation = false;
            }
        }
    }
}
