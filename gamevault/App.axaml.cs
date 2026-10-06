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

        public override void Initialize()
        {
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

        public void LogUnhandledException(Exception e)
        {
            if (handlingFatalError)
                return;
            handlingFatalError = true;
            try
            {
                Directory.CreateDirectory(ProfileManager.ErrorLogDir);
                string errorLogPath = Path.Combine(ProfileManager.ErrorLogDir, $"GameVault_ErrorLog_{DateTime.Now:yyyyMMddHHmmssfff}.txt");
                string errorMessage = $"MESSAGE:\n{e.Message}\nINNER_EXCEPTION:{e.InnerException?.Message}";
                string errorStackTrace = $"STACK_TRACE:\n{e.StackTrace}";
                File.WriteAllText(errorLogPath, errorMessage + "\n" + errorStackTrace);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            Log.Error(e, "Unhandled exception");

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
                menu.Items.Add(CreateTrayItem("Library", () => NavigateFromTray(MainControl.Library)));
                menu.Items.Add(CreateTrayItem("Downloads", () => NavigateFromTray(MainControl.Downloads)));
                menu.Items.Add(CreateTrayItem("Community", () => NavigateFromTray(MainControl.Community)));
                menu.Items.Add(CreateTrayItem("Settings", () => NavigateFromTray(MainControl.Settings)));
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(CreateTrayItem("Exit", async () => await ExitApp()));

                trayIcon = new TrayIcon
                {
                    ToolTipText = "GameVault",
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
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
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
            if (DownloadsViewModel.Instance.DownloadedGames.Any(g => g.IsDownloading()))
            {
                ShowMainWindow();
                MessageDialogResult result = await MainWindow.ShowMessageAsync("Downloads are still running in the background, are you sure you want to exit the app anyway?\nThey are paused and can be resumed at the next start.", "",
                    MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No" });
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
            if (MainWindow == null)
                return false;

            return MainWindow.IsActive && MainWindowViewModel.Instance.ActiveControlIndex == (int)control;
        }
    }
}
