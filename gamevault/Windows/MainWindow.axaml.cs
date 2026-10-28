using Avalonia.Controls;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.Localization;
using gamevault.Models;
using GameVault.Core;
using System;

namespace gamevault.Windows
{
    /// <summary>The PC's window around <see cref="MainView"/>: closed to the tray, disposed on sign-out.</summary>
    public partial class MainWindow : Window, IDisposable
    {
        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
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

        public void Dispose()
        {
            View.Dispose();
            this.Close();
        }
    }
}
