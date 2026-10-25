using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>Sunshine installed (Windows: its installer; Linux: its guide) and running, then its login kept; then the sharing goes on.</summary>
    public partial class SunshineSetupPopup : UserControl
    {
        private readonly Func<Task> ready;
        private bool running;

        public SunshineSetupPopup() : this(() => Task.CompletedTask) { }

        public SunshineSetupPopup(Func<Task> ready)
        {
            this.ready = ready;
            InitializeComponent();
            uiLinuxGuide.Text = SunshineHost.LinuxGuide;
            uiUser.Text = SunshineHost.Login?.User;
            _ = ShowAsync();
        }

        internal async Task ShowAsync()
        {
            running = await SunshineHost.RunningAsync();
            Mark(uiInstallDot, uiInstallMark, running, "1");
            uiLinuxGuide.IsVisible = !running && !PlatformInfo.IsWindows;
            uiInstallText.Text = running ? Loc.T("Installed and running")
                : PlatformInfo.IsWindows ? Loc.T("Its official installer: keep its virtual gamepads' driver (ViGEmBus) checked.")
                : Loc.T("Install it from your distribution (its package or its Flatpak) as its guide says, start it, then come back here:");
            uiNext.Content = running ? Loc.T("Share my screen") : PlatformInfo.IsWindows ? Loc.T("Install Sunshine") : Loc.T("It is running");
        }

        private void Mark(Border dot, TextBlock mark, bool done, string step)
        {
            dot.Background = done && this.TryFindResource("Brush.Success", out object? found) && found is IBrush b ? b : Brushes.Transparent;
            mark.Text = done ? "✓" : step;
        }

        private void OpenPage_Click(object? sender, RoutedEventArgs e) => PlatformInfo.OpenUrl(SunshineHost.WebPage);

        private void Later_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();

        private async void Next_Click(object? sender, RoutedEventArgs e)
        {
            uiNext.IsEnabled = false;
            try
            {
                if (!running)
                {
                    if (PlatformInfo.IsWindows)
                        await SunshineHost.InstallAsync(text => Dispatcher.UIThread.Post(() => uiStatus.Text = text));
                    await ShowAsync();
                    uiStatus.Text = running ? "" : Loc.T("Sunshine does not answer yet: start it, then try again.");
                }
                else
                {
                    await SunshineHost.SaveLoginAsync(uiUser.Text ?? "", uiPassword.Text ?? "");
                    Mark(uiLoginDot, uiLoginMark, true, "2");
                    MainWindowViewModel.Instance.ClosePopup();
                    await ready();
                }
            }
            catch (Exception ex) { uiStatus.Text = ex.Message; }
            finally { uiNext.IsEnabled = true; }
        }
    }
}
