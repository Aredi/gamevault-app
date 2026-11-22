using Avalonia.Controls;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using gamevault.Windows;

namespace gamevault.UserControls.Mobile
{
    /// <summary>The phone's profile: who is signed in, and the pages the computer's menu had.</summary>
    public partial class MobileProfileUserControl : UserControl
    {
        public MobileProfileUserControl()
        {
            InitializeComponent();
            DataContext = MainWindowViewModel.Instance;
            AttachedToVisualTree += (_, _) =>
            {
                uiServer.Text = SettingsViewModel.Instance.ServerUrl;
                uiVersion.Text = "SanctuaryVault " + SettingsViewModel.Instance.Version;
            };
            uiVersion.Holding += (_, _) => ToggleFrameRate();
            uiVersion.DoubleTapped += (_, _) => ToggleFrameRate();
        }

        /// <summary>Avalonia's own counters over the screen: frames per second, the time of the layout and of the rendering.</summary>
        private void ToggleFrameRate()
        {
            if (TopLevel.GetTopLevel(this) is not TopLevel screen)
                return;
            var shown = Avalonia.Rendering.RendererDebugOverlays.Fps | Avalonia.Rendering.RendererDebugOverlays.LayoutTimeGraph | Avalonia.Rendering.RendererDebugOverlays.RenderTimeGraph;
            screen.RendererDiagnostics.DebugOverlays = screen.RendererDiagnostics.DebugOverlays == shown ? Avalonia.Rendering.RendererDebugOverlays.None : shown;
        }

        private static void Open(MainControl page) => MainWindowViewModel.Instance.SetActiveControl(page);

        private void MyProfile_Click(object? sender, RoutedEventArgs e)
        {
            if (LoginManager.Instance.GetCurrentUser() is User me)
            {
                MainWindowViewModel.Instance.Community.ShowUser(me, alone: true);
            }
        }

        private void Downloads_Click(object? sender, RoutedEventArgs e) => Open(MainControl.Downloads);
        private void Activity_Click(object? sender, RoutedEventArgs e) => Open(MainControl.Activity);
        private void Community_Click(object? sender, RoutedEventArgs e) => Open(MainControl.Community);
        private void Settings_Click(object? sender, RoutedEventArgs e) => Open(MainControl.Settings);
        private void Admin_Click(object? sender, RoutedEventArgs e) => Open(MainControl.AdminConsole);
        private void News_Click(object? sender, RoutedEventArgs e) => MainView.Current?.OpenNews();
        private void Couch_Click(object? sender, RoutedEventArgs e) => MainView.Current?.EnterCouch();
        private void Problem_Click(object? sender, RoutedEventArgs e) => MainView.Current?.OpenProblemReport();

        private void ChangeProfile_Click(object? sender, RoutedEventArgs e)
        {
            Preferences.DeleteKey(AppConfigKey.LastUserProfile, ProfileManager.ProfileConfigFile);
            App.Instance.OpenLoginView();
        }
    }
}
