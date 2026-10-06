using gamevault.Localization;
using gamevault.Helper.Integrations;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Windows
{
    public partial class MainWindow : Window, IDisposable
    {
        private GameTimeTracker GameTimeTracker;
        private bool loaded;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = MainWindowViewModel.Instance;
            uiGithubShortcut.Tag = $"https://github.com/{AppRepository.Owner}/{AppRepository.Name}";
            uiBugReportShortcut.Tag = $"https://github.com/{AppRepository.Owner}/{AppRepository.Name}/issues/new";
            Opened += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            // Popups close themselves on Escape only while they have the keyboard focus (lost e.g. after a dialog)
            KeyDown += (_, e) =>
            {
                var popup = MainWindowViewModel.Instance.Popup;
                if (e.Handled || e.Key != Key.Escape || popup == null || popup is UserControls.MediaSlider || DialogLayer.Children.Count > 0)
                    return;
                MainWindowViewModel.Instance.ClosePopup();
                e.Handled = true;
            };
            InitBootTasks();
        }

        private void InitBootTasks()
        {
            App.HideToSystemTray = true;
            RestoreTheme();
            Task.Run(async () =>
            {
                if (GameTimeTracker == null)
                {
                    GameTimeTracker = new GameTimeTracker();
                    await GameTimeTracker.Start();
                }
            });
            if (PipeServiceHandler.Instance != null)// not started without a desktop session (tests)
                PipeServiceHandler.Instance.IsReadyForCommands = true;
            NewGamesNotifier.Start();
            // Savegames of games closed offline in a previous session
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                try { await SaveGameHelper.Instance.UploadPendingSaveGamesAsync(); }
                catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
            });
            LoginManager.Instance.BackOnline += async (_, _) =>
            {
                // Everything that waited for the server: library, offline play time and savegames, new games
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await MainWindowViewModel.Instance.Library.LoadLibrary();
                        await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
                    }
                    catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                });
                GameTimeTracker?.SyncNow();
                await SaveGameHelper.Instance.UploadPendingSaveGamesAsync();
                await NewGamesNotifier.CheckAsync();
            };
        }

        private void Navigation_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int index = uiNavigation.SelectedIndex;
            if (index < 0)
                return;

            MainControl activeControlIndex = (MainControl)index;
            switch (activeControlIndex)
            {
                case MainControl.Library:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Library;
                    break;
                case MainControl.Settings:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Settings;
                    break;
                case MainControl.Downloads:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Downloads;
                    break;
                case MainControl.Community:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Community;
                    break;
                case MainControl.AdminConsole:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.AdminConsole;
                    break;
            }
            MainWindowViewModel.Instance.LastMainControl = activeControlIndex;
        }

        private async void MainWindow_Loaded(object? sender, EventArgs e)
        {
            if (loaded)
                return;
            loaded = true;
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            LoginState state = LoginManager.Instance.GetState();
            if (LoginState.Success == state)
            {
                if (Preferences.Get(AppConfigKey.LibStartup, LoginManager.Instance.GetUserProfile().UserConfigFile) == "1")
                {
                    await MainWindowViewModel.Instance.Library.LoadLibrary();
                }
            }
            else if (LoginState.Unauthorized == state || LoginState.Forbidden == state)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("You are not logged in");
            }
            else if (LoginState.Error == state)
            {
                MainWindowViewModel.Instance.AppBarText = LoginManager.Instance.GetServerLoginResponseMessage();
                MainWindowViewModel.Instance.Library.ShowLibraryError();
            }
            await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
            await MainWindowViewModel.Instance.Downloads.RestoreDownloadedGames();
            LoginManager.Instance.InitOnlineTimer();
            MainWindowViewModel.Instance.UserAvatar = LoginManager.Instance.GetCurrentUser();

            uiNewsBadge.Badge = await CheckForNews() ? "!" : "";
            InitNewsTimer();
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (!App.HideToSystemTray)
                return;

            e.Cancel = true;
            if (!App.Instance.HasTrayIcon)
            {
                // Without a tray icon (e.g. GNOME without AppIndicator) a hidden window could not be brought back.
                await App.Instance.ExitApp();
                return;
            }
            this.Hide();
            if (Preferences.Get(AppConfigKey.RunningInTrayMessage, LoginManager.Instance.GetUserProfile().UserConfigFile) != "1")
            {
                Preferences.Set(AppConfigKey.RunningInTrayMessage, "1", LoginManager.Instance.GetUserProfile().UserConfigFile);
                ToastMessageHelper.CreateToastMessage(Loc.T("Information"), Loc.T("GameVault is still running in the background"));
            }
        }

        private void UserAvatar_Clicked(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.Community.ShowUser(LoginManager.Instance.GetCurrentUser());
        }

        private async Task<bool> CheckForNews()
        {
            try
            {
                if (Preferences.Get(AppConfigKey.UnreadNews, LoginManager.Instance.GetUserProfile().UserConfigFile) == "1")
                {
                    return true;
                }
                string gameVaultNews = await WebHelper.GetAsync("https://gamevau.lt/news.md");
                string serverNews = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/config/news");

                string hash = await CacheHelper.CreateHashAsync(gameVaultNews + serverNews);
                if (Preferences.Get(AppConfigKey.NewsHash, LoginManager.Instance.GetUserProfile().UserConfigFile) != hash)
                {
                    Preferences.Set(AppConfigKey.UnreadNews, "1", LoginManager.Instance.GetUserProfile().UserConfigFile);
                    Preferences.Set(AppConfigKey.NewsHash, hash, LoginManager.Instance.GetUserProfile().UserConfigFile);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void InitNewsTimer()
        {
            DispatcherTimer newsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(1)
            };
            newsTimer.Tick += async (s, e) => { uiNewsBadge.Badge = await CheckForNews() ? "!" : ""; };
            newsTimer.Start();
        }

        private void News_Click(object? sender, PointerReleasedEventArgs e)
        {
            MainWindowViewModel.Instance.OpenPopup(new NewsPopup());
            try
            {
                uiNewsBadge.Badge = "";
                Preferences.Set(AppConfigKey.UnreadNews, "0", LoginManager.Instance.GetUserProfile().UserConfigFile);
            }
            catch (Exception ignored)
            { Log.Ignored(ignored); }
        }

        private void ProblemReport_Click(object? sender, PointerReleasedEventArgs e)
        {
            e.Handled = true;
            MainWindowViewModel.Instance.OpenPopup(new UserControls.SettingsComponents.ProblemReportUserControl());
        }
        private void Shortlink_Click(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                string? url = (string?)((Control)sender!).Tag;
                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    PlatformInfo.OpenUrl(url!);
                }
                e.Handled = true;
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            ClipboardHelper.SetText(MainWindowViewModel.Instance.AppBarText);
        }

        private void RestoreTheme()
        {
            try
            {
                string currentThemeString = Preferences.Get(AppConfigKey.Theme, LoginManager.Instance.GetUserProfile().UserConfigFile, true);
                if (currentThemeString != string.Empty)
                {
                    ThemeItem currentTheme = JsonSerializer.Deserialize<ThemeItem>(currentThemeString)!;
                    if (ThemeManager.CurrentThemePath != ThemeManager.NormalizePath(currentTheme.Path))
                    {
                        App.Instance.SetTheme(currentTheme.Path);
                    }
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        public void Dispose()
        {
            GameTimeTracker?.Stop();
            MainWindowViewModel.Instance.Downloads.CancelAllDownloads();
            InstallViewModel.Instance.InstalledGames.Clear();
            DownloadsViewModel.Instance.DownloadedGames.Clear();
            ProcessShepherd.Instance.KillAllChildProcesses();
            App.HideToSystemTray = false;
            App.Instance.ResetToDefaultTheme();
            LoginManager.Instance.StopOnlineTimer();
            if (PipeServiceHandler.Instance != null)
                PipeServiceHandler.Instance.IsReadyForCommands = false;
            MainWindowViewModel.Instance.UserAvatar = null;
            this.Close();
        }
    }
}
