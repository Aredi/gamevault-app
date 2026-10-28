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
    /// <summary>The pages and the sidebar: in MainWindow on a PC, the whole screen of the Android app.</summary>
    public partial class MainView : UserControl, IDisposable
    {
        private GameTimeTracker GameTimeTracker;
        private bool loaded;

        public MainView()
        {
            InitializeComponent();
            this.DataContext = MainWindowViewModel.Instance;
            InitSidebar();
            // The Mac's and Android's versions: the PC games are Windows' (the library, the LAN rooms)
            uiNavLibrary.IsVisible = uiNavRooms.IsVisible = PlatformInfo.PlaysPcGames;
            InitPopupLayer();
            AttachedToVisualTree += MainView_Loaded;
            // The keys of the whole window (or screen), also when nothing has the focus
            AttachedToVisualTree += (_, _) =>
            {
                keysOf = TopLevel.GetTopLevel(this);
                keysOf?.AddHandler(KeyDownEvent, ClosePopup_KeyDown);
                keysOf?.AddHandler(KeyDownEvent, QuickSearch_KeyDown, RoutingStrategies.Tunnel);
                if (keysOf != null)
                    keysOf.BackRequested += Back_Requested;
            };
            DetachedFromVisualTree += (_, _) =>
            {
                keysOf?.RemoveHandler(KeyDownEvent, ClosePopup_KeyDown);
                keysOf?.RemoveHandler(KeyDownEvent, QuickSearch_KeyDown);
                if (keysOf != null)
                    keysOf.BackRequested -= Back_Requested;
                keysOf = null;
            };
            InitBootTasks();
        }

        private TopLevel? keysOf;

        /// <summary>Popups close themselves on Escape only while they have the keyboard focus (lost e.g. after a dialog)</summary>
        private void ClosePopup_KeyDown(object? sender, KeyEventArgs e)
        {
            var popup = MainWindowViewModel.Instance.Popup;
            if (e.Handled || e.Key != Key.Escape || popup == null || popup is UserControls.MediaSlider || DialogLayer.Children.Count > 0)
                return;
            MainWindowViewModel.Instance.ClosePopup();
            e.Handled = true;
        }

        /// <summary>
        /// Android's back button (and a mouse's back button): the popup closes, a game's page goes back to its list.
        /// On a main page it is left to Android, which puts the app in the background.
        /// </summary>
        private void Back_Requested(object? sender, RoutedEventArgs e)
        {
            if (DialogLayer.Children.Count > 0)
                return;
            if (uiQuickSearch.IsVisible)
                uiQuickSearch.Close();
            else if (MainWindowViewModel.Instance.Popup != null)
                MainWindowViewModel.Instance.ClosePopup();
            else if (MainWindowViewModel.Instance.ActiveControlIndex < 0)
                MainWindowViewModel.Instance.UndoActiveControl();
            else
                return;
            e.Handled = true;
        }

        /// <summary>Ctrl+K (⌘K on a Mac) opens the quick search from any page</summary>
        private void QuickSearch_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && e.KeyModifiers.HasFlag(PlatformInfo.CommandKey) && MainWindowViewModel.Instance.Popup == null)
            {
                if (uiQuickSearch.IsVisible) uiQuickSearch.Close(); else uiQuickSearch.Open();
                e.Handled = true;
            }
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
            RetroNewGamesNotifier.Start();
            // Games asked from the web page ("Install on my PC")
            if (PipeServiceHandler.Instance != null && PlatformInfo.PlaysPcGames)
                RemoteInstalls.Start();
            // Requests of the player that were added or refused
            if (PipeServiceHandler.Instance != null)
                GameRequestsService.Start();
            // Who is online and what they play (not in the tests: no desktop session there)
            if (PipeServiceHandler.Instance != null)
                _ = ActivityService.Instance.StartAsync();
            // Savegames of games closed offline in a previous session
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                try { await SaveGameHelper.Instance.UploadPendingSaveGamesAsync(); }
                catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
            });
            backOnline = async (_, _) =>
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
            LoginManager.Instance.BackOnline += backOnline;
        }
        /// <summary>Removed when the view is disposed: the next sign-in makes a new view, with its own.</summary>
        private EventHandler? backOnline;

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
                case MainControl.Retro:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Retro;
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
                case MainControl.Activity:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Activity;
                    break;
                case MainControl.Rooms:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Rooms;
                    break;
                case MainControl.AdminConsole:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.AdminConsole;
                    break;
            }
            MainWindowViewModel.Instance.LastMainControl = activeControlIndex;
        }

        private async void MainView_Loaded(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (loaded)
                return;
            loaded = true;
            MainWindowViewModel.Instance.SetActiveControl(PlatformInfo.PlaysPcGames ? MainControl.Library : MainControl.Retro);
            LoginState state = LoginManager.Instance.GetState();
            if (LoginState.Success == state)
            {
                if (PlatformInfo.PlaysPcGames && Preferences.Get(AppConfigKey.LibStartup, LoginManager.Instance.GetUserProfile().UserConfigFile) == "1")
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
            RetroDownloads.Restore();
            LoginManager.Instance.InitOnlineTimer();
            MainWindowViewModel.Instance.UserAvatar = LoginManager.Instance.GetCurrentUser();

            await RefreshNewsBadge();
            InitNewsTimer();
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

        private DispatcherTimer? newsTimer;
        private void InitNewsTimer()
        {
            // Games are added at any time: look again every quarter of an hour
            newsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            newsTimer.Tick += async (s, e) => await RefreshNewsBadge();
            newsTimer.Start();
        }


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
            if (popup is GameSettingsUserControl or UserSettingsUserControl or UserControls.SettingsComponents.PublishGameUserControl or AutoMatchUserControl or StorageUserControl or RetroControlsUserControl)
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
            ApplySidebarState(Bounds.Width);
            AttachedToVisualTree += (_, _) => uiServerName.Text = ServerName();
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
            newsTimer?.Stop();
            if (backOnline != null)
                LoginManager.Instance.BackOnline -= backOnline;
            // Paused like when the app is closed: cancelling would delete the downloaded archives and the partial downloads
            MainWindowViewModel.Instance.Downloads.PauseAllDownloads();
            InstallViewModel.Instance.InstalledGames.Clear();
            DownloadsViewModel.Instance.DownloadedGames.Clear();
            DownloadsViewModel.Instance.RetroDownloads.Clear();
            ProcessShepherd.Instance.KillAllChildProcesses();
            App.HideToSystemTray = false;
            App.Instance.ResetToDefaultTheme();
            LoginManager.Instance.StopOnlineTimer();
            LoginManager.Instance.SignOut();
            if (PipeServiceHandler.Instance != null)
                PipeServiceHandler.Instance.IsReadyForCommands = false;
            _ = ActivityService.Instance.StopAsync();
            MainWindowViewModel.Instance.UserAvatar = null;
        }
    }
}
