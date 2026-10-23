using Avalonia.Input;
using GameVault.Core.Retro;
using System.Collections.Generic;

namespace gamevault.Helper
{
    /// <summary>
    /// A key pressed in the client as RetroArch names it: by its place on the keyboard (RetroArch reads DirectInput's and
    /// X11's codes, places too), shown as the player's keyboard writes it (an AZERTY's A is RetroArch's "q").
    /// </summary>
    internal static class RetroKeys
    {
        private static readonly Dictionary<PhysicalKey, (string Name, string Label)> Named = new()
        {
            [PhysicalKey.Enter] = ("enter", "Enter"), [PhysicalKey.Escape] = ("escape", "Escape"), [PhysicalKey.Backspace] = ("backspace", "Backspace"),
            [PhysicalKey.Tab] = ("tab", "Tab"), [PhysicalKey.Space] = ("space", "Space"), [PhysicalKey.CapsLock] = ("capslock", "Caps Lock"),
            [PhysicalKey.ShiftLeft] = ("shift", "Left Shift"), [PhysicalKey.ShiftRight] = ("rshift", "Right Shift"),
            [PhysicalKey.ControlLeft] = ("ctrl", "Left Ctrl"), [PhysicalKey.ControlRight] = ("rctrl", "Right Ctrl"),
            [PhysicalKey.AltLeft] = ("alt", "Alt"), [PhysicalKey.AltRight] = ("ralt", "Alt Gr"),
            [PhysicalKey.Insert] = ("insert", "Insert"), [PhysicalKey.Delete] = ("del", "Delete"), [PhysicalKey.Home] = ("home", "Home"),
            [PhysicalKey.End] = ("end", "End"), [PhysicalKey.PageUp] = ("pageup", "Page Up"), [PhysicalKey.PageDown] = ("pagedown", "Page Down"),
            [PhysicalKey.ArrowUp] = ("up", "↑"), [PhysicalKey.ArrowDown] = ("down", "↓"), [PhysicalKey.ArrowLeft] = ("left", "←"), [PhysicalKey.ArrowRight] = ("right", "→"),
            [PhysicalKey.NumLock] = ("numlock", "Num Lock"), [PhysicalKey.NumPadDivide] = ("divide", "Num /"), [PhysicalKey.NumPadMultiply] = ("multiply", "Num *"),
            [PhysicalKey.NumPadSubtract] = ("kp_minus", "Num -"), [PhysicalKey.NumPadAdd] = ("kp_plus", "Num +"), [PhysicalKey.NumPadEnter] = ("kp_enter", "Num Enter"),
            [PhysicalKey.NumPadDecimal] = ("kp_period", "Num ."), [PhysicalKey.NumPadEqual] = ("kp_equals", "Num ="),
            [PhysicalKey.PrintScreen] = ("print_screen", "Print Screen"), [PhysicalKey.ScrollLock] = ("scroll_lock", "Scroll Lock"), [PhysicalKey.Pause] = ("pause", "Pause"),
        };

        // The keys that write a character: named by their place on a US keyboard
        private static readonly Dictionary<PhysicalKey, string> Characters = new()
        {
            [PhysicalKey.Minus] = "minus", [PhysicalKey.Equal] = "equals", [PhysicalKey.BracketLeft] = "leftbracket", [PhysicalKey.BracketRight] = "rightbracket",
            [PhysicalKey.Backslash] = "backslash", [PhysicalKey.Semicolon] = "semicolon", [PhysicalKey.Quote] = "quote", [PhysicalKey.Backquote] = "backquote",
            [PhysicalKey.Comma] = "comma", [PhysicalKey.Period] = "period", [PhysicalKey.Slash] = "slash", [PhysicalKey.IntlBackslash] = "oem102",
        };

        /// <param name="symbol">What the key writes on the player's keyboard (<see cref="KeyEventArgs.KeySymbol"/>), its label.</param>
        /// <returns>Null for a key RetroArch does not read.</returns>
        public static RetroKeyboard.Key? Of(PhysicalKey key, string? symbol)
        {
            if (Named.TryGetValue(key, out var named))
                return new(named.Name, named.Label);
            string text = key.ToString();
            string? name = Characters.GetValueOrDefault(key)
                ?? (text.Length == 1 ? text.ToLowerInvariant()
                : text.StartsWith("Digit") ? "num" + text[5..]
                : text.StartsWith("NumPad") && text.Length == 7 && char.IsDigit(text[6]) ? "keypad" + text[6..]
                : text[0] == 'F' && int.TryParse(text[1..], out int f) && f is >= 1 and <= 15 ? text.ToLowerInvariant()
                : null);
            if (name == null)
                return null;
            string label = text.StartsWith("NumPad") ? "Num " + text[6..]
                : text[0] == 'F' && text.Length > 1 && char.IsDigit(text[1]) ? text
                : !string.IsNullOrWhiteSpace(symbol) && !char.IsControl(symbol[0]) ? symbol.ToUpperInvariant()
                : text.Length == 1 ? text : text.StartsWith("Digit") ? text[5..] : name;
            return new(name, label);
        }
    }
}
