using Avalonia;
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
            InitSidebar();
            InitPopupLayer();
            InitLivingRoom();
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

            await RefreshNewsBadge();
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
                ToastMessageHelper.CreateToastMessage(Loc.T("Information"), Loc.T("SanctuaryVault is still running in the background"));
            }
        }

        private void UserAvatar_Clicked(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.Community.ShowUser(LoginManager.Instance.GetCurrentUser());
        }

        /// <summary>
        /// The badge on "News": the games added or updated since the news were last opened, and a new message of the
        /// administrator.
        /// </summary>
        private async Task RefreshNewsBadge()
        {
            try
            {
                ServerNewsData news = await ServerNewsService.LoadAsync();
                int count = news.Unread;
                if (news.Announcement != null)
                {
                    string config = LoginManager.Instance.GetUserProfile().UserConfigFile;
                    string hash = await CacheHelper.CreateHashAsync(news.Announcement);
                    if (Preferences.Get(AppConfigKey.NewsHash, config) != hash)
                    {
                        Preferences.Set(AppConfigKey.UnreadNews, "1", config);
                        Preferences.Set(AppConfigKey.NewsHash, hash, config);
                    }
                    if (Preferences.Get(AppConfigKey.UnreadNews, config) == "1")
                        count++;
                }
                MainWindowViewModel.Instance.NewsBadge = count == 0 ? "" : count > 99 ? "99+" : count.ToString();
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private void InitNewsTimer()
        {
            // Games are added at any time: look again every quarter of an hour
            DispatcherTimer newsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            newsTimer.Tick += async (s, e) => await RefreshNewsBadge();
            newsTimer.Start();
        }

        #region Living room mode
        private readonly Gamepads gamepads = new();
        private WindowState stateBeforeLivingRoom = WindowState.Maximized;
        private void InitLivingRoom()
        {
            gamepads.Action += action =>
            {
                // Only while SanctuaryVault is in front: a game being played must not move the library
                if (!IsActive)
                    return;
                if (uiLivingRoom.IsVisible)
                    uiLivingRoom.Handle(action);
                else if (action == GameVault.Core.Input.PadAction.Menu && MainWindowViewModel.Instance.Popup == null)
                    _ = OpenLivingRoom();
            };
            gamepads.ConnectedChanged += connected => uiLivingRoom.GamepadConnected = connected;
            gamepads.Start();
            uiLivingRoom.CloseRequested += (_, _) => CloseLivingRoom();
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.F11)
                    return;
                e.Handled = true;
                if (uiLivingRoom.IsVisible) CloseLivingRoom(); else _ = OpenLivingRoom();
            }, RoutingStrategies.Tunnel);
            Closing += (_, _) => gamepads.Dispose();
        }
        public async Task OpenLivingRoom()
        {
            if (uiLivingRoom.IsVisible)
                return;
            uiQuickSearch.Close();
            stateBeforeLivingRoom = WindowState == WindowState.FullScreen ? WindowState.Maximized : WindowState;
            WindowState = WindowState.FullScreen;
            uiLivingRoom.IsVisible = true;
            await uiLivingRoom.OpenAsync();
        }
        public void CloseLivingRoom()
        {
            if (!uiLivingRoom.IsVisible)
                return;
            uiLivingRoom.IsVisible = false;
            uiLivingRoom.Closed();
            WindowState = stateBeforeLivingRoom;
            MainWindowViewModel.Instance.ActiveControl?.Focus();
        }
        private void LivingRoom_Click(object? sender, RoutedEventArgs e) => _ = OpenLivingRoom();
        #endregion

        #region Popups
        private void InitPopupLayer()
        {
            MainWindowViewModel.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.Popup))
                    LayoutPopup();
            };
            SizeChanged += (_, _) => LayoutPopup();
        }
        /// <summary>
        /// Large panels take the room of the window (within limits), dialogs keep their own size, the full screen
        /// player fills the window. Sizes made for the old scaled interface (fixed size, wide margins) are dropped.
        /// </summary>
        private void LayoutPopup()
        {
            Control? popup = MainWindowViewModel.Instance.Popup;
            if (popup == null)
                return;
            double width = Math.Max(0, Bounds.Width), height = Math.Max(0, Bounds.Height);
            popup.Margin = new Thickness(0);
            if (popup is MediaSlider)
            {
                uiPopupFrame.Background = null;
                uiPopupFrame.BorderThickness = new Thickness(0);
                uiPopupFit.Margin = new Thickness(0);
                uiPopupFrame.CornerRadius = new CornerRadius(0);
                uiPopupFrame.Width = width;
                uiPopupFrame.Height = height;
                popup.Width = popup.Height = double.NaN;
                return;
            }
            uiPopupFit.Margin = new Thickness(28);
            uiPopupFrame.CornerRadius = new CornerRadius(18);
            if (popup is TrailerPopup)
            {
                // 16:9 video under its title bar, as large as the window allows (1440 px wide at most)
                const double titleBar = 52;
                double videoWidth = Math.Clamp(width - 80, 480, 1440);
                if (videoWidth * 9 / 16 + titleBar > height - 80)
                    videoWidth = Math.Max(480, (height - 80 - titleBar) * 16 / 9);
                popup.Width = popup.Height = double.NaN;
                uiPopupFrame.Background = null;
                uiPopupFrame.BorderThickness = new Thickness(0);
                uiPopupFrame.Width = videoWidth;
                uiPopupFrame.Height = videoWidth * 9 / 16 + titleBar;
                return;
            }
            if (popup is GameSettingsUserControl or UserSettingsUserControl or UserControls.SettingsComponents.PublishGameUserControl)
            {
                popup.Width = popup.Height = double.NaN;
                uiPopupFrame.Background = (Avalonia.Media.IBrush?)this.FindResource("Brush.Background");
                uiPopupFrame.BorderBrush = (Avalonia.Media.IBrush?)this.FindResource("Brush.Line");
                uiPopupFrame.BorderThickness = new Thickness(1);
                uiPopupFrame.Width = Math.Clamp(width - 56, 900, 1240);
                uiPopupFrame.Height = Math.Clamp(height - 56, 620, 860);
            }
            else
            {
                // Dialogs draw their own card
                uiPopupFrame.Background = null;
                uiPopupFrame.BorderThickness = new Thickness(0);
                uiPopupFrame.Width = uiPopupFrame.Height = double.NaN;
            }
        }
        #endregion

        #region Sidebar
        private bool sidebarFoldedByUser;
        private void InitSidebar()
        {
            try { sidebarFoldedByUser = Preferences.Get(AppConfigKey.SidebarFolded, ProfileManager.ProfileConfigFile) == "1"; }
            catch (Exception ex) { Log.Ignored(ex); }
            // Small windows keep the room for the games
            SizeChanged += (_, e) => ApplySidebarState(e.NewSize.Width);
            // Ctrl+K opens the quick search from any page
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control) && MainWindowViewModel.Instance.Popup == null)
                {
                    if (uiQuickSearch.IsVisible) uiQuickSearch.Close(); else uiQuickSearch.Open();
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
            ApplySidebarState(Bounds.Width);
            Opened += (_, _) => uiServerName.Text = ServerName();
            MainWindowViewModel.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.UserAvatar))
                    uiServerName.Text = ServerName();
            };
        }
        private void QuickSearch_Click(object? sender, RoutedEventArgs e) => uiQuickSearch.Open();

        private void ApplySidebarState(double width)
        {
            bool folded = sidebarFoldedByUser || (width > 0 && width < 1100);
            uiSidebar.Classes.Set("folded", folded);
            uiUnfold.IsVisible = folded;
        }
        private void FoldSidebar_Click(object? sender, RoutedEventArgs e)
        {
            sidebarFoldedByUser = !uiSidebar.Classes.Contains("folded");
            try { Preferences.Set(AppConfigKey.SidebarFolded, sidebarFoldedByUser ? "1" : "0", ProfileManager.ProfileConfigFile); }
            catch (Exception ex) { Log.Ignored(ex); }
            // Unfolding a small window shows the labels until the window is resized
            uiSidebar.Classes.Set("folded", sidebarFoldedByUser);
            uiUnfold.IsVisible = sidebarFoldedByUser;
        }
        /// <summary>"game.example.com": the server the user is signed in to.</summary>
        private static string ServerName()
        {
            try { return new Uri(SettingsViewModel.Instance.ServerUrl).Host; }
            catch { return ""; }
        }
        private void Link_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? url = (string?)((Control)sender!).Tag;
                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    PlatformInfo.OpenUrl(url!);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        #endregion

        private void News_Click(object? sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.OpenPopup(new NewsPopup());
            try
            {
                MainWindowViewModel.Instance.NewsBadge = "";
                Preferences.Set(AppConfigKey.UnreadNews, "0", LoginManager.Instance.GetUserProfile().UserConfigFile);
            }
            catch (Exception ignored)
            { Log.Ignored(ignored); }
        }

        private void ProblemReport_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            MainWindowViewModel.Instance.OpenPopup(new UserControls.SettingsComponents.ProblemReportUserControl());
        }
        private void Toast_PointerEntered(object? sender, PointerEventArgs e) => MainWindowViewModel.Instance.HoldAppBar(true);
        private void Toast_PointerExited(object? sender, PointerEventArgs e) => MainWindowViewModel.Instance.HoldAppBar(false);
        private void CloseToast_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.IsAppBarOpen = false;

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
