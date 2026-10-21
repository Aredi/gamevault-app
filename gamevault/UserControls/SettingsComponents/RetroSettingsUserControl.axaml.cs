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
    /// <summary>Settings > Retro: what RetroArch takes, the BIOS of the platforms, and the saves with their history.</summary>
    public partial class RetroSettingsUserControl : UserControl
    {
        internal sealed record BiosRow(string Platform, string State);

        /// <summary>The platforms by name (offline, RomM's names are not known).</summary>
        private static readonly Dictionary<string, string> Names = new()
        {
            ["nes"] = "NES", ["snes"] = "Super Nintendo", ["gba"] = "Game Boy Advance", ["nds"] = "Nintendo DS", ["ngc"] = "GameCube", ["wii"] = "Wii",
            ["psx"] = "PlayStation", ["ps2"] = "PlayStation 2", ["neogeoaes"] = "Neo Geo", ["3ds"] = "Nintendo 3DS",
        };

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
            var cores = RetroSystems.All.Values.Where(s => File.Exists(layout.CoreFile(s.Core))).Select(s => Names.GetValueOrDefault(s.Slug, s.Slug)).ToList();
            uiCores.Text = cores.Count > 0 ? Loc.F("Emulators installed for: {0}", string.Join(", ", cores)) : "";

            uiBios.ItemsSource = RetroSystems.All.Values.Where(s => s.FirmwareFolder != null).Select(system =>
            {
                string folder = Path.Combine(layout.SystemDirectory, system.FirmwareFolder!);
                var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Select(Path.GetFileName).Where(f => !f!.StartsWith('.')).ToList() : new();
                return new BiosRow(Names.GetValueOrDefault(system.Slug, system.Slug),
                    files.Count > 0 ? Loc.F("On this PC: {0}", string.Join(", ", files)) : Loc.T("Not on this PC yet: taken from RomM at the first game"));
            }).ToList();

            uiSavesState.Text = SettingsViewModel.Instance.CloudSaves
                ? Loc.T("Each game's saves go to the SanctuaryVault service after it is played and come back on your other PCs, as the cloud saves of the library.")
                : Loc.T("Kept on this PC only: the cloud saves are off (Settings > Data).");
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
            if (!await DialogService.ConfirmAsync(Loc.T("Uninstall RetroArch, its cores and the BIOS? They come back at the next retro game; the saves are kept."), "RetroArch"))
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
