using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Retro;
using GameVault.Core.Storage;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.UserControls;
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

        /// <summary>A console of RetroArch and whether it has the CRT look; the consoles of a core and whether they are in 16:9.</summary>
        internal sealed class CrtRow(string slug, string name, bool isOn)
        {
            public string Slug { get; } = slug;
            public string Name { get; } = name;
            public bool IsOn { get; set; } = isOn;
        }

        /// <summary>The 3D consoles of one emulator and the definition they are drawn at.</summary>
        internal sealed class ResolutionRow(string core, string consoles, int[] scales, int scale)
        {
            public string Core { get; } = core;
            public string Consoles { get; } = consoles;
            public int[] Scales { get; } = scales;
            public string[] Choices { get; } = scales.Select(s => s == 1 ? Loc.T("Native") : $"{s}x").ToArray();
            public int Index { get; set; } = Math.Max(0, Array.IndexOf(scales, scale));
        }

        public RetroSettingsUserControl()
        {
            InitializeComponent();
            AttachedToVisualTree += (_, _) => _ = RefreshAsync();
        }

        /// <summary>The consoles whose buttons were changed in Régler mes touches, and with what.</summary>
        private void RefreshControls()
        {
            var keyboard = RetroArchManager.Keyboard;
            var pad = RetroArchManager.Pad;
            uiControlsSet.ItemsSource = RetroControlsUserControl.Systems
                .Select(s => (s.Name, Pad: pad.ContainsKey(s.Slug), Keyboard: keyboard.ContainsKey(s.Slug)))
                .Where(s => s.Pad || s.Keyboard)
                .Select(s => s.Pad && s.Keyboard ? Loc.F("{0} · gamepad and keyboard", s.Name) : s.Pad ? Loc.F("{0} · gamepad", s.Name) : Loc.F("{0} · keyboard", s.Name))
                .ToList();
        }

        private void Controls_Click(object? sender, RoutedEventArgs e)
        {
            var controls = new RetroControlsUserControl();
            controls.DetachedFromVisualTree += (_, _) => RefreshControls();
            MainWindowViewModel.Instance.OpenPopup(controls);
        }

        internal async Task RefreshAsync()
        {
            RetroArchLayout layout = RetroArchManager.Layout;
            long size = await Task.Run(RetroArchManager.InstalledSize);
            uiRetroArch.Text = layout.IsInstalled
                ? Loc.F("Version {0} installed · {1}", RetroArchLayout.Version, StorageCleanup.FormatSize(size))
                : Loc.T("Not installed");
            uiUninstall.IsEnabled = size > 0;
            var cores = RetroSystems.All.Values.Where(s => StandaloneEmulator.Of(s) is StandaloneEmulator emulator ? emulator.Layout.IsInstalled : File.Exists(layout.CoreFile(s.Core)))
                .Select(s => StandaloneEmulator.Of(s) is StandaloneEmulator emulator ? $"{s.Name} ({emulator.Name})" : s.Name).Distinct().ToList();
            uiCores.Text = cores.Count > 0 ? Loc.F("Emulators installed for: {0}", string.Join(", ", cores)) : "";

            uiBios.ItemsSource = RetroSystems.All.Values.Where(s => s.FirmwareFolder != null).Select(system =>
            {
                string folder = Path.Combine(layout.SystemDirectory, system.FirmwareFolder!);
                var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Select(Path.GetFileName).Where(f => !f!.StartsWith('.')).ToList() : new();
                return new BiosRow(system.Name,
                    files.Count > 0 ? Loc.F("On this PC: {0}", string.Join(", ", files)) : Loc.T("Not downloaded yet"));
            }).Concat(RetroSystems.All.Values.Where(s => s.IsStandalone).Select(s => new BiosRow(s.Name, StandaloneEmulator.Of(s)!.FirmwareState))).ToList();

            await RefreshRommAsync();
            RefreshAchievements();
            var crt = RetroArchManager.CrtSystems;
            uiCrt.ItemsSource = RetroSystems.All.Values.Where(s => !s.IsStandalone).Select(s => new CrtRow(s.Slug, s.Name, crt.Contains(s.Slug))).ToList();
            var resolutions = RetroArchManager.Resolutions;
            uiResolution.ItemsSource = RetroResolution.Scales.Select(r => new ResolutionRow(r.Key,
                string.Join(", ", RetroSystems.All.Values.Where(s => s.Core == r.Key).Select(s => s.Name)), r.Value, resolutions.GetValueOrDefault(r.Key, 1))).ToList();
            var widescreen = RetroArchManager.WidescreenCores;
            uiWidescreen.ItemsSource = RetroResolution.WidescreenCores.Select(core => new CrtRow(core,
                string.Join(", ", RetroSystems.All.Values.Where(s => s.Core == core).Select(s => s.Name)), widescreen.Contains(core))).ToList();
            RefreshControls();
            uiLanguageConsoles.Text = Loc.F("The consoles that have a language start in this one: {0}. A game without it keeps its own; a game's page can choose another.",
                string.Join(", ", RetroSystems.All.Values.Where(s => RetroLanguage.Cores.Contains(s.Core)).Select(s => s.Name)));
            uiLanguage.ItemsSource = RetroLanguage.All.Select(RetroGameViewModel.LanguageName)
                .Prepend(Loc.F("As the client ({0})", RetroGameViewModel.Lower(RetroGameViewModel.LanguageName(RetroArchManager.ClientLanguage)))).ToList();
            uiLanguage.SelectedIndex = Array.IndexOf(RetroLanguage.All, RetroArchManager.LanguageSetting) + 1;
            uiSavesState.Text = SettingsViewModel.Instance.CloudSaves
                ? Loc.F("Cloud saves: {0}", RetroCloudSaves.Destination)
                : Loc.T("Cloud saves off (Settings > Data)");
        }

        private string? rommPage;

        private async Task RefreshRommAsync()
        {
            bool available = await RommAccount.UrlAsync() != null;
            uiRommState.Text = !available ? Loc.T("RomM is not set up on this server.")
                : RommAccount.IsLinked ? Loc.F("Linked as {0}", RommAccount.Username)
                : Loc.T("Not linked");
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

        private void RefreshAchievements()
        {
            bool signedIn = RetroAchievementsAccount.IsSignedIn;
            uiAchievementsState.Text = signedIn
                ? Loc.F("Signed in as {0}", RetroAchievementsAccount.User)
                : Loc.T("Not signed in");
            uiAchievementsForm.IsVisible = !signedIn;
            uiAchievementsSignOut.IsVisible = signedIn;
        }

        private async void AchievementsSignIn_Click(object? sender, RoutedEventArgs e) => await SignInAchievementsAsync();

        private async void AchievementsPassword_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                await SignInAchievementsAsync();
        }

        private async Task SignInAchievementsAsync()
        {
            if (string.IsNullOrWhiteSpace(uiAchievementsUser.Text) || string.IsNullOrEmpty(uiAchievementsPassword.Text))
                return;
            uiAchievementsSignIn.IsEnabled = false;
            try
            {
                string? failure = await RetroAchievementsAccount.SignInAsync(uiAchievementsUser.Text, uiAchievementsPassword.Text);
                MainWindowViewModel.Instance.AppBarText = failure ?? Loc.F("Signed in to RetroAchievements as {0}", RetroAchievementsAccount.User);
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
            // The password is not kept, not even here
            uiAchievementsPassword.Text = "";
            uiAchievementsSignIn.IsEnabled = true;
            RefreshAchievements();
        }

        private void AchievementsSignOut_Click(object? sender, RoutedEventArgs e)
        {
            RetroAchievementsAccount.SignOut();
            RefreshAchievements();
        }

        private void Language_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (uiLanguage.SelectedIndex < 0 || !IsLoaded)
                return;
            RetroArchManager.LanguageSetting = uiLanguage.SelectedIndex == 0 ? "" : RetroLanguage.All[uiLanguage.SelectedIndex - 1];
        }

        private void Resolution_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not ResolutionRow row || sender is not ComboBox box || box.SelectedIndex < 0 || !IsLoaded)
                return;
            var resolutions = RetroArchManager.Resolutions;
            resolutions[row.Core] = row.Scales[box.SelectedIndex];
            RetroArchManager.Resolutions = resolutions;
        }

        private void Widescreen_Changed(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not CrtRow row || sender is not CheckBox box)
                return;
            var cores = RetroArchManager.WidescreenCores;
            if (box.IsChecked == true)
                cores.Add(row.Slug);
            else
                cores.Remove(row.Slug);
            RetroArchManager.WidescreenCores = cores;
        }

        private void Crt_Changed(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not CrtRow row || sender is not CheckBox box)
                return;
            var crt = RetroArchManager.CrtSystems;
            if (box.IsChecked == true)
                crt.Add(row.Slug);
            else
                crt.Remove(row.Slug);
            RetroArchManager.CrtSystems = crt;
        }

        private async void OpenMenu_Click(object? sender, RoutedEventArgs e)
        {
            try { await RetroArchManager.OpenMenuAsync(text => Dispatcher.UIThread.Post(() => uiRetroArch.Text = text)); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
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
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private async void Uninstall_Click(object? sender, RoutedEventArgs e)
        {
            if (!await DialogService.ConfirmAsync(Loc.T("Uninstall the emulators, the BIOS and the firmware? The saves are kept."), "RetroArch"))
                return;
            try { await RetroArchManager.UninstallAsync(); }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
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
