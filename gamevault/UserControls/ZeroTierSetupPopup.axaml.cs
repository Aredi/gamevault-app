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
    /// <summary>ZeroTier installed (Windows: its installer; Linux: the command to type), then usable by SanctuaryVault.</summary>
    public partial class ZeroTierSetupPopup : UserControl
    {
        public ZeroTierSetupPopup()
        {
            InitializeComponent();
            uiPriority.IsVisible = uiFirewall.IsVisible = PlatformInfo.IsWindows;
            uiLinuxCommand.Text = ZeroTierAgent.LinuxInstall;
            Show();
        }

        private void Show()
        {
            bool installed = ZeroTierAgent.IsInstalled, access = installed && ZeroTierAgent.HasAccess;
            Mark(uiInstallDot, uiInstallMark, installed, "1");
            Mark(uiAccessDot, uiAccessMark, access, "2");
            uiLinuxCommand.IsVisible = !installed && !PlatformInfo.IsWindows;
            uiInstallText.Text = installed ? Loc.T("Installed")
                : PlatformInfo.IsWindows ? Loc.T("The official installer, from ZeroTier's site: Windows asks to allow it.")
                : Loc.T("Type this command in a terminal, then come back here:");
            uiNext.Content = !installed ? (PlatformInfo.IsWindows ? Loc.T("Install ZeroTier") : Loc.T("I installed it"))
                : !access ? Loc.T("Allow") : Loc.T("Done");
        }

        private void Mark(Border dot, TextBlock mark, bool done, string step)
        {
            dot.Background = done && this.TryFindResource("Brush.Success", out object? found) && found is IBrush b ? b : Brushes.Transparent;
            mark.Text = done ? "✓" : step;
        }

        private void Later_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();

        private async void Next_Click(object? sender, RoutedEventArgs e)
        {
            uiNext.IsEnabled = false;
            try
            {
                if (!ZeroTierAgent.IsInstalled)
                {
                    if (PlatformInfo.IsWindows)
                        await ZeroTierAgent.InstallAsync(text => Dispatcher.UIThread.Post(() => uiStatus.Text = text));
                    uiStatus.Text = ZeroTierAgent.IsInstalled ? "" : Loc.T("ZeroTier is not installed yet.");
                }
                else if (!ZeroTierAgent.HasAccess)
                {
                    await ZeroTierAgent.GrantAccessAsync();
                    uiStatus.Text = "";
                }
                else
                {
                    MainWindowViewModel.Instance.ClosePopup();
                    await MainWindowViewModel.Instance.Rooms.LoadAsync();
                    return;
                }
            }
            catch (Exception ex) { uiStatus.Text = WebExceptionHelper.TryGetServerMessage(ex); }
            finally { uiNext.IsEnabled = true; }
            Show();
        }
    }
}
