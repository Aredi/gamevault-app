using gamevault.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using gamevault.Windows;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault
{
    public partial class App : Application
    {
        public static App Instance => (App)Current!;

        public static bool HideToSystemTray = true;
        /// <summary>Kept for the code paths that distinguish MS Store installs; this build is never one.</summary>
        public static bool IsWindowsPackage = false;

        public static CommandOptions? CommandLineOptions { get; internal set; } = null;

        private TrayIcon? trayIcon;
        private bool handlingFatalError;

        private IClassicDesktopStyleApplicationLifetime? Desktop => ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        /// <summary>Android: one screen, no windows. The login view, then the main view, fill it.</summary>
        private ISingleViewApplicationLifetime? SingleView => ApplicationLifetime as ISingleViewApplicationLifetime;
        private readonly ContentControl singleViewShell = new ContentControl();
        /// <summary>Over the pages: the Android app's own game player.</summary>
        private readonly ContentControl overScreen = new ContentControl { IsVisible = false };

        /// <summary>
        /// The main GameVault window once a profile is logged in, otherwise the login window.
        /// </summary>
        public Window? MainWindow
        {
            get => Desktop?.MainWindow ?? mainWindow;
            set
            {
                // Also kept without a desktop lifetime (headless tests), dialogs are shown on it
                mainWindow = value;
                if (Desktop != null)
                    Desktop.MainWindow = value;
            }
        }
        private Window? mainWindow;

        public Window? ActiveWindow => Desktop?.Windows.FirstOrDefault(w => w.IsActive) ?? Desktop?.Windows.FirstOrDefault(w => w.IsVisible);

        /// <summary>Where dialogs, pickers and the clipboard are: the main window, another window, or the Android screen.</summary>
        public TopLevel? Root => MainWindow as TopLevel ?? ActiveWindow ?? (SingleView != null ? TopLevel.GetTopLevel(singleViewShell) : null);

        /// <summary>The Android app (one screen): what is the PC's window elsewhere is there.</summary>
        public bool IsSingleView => SingleView != null;

        /// <summary>
        /// Android: the room the status and navigation bars (and a camera's notch) take, kept free by the pages; a game
        /// takes the whole screen.
        /// </summary>
        public void SetSafeArea(Thickness padding) => singleViewShell.Margin = padding;

        /// <summary>Android: a full screen view over the pages (a game), or none.</summary>
        public Control? OverScreen
        {
            get => overScreen.Content as Control;
            set
            {
                overScreen.Content = value;
                overScreen.IsVisible = value != null;
            }
        }

        public override void Initialize()
        {
            // Before any window: the XAML texts are translated while they load
            try { Localization.Loc.Initialize(Preferences.Get(AppConfigKey.Language, ProfileManager.ProfileConfigFile)); }
            catch (Exception ex) { Log.Ignored(ex); }
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Log.Initialize(ProfileManager.ErrorLogDir);
            Log.Info($"GameVault {SettingsViewModel.Instance.Version} starting on {Environment.OSVersion}");
#if DEBUG
            UiDump.StartIfRequested();
#endif
            Dispatcher.UIThread.UnhandledException += (s, e) =>
            {
                ProcessShepherd.Instance.KillAllChildProcesses();
#if !DEBUG
                e.Handled = true;
                LogUnhandledException(e.Exception);
#endif
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            ThemeManager.ApplyDefault();

            if (Desktop != null)
            {
                Desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Desktop.Exit += (_, _) => trayIcon?.Dispose();
                Dispatcher.UIThread.Post(async () => await StartupAsync());
            }
            else if (SingleView != null)
            {
                SingleView.MainView = new Panel { Children = { singleViewShell, overScreen } };
                Dispatcher.UIThread.Post(async () => await StartupSingleViewAsync());
            }
            base.OnFrameworkInitializationCompleted();
        }

        private async Task StartupAsync()
        {
            try
            {
                var loginWindow = new LoginWindow();
                MainWindow = loginWindow;
                loginWindow.Show();
                bool loggedIn = await loginWindow.Completion;
                if (!loggedIn)
                {
                    Shutdown();
                    return;
                }
                InitTrayIcon();
            }
            catch (Exception ex)
            {
                LogUnhandledException(ex);
                return;
            }

            if (PipeServiceHandler.Instance != null)
            {
                // Strictly speaking we should hold up all commands until we have a confirmed login & setup is complete, but for now we'll assume that auto-login has worked
                PipeServiceHandler.Instance.IsReadyForCommands = true;
                await PipeServiceHandler.Instance.HandleCommand(CommandLineOptions);
            }
        }

        private async Task StartupSingleViewAsync()
        {
            try
            {
                var login = new LoginView();
                singleViewShell.Content = FitLogin(login);
                await login.Completion;
            }
            catch (Exception ex) { LogUnhandledException(ex); }
        }

        /// <summary>
        /// The pages once signed in: a new main window on a PC (its visibility at the first start is the protocol
        /// handler's), the screen of the Android app.
        /// </summary>
        public void OpenMainView()
        {
            if (SingleView != null)
            {
                singleViewShell.Content = new MainView();
                return;
            }
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            if (PipeServiceHandler.Instance?.IsAppStartup != true && !SettingsViewModel.Instance.BackgroundStart)
                mainWindow.Show();
        }

        /// <summary>The login pages on the screen of the Android app, as large as the PC's window at most.</summary>
        private static Control FitLogin(LoginView login)
        {
            // The whole screen; on a phone held upright the covers' column goes and the forms take the width
            login.MaxWidth = 1100;
            login.MaxHeight = 760;
            var frame = new Border { Child = login };
            frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Brush.Background2"));
            return frame;
        }

        /// <summary>Back to the profiles (sign-out, another profile): the main view is disposed.</summary>
        /// <param name="signInAgain">Android: the profile chosen last signs in by itself (the living-room mode's change of profile).</param>
        public void OpenLoginView(bool signInAgain = false)
        {
            if (SingleView != null)
            {
                (singleViewShell.Content as MainView)?.Dispose();
                singleViewShell.Content = FitLogin(new LoginView(skipBootTasks: !signInAgain));
                return;
            }
            var mainWindow = (MainWindow)MainWindow!;
            var loginWindow = new LoginWindow(true);
            MainWindow = loginWindow;
            loginWindow.Show();
            mainWindow.Dispose();
        }

        public void LogUnhandledException(Exception e)
        {
            if (handlingFatalError)
                return;
            handlingFatalError = true;
            try
            {
                Directory.CreateDirectory(ProfileManager.ErrorLogDir);
                string errorLogPath = Path.Combine(ProfileManager.ErrorLogDir, $"SanctuaryVault_ErrorLog_{DateTime.Now:yyyyMMddHHmmssfff}.txt");
                string errorMessage = $"MESSAGE:\n{e.Message}\nINNER_EXCEPTION:{e.InnerException?.Message}";
                string errorStackTrace = $"STACK_TRACE:\n{e.StackTrace}";
                File.WriteAllText(errorLogPath, errorMessage + "\n" + errorStackTrace);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            Log.Error(e, "Unhandled exception");

            if (SingleView != null)
            {
                // No second window on Android: the message takes the screen
                singleViewShell.Content = new SelectableTextBlock { Text = Loc.T("SanctuaryVault stopped after an error.") + "\n\n" + e, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) };
                return;
            }
            var exceptionWindow = new ExceptionWindow();
            exceptionWindow.Closed += (_, _) => ShutdownApp();
            exceptionWindow.Show();
        }

        private void InitTrayIcon()
        {
            if (trayIcon != null)
                return;
            try
            {
                var menu = new NativeMenu();
                if (!Helper.Platform.PlatformInfo.IsMac)
                    menu.Items.Add(CreateTrayItem(Loc.T("Library"), () => NavigateFromTray(MainControl.Library)));
                menu.Items.Add(CreateTrayItem(Loc.T("Consoles and retro"), () => NavigateFromTray(MainControl.Retro)));
                menu.Items.Add(CreateTrayItem(Loc.T("Downloads"), () => NavigateFromTray(MainControl.Downloads)));
                menu.Items.Add(CreateTrayItem(Loc.T("Community"), () => NavigateFromTray(MainControl.Community)));
                menu.Items.Add(CreateTrayItem(Loc.T("Settings"), () => NavigateFromTray(MainControl.Settings)));
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(CreateTrayItem(Loc.T("Exit"), async () => await ExitApp()));

                trayIcon = new TrayIcon
                {
                    ToolTipText = "SanctuaryVault",
                    Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://gamevault/Resources/Images/icon.ico"))),
                    Menu = menu,
                    IsVisible = true,
                };
                trayIcon.Clicked += (_, _) => ShowMainWindow();
                TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
            }
            catch (Exception ex)
            {
                // No tray available (e.g. GNOME without the AppIndicator extension): closing the window must then quit the app.
                Log.Error(ex, "Tray icon unavailable");
                trayIcon = null;
            }
        }

        public bool HasTrayIcon => trayIcon != null;

        private static NativeMenuItem CreateTrayItem(string header, Action action)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => action();
            return item;
        }

        private void NavigateFromTray(MainControl control)
        {
            ShowMainWindow();
            MainWindowViewModel.Instance.SetActiveControl(control);
        }

        public void ShowMainWindow()
        {
            Window? window = MainWindow;
            if (window is not gamevault.Windows.MainWindow)
                return;
            window.Show();
            // The living-room mode is on the whole screen: back to it, not to a window
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = gamevault.Windows.MainView.Current?.IsCouchShown == true ? WindowState.FullScreen : WindowState.Normal;
            window.Activate();
        }

        public void SetTheme(string themeUri)
        {
            ThemeManager.Apply(themeUri);
        }

        public void ResetToDefaultTheme()
        {
            ThemeManager.ApplyDefault();
        }

        public async Task ExitApp()
        {
            if (DownloadsViewModel.Instance.DownloadedGames.Any(g => g.IsDownloading()) || DownloadsViewModel.Instance.RetroDownloads.Any(d => d.IsDownloading()))
            {
                ShowMainWindow();
                MessageDialogResult result = await MainWindow.ShowMessageAsync(Loc.T("Downloads are still running in the background, are you sure you want to exit the app anyway?\nThey are paused and can be resumed at the next start."), "",
                    MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });
                if (result == MessageDialogResult.Affirmative)
                {
                    MainWindowViewModel.Instance.Downloads.PauseAllDownloads();
                    // The download loop writes the resume position at its next read
                    await Task.Delay(500);
                    ShutdownApp();
                }
            }
            else
            {
                ShutdownApp();
            }
        }

        public void ShutdownApp()
        {
            HideToSystemTray = false;
            ProcessShepherd.Instance.KillAllChildProcesses();
            trayIcon?.Dispose();
            trayIcon = null;
            Shutdown();
        }

        private void Shutdown()
        {
            Desktop?.Shutdown();
        }

        public bool IsWindowActiveAndControlInFocus(MainControl control)
        {
            if (SingleView != null)
                return MainWindowViewModel.Instance.ActiveControlIndex == (int)control;
            if (MainWindow == null)
                return false;

            return MainWindow.IsActive && MainWindowViewModel.Instance.ActiveControlIndex == (int)control;
        }
    }
}
