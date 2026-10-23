using Avalonia.Controls;
using Avalonia.Input;
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

        /// <summary>A console of RetroArch and whether it has the CRT look.</summary>
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

        /// <summary>A console with several controllers and the one its games are played with.</summary>
        internal sealed class ControllerRow(string slug, string console, RetroControllers.Controller[] controllers, int? device)
        {
            public string Slug { get; } = slug;
            public string Console { get; } = console;
            public RetroControllers.Controller[] Controllers { get; } = controllers;
            public string[] Choices { get; } = controllers.Select(c => RetroGameViewModel.ControllerName(c.Name)).ToArray();
            public int Index { get; set; } = Math.Max(0, Array.FindIndex(controllers, c => c.Device == device));
        }

        /// <summary>A button of the console chosen in Keyboard and its key; the one waiting for a key is listening.</summary>
        internal sealed class KeyRow(RetroKeyboard.Button button, RetroKeyboard.Key? key, bool listening)
        {
            public RetroKeyboard.Button Button { get; } = button;
            public string Name { get; } = Shown(button.Name);
            public bool Listening { get; } = listening;
            public string KeyText { get; } = listening ? Loc.T("Press a key...") : key == null || key == RetroKeyboard.None ? "—" : Shown(key.Label);
        }

        /// <summary>The consoles whose keys are chosen here: the ones RetroArch plays.</summary>
        internal static readonly RetroSystem[] KeyboardSystems = RetroSystems.All.Values.Where(s => RetroKeyboard.Of(s.Slug).Length > 0).ToArray();
        private RetroSystem KeyboardSystem => KeyboardSystems[Math.Max(0, uiKeyboardConsole.SelectedIndex)];
        private string? listening;

        public RetroSettingsUserControl()
        {
            InitializeComponent();
            AttachedToVisualTree += (_, _) => _ = RefreshAsync();
            uiKeyboardConsole.ItemsSource = KeyboardSystems.Select(s => s.Name).ToList();
            uiKeyboardConsole.SelectedIndex = 0;
            // On the window, before the buttons: the button clicked is rebuilt (its focus is lost), Enter and Space are keys to give
            AttachedToVisualTree += (_, _) => TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, Key_Pressed, RoutingStrategies.Tunnel);
            DetachedFromVisualTree += (_, e) => (e.Root as Interactive)?.RemoveHandler(KeyDownEvent, Key_Pressed);
        }

        /// <summary>The Wii's buttons are its controller's (Controllers above).</summary>
        private int? KeyboardDevice => RetroControllers.Pick(KeyboardSystem.Slug, null, RetroArchManager.Controllers.TryGetValue(KeyboardSystem.Slug, out int device) ? device : null);

        internal void RefreshKeys()
        {
            var chosen = RetroArchManager.Keyboard.GetValueOrDefault(KeyboardSystem.Slug);
            uiKeys.ItemsSource = RetroKeyboard.Of(KeyboardSystem.Slug, KeyboardDevice).Select(b => new KeyRow(b, RetroKeyboard.KeyOf(chosen, b.Bind), b.Bind == listening)).ToList();
            uiKeyboardDefaults.IsEnabled = chosen?.Count > 0;
        }

        private void ShowKeyboardMessage(string? text)
        {
            uiKeyboardMessage.Text = text;
            uiKeyboardMessage.IsVisible = text != null;
        }

        private void KeyboardConsole_Changed(object? sender, SelectionChangedEventArgs e)
        {
            listening = null;
            ShowKeyboardMessage(null);
            RefreshKeys();
        }

        private void Key_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not KeyRow row)
                return;
            listening = row.Listening ? null : row.Button.Bind;
            ShowKeyboardMessage(null);
            RefreshKeys();
        }

        private void Key_Pressed(object? sender, KeyEventArgs e)
        {
            if (listening == null)
                return;
            e.Handled = true;
            Give(listening, e.Key == Key.Escape ? null : RetroKeys.Of(e.PhysicalKey, e.KeySymbol), e.Key == Key.Escape);
        }

        /// <summary>The key pressed for the button waiting for one: kept for the console, unless RetroArch keeps it or does not read it.</summary>
        internal void Give(string bind, RetroKeyboard.Key? key, bool cancel = false)
        {
            listening = null;
            ShowKeyboardMessage(null);
            if (!cancel && key == null)
                ShowKeyboardMessage(Loc.T("RetroArch does not read this key: choose another one."));
            else if (key != null && RetroKeyboard.Reserved.TryGetValue(key.Name, out string? use))
                ShowKeyboardMessage(Loc.F("{0} stays RetroArch's: {1}.", key.Label, Shown(use)));
            else if (key != null)
            {
                var keyboard = RetroArchManager.Keyboard;
                keyboard[KeyboardSystem.Slug] = RetroKeyboard.Assign(KeyboardSystem.Slug, KeyboardDevice, keyboard.GetValueOrDefault(KeyboardSystem.Slug), bind, key)!;
                RetroArchManager.Keyboard = keyboard;
            }
            RefreshKeys();
        }

        private void KeyboardDefaults_Click(object? sender, RoutedEventArgs e)
        {
            var keyboard = RetroArchManager.Keyboard;
            keyboard.Remove(KeyboardSystem.Slug);
            RetroArchManager.Keyboard = keyboard;
            listening = null;
            ShowKeyboardMessage(null);
            RefreshKeys();
        }

        /// <summary>A button's or a key's name in the player's language; a letter or a console's own name ("L1", "+") stays.</summary>
        internal static string Shown(string text)
        {
            string arrow = text.Length > 2 && text[^2] == ' ' && "↑↓←→".Contains(text[^1]) ? text[^2..] : "";
            string name = text[..^arrow.Length] switch
            {
                "Up" => Loc.T("Up"), "Down" => Loc.T("Down"), "Left" => Loc.T("Left"), "Right" => Loc.T("Right"),
                "Cross" => Loc.T("Cross"), "Circle" => Loc.T("Circle"), "Square" => Loc.T("Square"), "Coin" => Loc.T("Coin"),
                "Control Stick" => Loc.T("Control Stick"), "C Stick" => Loc.T("C Stick"), "Left Stick" => Loc.T("Left Stick"), "Right Stick" => Loc.T("Right Stick"),
                "Circle Pad" => Loc.T("Circle Pad"), "Nunchuk Stick" => Loc.T("Nunchuk Stick"), "Tilt" => Loc.T("Tilt"),
                "Shake Wiimote" => Loc.T("Shake Wiimote"), "Shake Nunchuk" => Loc.T("Shake Nunchuk"), "Home/Swap screens" => Loc.T("Home/Swap screens"),
                "Enter" => Loc.T("Enter"), "Escape" => Loc.T("Escape"), "Backspace" => Loc.T("Backspace"), "Space" => Loc.T("Space"), "Caps Lock" => Loc.T("Caps Lock"),
                "Left Shift" => Loc.T("Left Shift"), "Right Shift" => Loc.T("Right Shift"), "Left Ctrl" => Loc.T("Left Ctrl"), "Right Ctrl" => Loc.T("Right Ctrl"),
                "Insert" => Loc.T("Insert"), "Delete" => Loc.T("Delete"), "End" => Loc.T("End"),
                "Page Up" => Loc.T("Page Up"), "Page Down" => Loc.T("Page Down"), "Print Screen" => Loc.T("Print Screen"),
                "Menu" => Loc.T("Menu"), "Quit" => Loc.T("Quit"), "Save state" => Loc.T("Save state"), "Load state" => Loc.T("Load state"),
                "Previous slot" => Loc.T("Previous slot"), "Next slot" => Loc.T("Next slot"),
                var other => other,
            };
            return name + arrow;
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
            var controllers = RetroArchManager.Controllers;
            uiControllers.ItemsSource = RetroControllers.BySystem.Select(c => new ControllerRow(c.Key, RetroSystems.All[c.Key].Name, c.Value,
                controllers.TryGetValue(c.Key, out int device) ? device : null)).ToList();
            RefreshKeys();
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

        private void Controller_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not ControllerRow row || sender is not ComboBox box || box.SelectedIndex < 0 || !IsLoaded)
                return;
            var controllers = RetroArchManager.Controllers;
            controllers[row.Slug] = row.Controllers[box.SelectedIndex].Device;
            RetroArchManager.Controllers = controllers;
            RefreshKeys();
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
            if (!await DialogService.ConfirmAsync(Loc.T("Uninstall the emulators, the BIOS and the firmware? The saves are kept."), "RetroArch"))
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
