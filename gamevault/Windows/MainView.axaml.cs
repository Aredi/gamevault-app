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
using System.Linq;
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

        /// <summary>The view on the screen (the PC's window or Android's), null before signing in.</summary>
        internal static MainView? Current { get; private set; }

        public MainView()
        {
            InitializeComponent();
            Current = this;
            this.DataContext = MainWindowViewModel.Instance;
            uiLibraryHost.Content = MainWindowViewModel.Instance.Library;
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
                keysOf?.AddHandler(KeyDownEvent, MobileEscape_KeyDown, RoutingStrategies.Tunnel);
                if (keysOf != null)
                    keysOf.BackRequested += Back_Requested;
            };
            DetachedFromVisualTree += (_, _) =>
            {
                keysOf?.RemoveHandler(KeyDownEvent, ClosePopup_KeyDown);
                keysOf?.RemoveHandler(KeyDownEvent, QuickSearch_KeyDown);
                keysOf?.RemoveHandler(KeyDownEvent, MobileEscape_KeyDown);
                if (keysOf != null)
                    keysOf.BackRequested -= Back_Requested;
                keysOf = null;
            };
            InitCouch();
            InitMobile();
            InitBootTasks();
        }

        private WindowState beforeCouch;
        private bool opened;

        /// <summary>The PC's window around the view; none on Android, whose app has the whole screen already.</summary>
        private Window? Window => TopLevel.GetTopLevel(this) as Window;

        /// <summary>The living-room mode: F11 or its button; its keys are the gamepad's while it is shown.</summary>
        private void InitCouch()
        {
            uiCouch.LeaveRequested += LeaveCouch;
            uiCouch.TextKeyboard = uiCouchKeys;
            SizeChanged += (_, _) =>
            {
                if (uiCouch.IsVisible)
                    ZoomForCouch();
            };
            AttachedToVisualTree += (_, _) =>
            {
                keysOf?.AddHandler(KeyDownEvent, Couch_KeyDown, RoutingStrategies.Tunnel);
                if (Window is Window window)
                    window.PropertyChanged += Window_PropertyChanged;
                if (opened)
                    return;
                opened = true;
                // Back in the mode after a change of profile made in it, whatever the new profile chose
                Dispatcher.UIThread.Post(() =>
                {
                    if (UserControls.CouchUserControl.TakeComeBack() || UserControls.CouchUserControl.StartsHere)
                        EnterCouch(welcome: true);
                    UserControls.CouchUserControl.CloseSwitchCurtain();
                });
            };
            DetachedFromVisualTree += (_, e) =>
            {
                (e.Root as TopLevel)?.RemoveHandler(KeyDownEvent, Couch_KeyDown);
                if (e.Root is Window window)
                    window.PropertyChanged -= Window_PropertyChanged;
            };
        }

        private void Couch_KeyDown(object? sender, KeyEventArgs e)
        {
            if (CouchFocus.Activating)
                return;
            if (e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.None)
            {
                if (uiCouch.IsVisible) LeaveCouch(); else EnterCouch();
                e.Handled = true;
            }
            // Nothing under it gets the keyboard; a popup over it and a question with a text to type keep their own keys
            else if (uiCouch.IsVisible && MainWindowViewModel.Instance.Popup == null && (DialogLayer.Children.Count == 0 || DialogService.Current is { Input: null }) && e.KeyModifiers == KeyModifiers.None)
            {
                uiCouch.HandleKey(e.Key);
                e.Handled = true;
            }
        }

        // Hidden in the tray or minimized, the mode is not on the screen: the system's notifications are shown again
        private void Window_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty)
                UserControls.CouchUserControl.Shown = uiCouch.IsVisible && sender is Window { IsVisible: true } window && window.WindowState != WindowState.Minimized;
        }

        internal bool IsCouchShown => uiCouch.IsVisible;

        internal void EnterCouch(bool welcome = false)
        {
            if (uiCouch.IsVisible)
                return;
            MainWindowViewModel.Instance.ClosePopup();
            uiQuickSearch.Close();
            uiDesktop.IsVisible = false;
            if (Window is Window window)
            {
                beforeCouch = window.WindowState == WindowState.FullScreen ? WindowState.Normal : window.WindowState;
                window.WindowState = WindowState.FullScreen;
            }
            MobilePlatform.Current?.SetCouch(true);
            ZoomForCouch();
            _ = uiCouch.OpenAsync(welcome);
        }

        /// <summary>The client's windows and questions are made for a desk: larger on a TV, as much as the screen is taller than them.</summary>
        private void ZoomForCouch()
        {
            double zoom = uiCouch.IsVisible || !uiDesktop.IsVisible ? Math.Clamp(Bounds.Height / 820, 1, 1.7) : 1;
            Avalonia.Media.ScaleTransform? scale = zoom > 1.01 ? new Avalonia.Media.ScaleTransform(zoom, zoom) : null;
            uiPopupZoom.LayoutTransform = scale;
            DialogLayer.RenderTransform = scale;
            uiCouchKeys.RenderTransform = scale;
        }

        internal void LeaveCouch()
        {
            if (!uiCouch.IsVisible)
                return;
            uiCouch.Close();
            uiDesktop.IsVisible = true;
            if (Window is Window window)
                window.WindowState = beforeCouch;
            MobilePlatform.Current?.SetCouch(false);
            ZoomForCouch();
        }

        private void Couch_Click(object? sender, RoutedEventArgs e) => EnterCouch();

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
            // A game over the pages has its own menu
            if (App.Instance.OverScreen != null)
                return;
            e.Handled = GoBack();
        }

        private DateTime lastBackAt;

        /// <summary>
        /// One step back (Android's back button and gesture, a mouse's back button): true when it was taken here, false when
        /// it is left to the system (home on a phone: the app to the background). One press comes twice on Android, as Escape
        /// then as Back (or as Back alone): the second within a moment of the first is the same press.
        /// </summary>
        private bool GoBack()
        {
            DateTime last = new[] { lastBackAt, DialogService.LastClosedAt, MainWindowViewModel.Instance.PopupClosedAt }.Max();
            if (DateTime.UtcNow - last < TimeSpan.FromMilliseconds(350))
                return true;
            bool taken = Step();
            if (taken)
                lastBackAt = DateTime.UtcNow;
            return taken;
        }

        private bool Step()
        {
            // A question: Back answers it as "No" (the app is not left under it)
            if (DialogLayer.Children.Count > 0)
            {
                DialogService.Current?.Cancel();
                return true;
            }
            // The living-room mode: B (its own way back, then its menu to leave it)
            if (uiCouch.IsVisible)
            {
                if (MainWindowViewModel.Instance.Popup != null)
                    MainWindowViewModel.Instance.ClosePopup();
                else
                    uiCouch.Handle(GameVault.Core.Couch.CouchCommand.Back);
                return true;
            }
            if (uiQuickSearch.IsVisible)
                uiQuickSearch.Close();
            else if (MainWindowViewModel.Instance.Popup != null)
                MainWindowViewModel.Instance.ClosePopup();
            else if (uiChatHost.IsVisible && uiChat.GoBack())
            {
            }
            else if (MainWindowViewModel.Instance.ActiveControl is IGoesBack page && page.GoBack())
            {
            }
            else if (IsMobile && MainWindowViewModel.Instance.ActiveControl == MainWindowViewModel.Instance.MobileReturn)
            {
                // A tab's own page: home first, and from home the app goes to the background (Android's)
                return MobileBack();
            }
            // A phone: any other page (the settings, the downloads, a game's...) back to the tab it was opened from
            else if (IsMobile)
                MainWindowViewModel.Instance.UndoActiveControl();
            else if (MainWindowViewModel.Instance.ActiveControlIndex < 0)
                MainWindowViewModel.Instance.UndoActiveControl();
            else
                return false;
            return true;
        }

        /// <summary>
        /// A phone: Android's back button comes first as Escape to the control with the focus (a text box kept it, and Back
        /// was never seen): taken before it, as Back.
        /// </summary>
        private void MobileEscape_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!IsMobile || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || App.Instance.OverScreen != null || uiCouch.IsVisible)
                return;
            e.Handled = GoBack();
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
            // The screenshots and videos taken in a game, kept with it (not in the tests: they would watch the real folders)
            if (PipeServiceHandler.Instance != null)
            {
                GameCaptures.Start();
                if (OperatingSystem.IsWindows())
                {
                    void Attach(object? sender, VisualTreeAttachmentEventArgs e)
                    {
                        AttachedToVisualTree -= Attach;
                        if (e.Root is Window window)
                            ScreenCapture.Attach(window);
                    }
                    AttachedToVisualTree += Attach;
                }
            }
            // Games asked from the web page ("Install on my PC")
            if (PipeServiceHandler.Instance != null && PlatformInfo.PlaysPcGames)
                RemoteInstalls.Start();
            // Not in the tests (no desktop session there); Android has no pipe, but a real session
            bool live = PipeServiceHandler.Instance != null || PlatformInfo.IsAndroid;
            // Requests of the player that were added or refused
            if (live)
                GameRequestsService.Start();
            // Who is online and what they play
            if (live)
                _ = ActivityService.Instance.StartAsync();
            // The chat with the other players (when the service keeps one)
            if (live)
                ChatService.Instance.Start();
            // The notices go left of the chat: over it they would hide what is being written
            uiChatHost.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                    uiToast.Margin = new Thickness(0, 0, uiChatHost.IsVisible && !chatOverPage ? 24 + uiChat.Width : 24, 24);
            };
            // A narrow screen (a phone held upright): the chat over the page, on its whole width, else beside it
            SizeChanged += (_, e) =>
            {
                chatOverPage = e.NewSize.Width > 0 && e.NewSize.Width < 760;
                Grid.SetColumn(uiChatHost, chatOverPage ? 1 : 2);
                Grid.SetColumnSpan(uiChatHost, chatOverPage ? 2 : 1);
                uiChat.Width = chatOverPage ? double.NaN : 320;
                uiChatHost.ZIndex = chatOverPage ? 1 : 0;
            };
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
                case MainControl.Challenges:
                    MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Challenges;
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
            // The phone opens on its home
            if (IsMobile)
            {
                PlaceMobileNav(Bounds.Width);
                ShowMobileTab(MobileTab.Home);
            }
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
            // The living-room mode's game themes, searched for in the background once the window is settled
            if (LoginState.Success == state && GameThemes.PreloadAtStart)
                _ = uiCouch.PreloadThemesAsync(TimeSpan.FromSeconds(30));
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
        private bool popupShown;
        private void InitPopupLayer()
        {
            MainWindowViewModel.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.Popup))
                {
                    LayoutPopup();
                    // Opened (not one replacing another): the page darkens and the panel grows to its size
                    bool open = MainWindowViewModel.Instance.Popup != null;
                    if (open && !popupShown)
                    {
                        Motion.FadeIn(uiPopupLayer);
                        Motion.Settle(uiPopupFrame, fromScale: 0.96, durationMs: 260);
                    }
                    popupShown = open;
                }
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
            uiPopupScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            popup.MaxWidth = double.PositiveInfinity;
            if (IsMobile && popup is not MediaSlider and not TrailerPopup)
            {
                MobilePopup(popup, width, height);
                return;
            }
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
        private bool chatOverPage;
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
                else if (e.PropertyName is nameof(MainWindowViewModel.ActiveControlIndex) or nameof(MainWindowViewModel.ActiveControl))
                    ShowCurrentSection();
            };
        }

        /// <summary>
        /// A game's page (or another page opened from a section) is in no item of the menu: the section it was opened
        /// from stays lit, so the player knows where they are. Clicking it goes back there, as it is not selected.
        /// </summary>
        private void ShowCurrentSection()
        {
            var vm = MainWindowViewModel.Instance;
            // A section's own page shown without the menu (the downloads from the tray): its own item, not the last one chosen
            int current = vm.ActiveControlIndex >= 0 || vm.ActiveControl == null ? -1 : (int)(vm.SectionOf(vm.ActiveControl) ?? vm.LastMainControl);
            for (int i = 0; i < uiNavigation.ItemCount; i++)
                if (uiNavigation.ContainerFromIndex(i) is ListBoxItem item)
                    item.Classes.Set("current", i == current);
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

        private void Chat_Click(object? sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.IsChatOpen = !MainWindowViewModel.Instance.IsChatOpen;
            if (MainWindowViewModel.Instance.IsChatOpen)
                _ = ChatService.Instance.RefreshAsync();
        }

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
            if (Current == this)
                Current = null;
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
            ChatService.Instance.Stop();
            MainWindowViewModel.Instance.UserAvatar = null;
        }
    }
}
