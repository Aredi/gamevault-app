using Avalonia.Controls;
using Avalonia.Interactivity;
using GameVault.Core;
using GameVault.Core.Retro;
using GameVault.Core.Storage;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls.SettingsComponents
{
    /// <summary>Settings > Retro: what RetroArch takes, the BIOS of the platforms, the RomM account and the saves' folders.</summary>
    public partial class RetroSettingsUserControl : UserControl
    {
        internal sealed record BiosRow(string Platform, string State);

        public RetroSettingsUserControl()
        {
            InitializeComponent();
            AttachedToVisualTree += (_, _) => _ = RefreshAsync();
        }

        internal async Task RefreshAsync()
        {
            RetroArchLayout layout = RetroArchManager.Layout;
            long size = await Task.Run(RetroArchManager.InstalledSize);
            uiRetroArch.Text = layout.IsInstalled
                ? Loc.F("Version {0} installed · {1}", RetroArchLayout.Version, StorageCleanup.FormatSize(size))
                : Loc.T("Not installed yet: it is installed with the first retro game.");
            uiUninstall.IsEnabled = size > 0;
            var cores = RetroSystems.All.Values.Where(s => s.IsRpcs3 ? Rpcs3Manager.Layout.IsInstalled : File.Exists(layout.CoreFile(s.Core)))
                .Select(s => s.IsRpcs3 ? $"{s.Name} (RPCS3)" : s.Name).Distinct().ToList();
            uiCores.Text = cores.Count > 0 ? Loc.F("Emulators installed for: {0}", string.Join(", ", cores)) : "";

            uiBios.ItemsSource = RetroSystems.All.Values.Where(s => s.FirmwareFolder != null).Select(system =>
            {
                string folder = Path.Combine(layout.SystemDirectory, system.FirmwareFolder!);
                var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Select(Path.GetFileName).Where(f => !f!.StartsWith('.')).ToList() : new();
                return new BiosRow(system.Name,
                    files.Count > 0 ? Loc.F("On this PC: {0}", string.Join(", ", files)) : Loc.T("Not on this PC yet: taken from RomM at the first game"));
            }).Append(new BiosRow(RetroSystems.All["ps3"].Name, Rpcs3Manager.Layout.HasFirmware
                    ? Loc.T("Firmware installed in RPCS3")
                    : Loc.T("Not on this PC yet: PS3UPDAT.PUP is taken from RomM at the first game, RPCS3 asks to install it"))).ToList();

            await RefreshRommAsync();
            uiSavesState.Text = SettingsViewModel.Instance.CloudSaves
                ? Loc.F("Each game's saves go to {0} after it is played and come back on your other PCs, as the cloud saves of the library.", RetroCloudSaves.Destination)
                : Loc.T("Kept on this PC only: the cloud saves are off (Settings > Data).");
        }

        private string? rommPage;

        private async Task RefreshRommAsync()
        {
            bool available = await RommAccount.UrlAsync() != null;
            uiRommState.Text = !available ? Loc.T("RomM is not set up on this server.")
                : RommAccount.IsLinked ? Loc.F("Linked to RomM as {0}: the retro saves go to your RomM.", RommAccount.Username)
                : Loc.T("Not linked: the retro saves go to SanctuaryVault.");
            uiRommLink.IsVisible = available && !RommAccount.IsLinked;
            uiRommUnlink.IsVisible = RommAccount.IsLinked;
        }

        private async void RommLink_Click(object? sender, RoutedEventArgs e) => await LinkRommAsync(open: true);

        /// <summary>RomM's pairing: the code shown here, RomM's page opened, the approval waited for.</summary>
        internal async Task LinkRommAsync(bool open)
        {
            uiRommLink.IsEnabled = false;
            try
            {
                string? failure = await RommAccount.LinkAsync((code, page) =>
                {
                    rommPage = page;
                    uiRommCodeText.Text = code;
                    uiRommCode.IsVisible = true;
                    if (open)
                        PlatformInfo.OpenUrl(page);
                }, System.Threading.CancellationToken.None);
                MainWindowViewModel.Instance.AppBarText = failure ?? Loc.F("Linked to RomM as {0}", RommAccount.Username);
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
            uiRommCode.IsVisible = false;
            uiRommLink.IsEnabled = true;
            await RefreshAsync();
        }

        private void RommPage_Click(object? sender, RoutedEventArgs e)
        {
            if (rommPage != null)
                PlatformInfo.OpenUrl(rommPage);
        }

        private async void RommUnlink_Click(object? sender, RoutedEventArgs e)
        {
            if (!await DialogService.ConfirmAsync(Loc.T("Unlink your RomM account? The retro saves go to SanctuaryVault again (the ones in RomM stay there)."), "RomM"))
                return;
            RommAccount.Unlink();
            await RefreshAsync();
        }

        private async void OpenMenu_Click(object? sender, RoutedEventArgs e)
        {
            try { await RetroArchManager.OpenMenuAsync(text => uiRetroArch.Text = text); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
            await RefreshAsync();
        }

        private async void ResetSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (!await DialogService.ConfirmAsync(Loc.T("Forget the core options and controls changed in RetroArch's menu?"), "RetroArch"))
                return;
            try
            {
                RetroArchManager.ResetMenuSettings();
                MainWindowViewModel.Instance.AppBarText = Loc.T("RetroArch's settings were reset");
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
        }

        private async void Uninstall_Click(object? sender, RoutedEventArgs e)
        {
            if (!await DialogService.ConfirmAsync(Loc.T("Uninstall the emulators (RetroArch, its cores, RPCS3), the BIOS and the PS3 firmware? They come back at the next retro game; the saves are kept."), "RetroArch"))
                return;
            try { await RetroArchManager.UninstallAsync(); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
            await RefreshAsync();
        }

        private void OpenSaves_Click(object? sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(RetroArchManager.SavesFolder);
            PlatformInfo.OpenFolder(RetroArchManager.SavesFolder);
        }

        private void OpenBackups_Click(object? sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(RetroArchManager.SaveBackupsFolder);
            PlatformInfo.OpenFolder(RetroArchManager.SaveBackupsFolder);
        }
    }
}
