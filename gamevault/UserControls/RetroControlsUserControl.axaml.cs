using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// Régler mes touches: each console played in RetroArch, its buttons on the gamepad (read with RetroArch's SDL2, see
    /// <see cref="RetroGamepad"/>) or the keyboard, kept by console (<see cref="RetroPad"/>, <see cref="RetroKeyboard"/>), and
    /// the controller of the consoles that have several. Opened from a game's page, its console first and its own controller.
    /// </summary>
    public partial class RetroControlsUserControl : UserControl
    {
        internal sealed record ConsoleChip(RetroSystem System, string Name, bool Selected);
        internal sealed record ControllerChip(int Device, string Name, bool Selected);
        /// <summary>A button of the console and what plays it; the lit one is the one waiting for a key or a button.</summary>
        internal sealed record ControlRow(string Bind, string Name, string Input, string Changed, bool Lit);

        /// <summary>The consoles set here: the ones RetroArch plays.</summary>
        internal static readonly RetroSystem[] Systems = RetroSystems.All.Values.Where(s => RetroKeyboard.Of(s.Slug).Length > 0).ToArray();

        private readonly RetroGame? game;
        private RetroSystem system;
        private bool keyboard;
        /// <summary>The button waiting for a key or a gamepad's button.</summary>
        private string? listening;
        /// <summary>"Set them all": the row being set.</summary>
        private int? guided;
        private string? message;
        private RetroGamepad? gamepad;
        private DispatcherTimer? timer;
        private bool starting;
        private string? gamepadError;

        public RetroControlsUserControl() : this(null) { }

        internal RetroControlsUserControl(RetroGame? game)
        {
            InitializeComponent();
            this.game = game;
            system = game != null && Systems.Contains(game.Platform.System) ? game.Platform.System : Systems[0];
            if (game != null)
                uiSubtitle.Text = Loc.F("Opened from {0}. Set once for each console, used by all its games.", game.Name);
            // On the window, before the buttons: Enter and Space are keys to give, not clicks
            AttachedToVisualTree += (_, _) =>
            {
                TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, Key_Pressed, RoutingStrategies.Tunnel);
                _ = StartGamepadAsync();
            };
            DetachedFromVisualTree += (_, e) =>
            {
                (e.Root as Interactive)?.RemoveHandler(KeyDownEvent, Key_Pressed);
                StopGamepad();
            };
            Refresh();
        }

        private RetroControllers.Controller[] Controllers => RetroControllers.BySystem.GetValueOrDefault(system.Slug) ?? Array.Empty<RetroControllers.Controller>();
        private int? ConsoleDevice => RetroControllers.Pick(system.Slug, null, RetroArchManager.Controllers.TryGetValue(system.Slug, out int device) ? device : null);
        /// <summary>Opened from a game of this console: its own controller counts.</summary>
        private bool ForGame => game != null && game.Platform.Slug == system.Slug;
        private int? Device => ForGame ? RetroArchManager.ControllerOf(game!) : ConsoleDevice;
        private RetroKeyboard.Button[] Buttons => RetroKeyboard.Of(system.Slug, Device);

        internal string? Listening => listening;
        internal bool IsKeyboard => keyboard;
        internal RetroSystem CurrentSystem => system;

        internal void Refresh()
        {
            string slug = system.Slug;
            uiConsoles.ItemsSource = Systems.Select(s => new ConsoleChip(s, s.Name, s == system)).ToList();
            uiPadTab.IsChecked = !keyboard;
            uiKeyboardTab.IsChecked = keyboard;
            uiColumn.Text = keyboard ? Loc.T("YOUR KEYBOARD") : Loc.T("YOUR GAMEPAD");
            RefreshDevice();

            uiControllerRow.IsVisible = Controllers.Length > 0;
            uiControllerLabel.Text = Loc.F("{0} CONTROLLER", system.Name.ToUpperInvariant());
            uiControllers.ItemsSource = Controllers.Select(c => new ControllerChip(c.Device, RetroGameViewModel.ControllerName(c.Name), c.Device == ConsoleDevice)).ToList();
            uiGameController.IsVisible = ForGame;
            if (ForGame)
            {
                uiGameControllerLabel.Text = Loc.F("For {0}:", game!.Name);
                uiGameControllerChoice.ItemsSource = Controllers.Select(c => RetroGameViewModel.ControllerName(c.Name)).Prepend(Loc.T("Like the console")).ToList();
                uiGameControllerChoice.SelectedIndex = RetroArchManager.GameControllers.TryGetValue(game.Id.ToString(), out int chosen) ? Array.FindIndex(Controllers, c => c.Device == chosen) + 1 : 0;
            }

            var buttons = Buttons;
            var keys = RetroArchManager.Keyboard.GetValueOrDefault(slug);
            var pad = RetroArchManager.Pad.GetValueOrDefault(slug);
            int changed = 0;
            var rows = buttons.Select(b =>
            {
                string? label, defaultName, name;
                if (keyboard)
                {
                    var key = RetroKeyboard.KeyOf(keys, b.Bind);
                    (label, name, defaultName) = (key?.Label, key?.Name, RetroKeyboard.Defaults.GetValueOrDefault(b.Bind)?.Name);
                }
                else
                {
                    var input = RetroPad.InputOf(pad, b.Bind);
                    (label, name, defaultName) = (input?.Label, input?.Name, RetroPad.Defaults.GetValueOrDefault(b.Bind)?.Name);
                }
                bool isChanged = (keyboard ? keys != null : pad != null) && name != defaultName && !(name == "nul" && defaultName == null);
                if (isChanged)
                    changed++;
                string shown = b.Bind == listening ? "…" : string.IsNullOrEmpty(label) || name == "nul" ? "—" : Shown(label);
                return new ControlRow(b.Bind, Shown(b.Name), shown, isChanged ? Loc.T("changed") : "", b.Bind == listening);
            }).ToList();
            uiRows.ItemsSource = rows;
            uiDefaults.IsEnabled = keyboard ? keys?.Count > 0 : pad?.Count > 0;
            uiSummary.Text = changed > 0 ? Loc.F("{0} button(s) changed for this console", changed) : Loc.T("RetroArch's default buttons");

            uiDiagram.Kind = PadDiagram.KindOf(slug, Device);
            var current = buttons.FirstOrDefault(b => b.Bind == listening);
            uiDiagram.Highlight = current == null ? null : current.Bind.StartsWith("l_") ? "lstick" : current.Bind.StartsWith("r_") ? "rstick" : current.Bind;
            uiGuidedButtons.IsVisible = guided != null;
            uiCancel.IsVisible = listening != null && guided == null;
            uiGuided.IsEnabled = guided == null;
            if (current == null)
            {
                uiPrompt.Text = "";
                uiHint.Text = message ?? Loc.T("Choose a button on the right, then press its key or its gamepad's button. It lights up on the plan.");
            }
            else
            {
                string name = Shown(current.Name);
                uiPrompt.Text = guided is int step ? Loc.F("Button {0} of {1}: {2}", step + 1, buttons.Length, name)
                    : keyboard ? Loc.F("Press the key for: {0}", name) : Loc.F("Press the gamepad's button for: {0}", name);
                uiHint.Text = message ?? (guided != null ? Loc.T("Escape keeps the current one and goes on.") : Loc.T("Escape or Cancel keeps the current one."));
            }
        }

        private void RefreshDevice()
        {
            uiInstall.IsVisible = !keyboard && !RetroArchManager.Layout.IsInstalled && !starting;
            if (keyboard)
                uiDevice.Text = Loc.T("Your keyboard's keys, as it writes them.");
            else if (starting)
                uiDevice.Text = Loc.T("Getting the gamepads ready...");
            else if (gamepadError != null)
                uiDevice.Text = gamepadError;
            else if (!RetroArchManager.Layout.IsInstalled)
                uiDevice.Text = Loc.T("The gamepad is read with RetroArch, installed with the first retro game.");
            else if (gamepad?.Name is string name)
                uiDevice.Text = gamepad.IsStandard ? Loc.F("Gamepad: {0}", name) : Loc.F("Gamepad: {0} (not a common one: set all its buttons)", name);
            else
                uiDevice.Text = Loc.T("No gamepad plugged: plug one in.");
        }

        #region The gamepad
        private async Task StartGamepadAsync()
        {
            if (gamepad != null || starting || !RetroArchManager.Layout.IsInstalled)
                return;
            starting = true;
            RefreshDevice();
            try
            {
                string library = await RetroArchManager.Sdl2LibraryAsync() ?? throw new InvalidOperationException(Loc.T("RetroArch's SDL2 library was not found"));
                gamepad = new RetroGamepad(library);
                gamepadError = null;
                timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => Tick());
                timer.Start();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                gamepadError = Loc.F("The gamepad can not be read: {0}", ex.Message);
            }
            finally
            {
                starting = false;
                RefreshDevice();
            }
        }

        private void StopGamepad()
        {
            timer?.Stop();
            timer = null;
            gamepad?.Dispose();
            gamepad = null;
        }

        private string? shownDevice;
        private void Tick()
        {
            if (gamepad == null)
                return;
            RetroPad.Input? input = gamepad.Poll();
            if (gamepad.Name != shownDevice)
            {
                shownDevice = gamepad.Name;
                RefreshDevice();
            }
            if (input != null && listening != null && !keyboard)
                Give(listening, input);
        }

        private async void Install_Click(object? sender, RoutedEventArgs e)
        {
            starting = true;
            uiInstall.IsVisible = false;
            try { await RetroArchManager.EnsureRetroArchAsync(text => uiDevice.Text = text); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                gamepadError = Loc.F("RetroArch could not be installed: {0}", ex.Message);
            }
            starting = false;
            await StartGamepadAsync();
        }
        #endregion

        #region Giving a key or a button
        private void Key_Pressed(object? sender, KeyEventArgs e)
        {
            if (listening == null)
                return;
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (guided != null)
                    Next();
                else
                    Stop();
                return;
            }
            if (!keyboard)
                return;
            e.Handled = true;
            if (RetroKeys.Of(e.PhysicalKey, e.KeySymbol) is RetroKeyboard.Key key)
                Give(listening, key);
            else
            {
                message = Loc.T("RetroArch does not read this key: choose another one.");
                Refresh();
            }
        }

        /// <summary>The key for the button waiting for one, unless RetroArch keeps it; the button that had it takes the previous one.</summary>
        internal void Give(string bind, RetroKeyboard.Key key)
        {
            message = null;
            if (RetroKeyboard.Reserved.TryGetValue(key.Name, out string? use))
            {
                message = Loc.F("{0} stays RetroArch's: {1}.", key.Label, Shown(use));
                Refresh();
                return;
            }
            var keyboards = RetroArchManager.Keyboard;
            keyboards[system.Slug] = RetroKeyboard.Assign(system.Slug, Device, keyboards.GetValueOrDefault(system.Slug), bind, key)!;
            RetroArchManager.Keyboard = keyboards;
            Next();
        }

        /// <summary>The gamepad's button for the button waiting for one; the button that had it takes the previous one.</summary>
        internal void Give(string bind, RetroPad.Input input)
        {
            message = null;
            var pads = RetroArchManager.Pad;
            pads[system.Slug] = RetroPad.Assign(system.Slug, Device, pads.GetValueOrDefault(system.Slug), bind, input);
            RetroArchManager.Pad = pads;
            Next();
        }

        /// <summary>No key or button for this one ("Set them all": a button the player does not use).</summary>
        private void GiveNone(string bind)
        {
            if (keyboard)
            {
                var keyboards = RetroArchManager.Keyboard;
                var chosen = keyboards.GetValueOrDefault(system.Slug);
                var keys = Buttons.Select(b => (b.Bind, Key: RetroKeyboard.KeyOf(chosen, b.Bind))).Where(k => k.Key != null).ToDictionary(k => k.Bind, k => k.Key!);
                keys[bind] = RetroKeyboard.None;
                keyboards[system.Slug] = keys;
                RetroArchManager.Keyboard = keyboards;
            }
            else
            {
                var pads = RetroArchManager.Pad;
                var chosen = pads.GetValueOrDefault(system.Slug);
                var inputs = Buttons.Select(b => (b.Bind, Input: RetroPad.InputOf(chosen, b.Bind))).Where(i => i.Input != null).ToDictionary(i => i.Bind, i => i.Input!);
                inputs[bind] = RetroPad.None;
                pads[system.Slug] = inputs;
                RetroArchManager.Pad = pads;
            }
            Next();
        }

        /// <summary>Done with this button: the next one in "Set them all", else none waits.</summary>
        private void Next()
        {
            if (guided is int step && step + 1 < Buttons.Length)
                Listen(step + 1);
            else
            {
                if (guided != null)
                    message = Loc.T("All the buttons are set.");
                Stop(keepMessage: true);
            }
        }

        private void Listen(int? step, string? bind = null)
        {
            guided = step;
            listening = step is int i ? Buttons[i].Bind : bind;
            gamepad?.Rest();
            Refresh();
        }

        private void Stop(bool keepMessage = false)
        {
            guided = null;
            listening = null;
            if (!keepMessage)
                message = null;
            Refresh();
        }
        #endregion

        #region Clicks
        private void Console_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ConsoleChip chip)
                system = chip.System;
            Stop();
        }

        private void Source_Click(object? sender, RoutedEventArgs e)
        {
            keyboard = sender == uiKeyboardTab;
            Stop();
            if (!keyboard)
                _ = StartGamepadAsync();
        }

        private void Controller_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not ControllerChip chip)
                return;
            var controllers = RetroArchManager.Controllers;
            controllers[system.Slug] = chip.Device;
            RetroArchManager.Controllers = controllers;
            Stop();
        }

        private void GameController_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (!ForGame || uiGameControllerChoice.SelectedIndex < 0 || !IsLoaded)
                return;
            var games = RetroArchManager.GameControllers;
            if (uiGameControllerChoice.SelectedIndex == 0)
                games.Remove(game!.Id.ToString());
            else
                games[game!.Id.ToString()] = Controllers[uiGameControllerChoice.SelectedIndex - 1].Device;
            if (RetroControllers.Format(games) == RetroControllers.Format(RetroArchManager.GameControllers))
                return;
            RetroArchManager.GameControllers = games;
            Stop();
        }

        private void Change_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ControlRow row)
            {
                message = null;
                Listen(null, row.Bind == listening ? null : row.Bind);
            }
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Stop();
        private void Guided_Click(object? sender, RoutedEventArgs e)
        {
            message = null;
            Listen(0);
        }
        private void GuidedBack_Click(object? sender, RoutedEventArgs e) => Listen(Math.Max(0, (guided ?? 0) - 1));
        private void GuidedKeep_Click(object? sender, RoutedEventArgs e) => Next();
        private void GuidedNone_Click(object? sender, RoutedEventArgs e)
        {
            if (listening != null)
                GiveNone(listening);
        }
        private void GuidedStop_Click(object? sender, RoutedEventArgs e) => Stop();

        private void Defaults_Click(object? sender, RoutedEventArgs e)
        {
            if (keyboard)
            {
                var keyboards = RetroArchManager.Keyboard;
                keyboards.Remove(system.Slug);
                RetroArchManager.Keyboard = keyboards;
            }
            else
            {
                var pads = RetroArchManager.Pad;
                pads.Remove(system.Slug);
                RetroArchManager.Pad = pads;
            }
            Stop();
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
        #endregion

        /// <summary>A button's, a key's or a gamepad input's name in the player's language; a letter or a console's own name ("L1", "+") stays.</summary>
        internal static string Shown(string text)
        {
            string arrow = text.Length > 2 && text[^2] == ' ' && "↑↓←→".Contains(text[^1]) ? text[^2..] : "";
            string name = text[..^arrow.Length];
            if (name.StartsWith("Button ") && int.TryParse(name[7..], out int button))
                return Loc.F("Button {0}", button) + arrow;
            if (name.StartsWith("Axis "))
                return Loc.F("Axis {0}", name[5..]) + arrow;
            if (name.StartsWith("Hat ") && int.TryParse(name[4..], out int hat))
                return Loc.F("Hat {0}", hat) + arrow;
            return name switch
            {
                "Up" => Loc.T("Up"), "Down" => Loc.T("Down"), "Left" => Loc.T("Left"), "Right" => Loc.T("Right"),
                "Cross" => Loc.T("Cross"), "Circle" => Loc.T("Circle"), "Square" => Loc.T("Square"), "Coin" => Loc.T("Coin"),
                "Control Stick" => Loc.T("Control Stick"), "C Stick" => Loc.T("C Stick"), "Left Stick" => Loc.T("Left Stick"), "Right Stick" => Loc.T("Right Stick"),
                "Circle Pad" => Loc.T("Circle Pad"), "Nunchuk Stick" => Loc.T("Nunchuk Stick"), "Tilt" => Loc.T("Tilt"), "D-Pad" => Loc.T("D-Pad"),
                "Shake Wiimote" => Loc.T("Shake Wiimote"), "Shake Nunchuk" => Loc.T("Shake Nunchuk"), "Home/Swap screens" => Loc.T("Home/Swap screens"),
                "Enter" => Loc.T("Enter"), "Escape" => Loc.T("Escape"), "Backspace" => Loc.T("Backspace"), "Space" => Loc.T("Space"), "Caps Lock" => Loc.T("Caps Lock"),
                "Left Shift" => Loc.T("Left Shift"), "Right Shift" => Loc.T("Right Shift"), "Left Ctrl" => Loc.T("Left Ctrl"), "Right Ctrl" => Loc.T("Right Ctrl"),
                "Insert" => Loc.T("Insert"), "Delete" => Loc.T("Delete"), "End" => Loc.T("End"),
                "Page Up" => Loc.T("Page Up"), "Page Down" => Loc.T("Page Down"), "Print Screen" => Loc.T("Print Screen"),
                "Menu" => Loc.T("Menu"), "Quit" => Loc.T("Quit"), "Save state" => Loc.T("Save state"), "Load state" => Loc.T("Load state"),
                "Previous slot" => Loc.T("Previous slot"), "Next slot" => Loc.T("Next slot"),
                var other => other,
            } + arrow;
        }
    }
}
