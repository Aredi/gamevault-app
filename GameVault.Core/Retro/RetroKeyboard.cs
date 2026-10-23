using System.Text.Json;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// The keyboard of a console's games (Settings > Retro): RetroArch's keys of the first player's RetroPad, written in the
    /// client's config before each game, by platform. RetroArch reads a key by its place on the keyboard (DirectInput's and
    /// X11's codes), named as on a US keyboard ("z"); checked in its sources (1.22.2), as the cores' names of their buttons.
    /// </summary>
    public static class RetroKeyboard
    {
        /// <summary>A button of the console: the RetroPad's input it is in RetroArch's config ("a", "l_x_minus") and its name on the console.</summary>
        public sealed record Button(string Bind, string Name);

        /// <summary>A key: RetroArch's name of its place ("z") and what it shows on the player's keyboard ("W").</summary>
        public sealed record Key(string Name, string Label);

        /// <summary>RetroArch's keys when none is set (config.def.keybinds.h), named as on a US keyboard.</summary>
        public static readonly IReadOnlyDictionary<string, Key> Defaults = new Dictionary<string, Key>
        {
            ["up"] = new("up", "↑"), ["down"] = new("down", "↓"), ["left"] = new("left", "←"), ["right"] = new("right", "→"),
            // A letter is where it is on a QWERTY keyboard: an AZERTY's Q is RetroArch's "a"
            ["a"] = new("x", "X (QWERTY)"), ["b"] = new("z", "Z (QWERTY)"), ["x"] = new("s", "S (QWERTY)"), ["y"] = new("a", "A (QWERTY)"),
            ["l"] = new("q", "Q (QWERTY)"), ["r"] = new("w", "W (QWERTY)"), ["start"] = new("enter", "Enter"), ["select"] = new("rshift", "Right Shift"),
        };

        /// <summary>The keys of RetroArch's own menu, quitting and save states: kept for them.</summary>
        public static readonly IReadOnlyDictionary<string, string> Reserved = new Dictionary<string, string>
        {
            ["f1"] = "Menu", ["escape"] = "Quit", ["f2"] = "Save state", ["f4"] = "Load state", ["f6"] = "Previous slot", ["f7"] = "Next slot",
        };

        /// <summary>RetroArch's other shortcuts (no hotkey is needed to use them): one a button takes is turned off.</summary>
        private static readonly IReadOnlyDictionary<string, string> Shortcuts = new Dictionary<string, string>
        {
            ["h"] = "reset", ["space"] = "toggle_fast_forward", ["l"] = "hold_fast_forward", ["e"] = "hold_slowmotion", ["r"] = "rewind",
            ["p"] = "pause_toggle", ["k"] = "frame_advance", ["f9"] = "audio_mute", ["kp_plus"] = "volume_up", ["kp_minus"] = "volume_down",
            ["comma"] = "shader_toggle", ["m"] = "shader_next", ["n"] = "shader_prev", ["u"] = "cheat_toggle", ["y"] = "cheat_index_plus",
            ["t"] = "cheat_index_minus", ["f8"] = "screenshot", ["f11"] = "grab_mouse_toggle", ["scroll_lock"] = "game_focus_toggle",
            ["f"] = "toggle_fullscreen", ["f5"] = "desktop_menu_toggle", ["f3"] = "fps_toggle", ["i"] = "netplay_game_watch", ["backquote"] = "netplay_player_chat",
        };

        private static Button[] Pad => new Button[] { new("up", "Up"), new("down", "Down"), new("left", "Left"), new("right", "Right") };
        private static Button[] Stick(string side, string name) => new Button[]
        {
            new($"{side}_y_minus", $"{name} ↑"), new($"{side}_y_plus", $"{name} ↓"), new($"{side}_x_minus", $"{name} ←"), new($"{side}_x_plus", $"{name} →"),
        };
        private static Button[] Buttons(params (string Bind, string Name)[] buttons) => buttons.Select(b => new Button(b.Bind, b.Name)).ToArray();

        private static readonly Button[] GameCube = Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("x", "X"), ("y", "Y"), ("r", "Z"), ("l2", "L"), ("r2", "R"), ("start", "Start")))
            .Concat(Stick("l", "Control Stick")).Concat(Stick("r", "C Stick")).ToArray();
        private static readonly Button[] PlayStation = Pad.Concat(Buttons(("b", "Cross"), ("a", "Circle"), ("y", "Square"), ("x", "Triangle"), ("l", "L1"), ("r", "R1"),
            ("l2", "L2"), ("r2", "R2"), ("l3", "L3"), ("r3", "R3"), ("select", "Select"), ("start", "Start"))).Concat(Stick("l", "Left Stick")).Concat(Stick("r", "Right Stick")).ToArray();

        /// <summary>
        /// The buttons of a platform played in RetroArch, as its core names them (the Wii's by its controller, see
        /// <see cref="RetroControllers"/>); none for the others.
        /// </summary>
        public static Button[] Of(string slug, int? device = null) => slug switch
        {
            "nes" => Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("select", "Select"), ("start", "Start"))).ToArray(),
            "snes" or "nds" => Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("x", "X"), ("y", "Y"), ("l", "L"), ("r", "R"), ("select", "Select"), ("start", "Start"))).ToArray(),
            "gba" => Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("l", "L"), ("r", "R"), ("select", "Select"), ("start", "Start"))).ToArray(),
            "3ds" => Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("x", "X"), ("y", "Y"), ("l", "L"), ("r", "R"), ("l2", "ZL"), ("r2", "ZR"), ("select", "Select"), ("start", "Start"), ("l3", "Home/Swap screens")))
                .Concat(Stick("l", "Circle Pad")).Concat(Stick("r", "C-Stick")).ToArray(),
            "ngc" => GameCube,
            "wii" => (device ?? RetroControllers.BySystem["wii"][0].Device) switch
            {
                1 => Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("x", "1"), ("y", "2"), ("start", "+"), ("select", "-"), ("r3", "Home"), ("r2", "Shake Wiimote"))).Concat(Stick("l", "Tilt")).ToArray(),
                513 => Pad.Concat(Buttons(("x", "A"), ("y", "B"), ("b", "1"), ("a", "2"), ("start", "+"), ("select", "-"), ("r3", "Home"), ("r2", "Shake Wiimote"))).Concat(Stick("l", "Tilt")).ToArray(),
                1025 => Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("x", "X"), ("y", "Y"), ("l2", "L"), ("r2", "R"), ("l", "ZL"), ("r", "ZR"), ("start", "+"), ("select", "-"), ("r3", "Home")))
                    .Concat(Stick("l", "Left Stick")).Concat(Stick("r", "Right Stick")).ToArray(),
                1537 => GameCube,
                // The Nunchuk's
                _ => Pad.Concat(Buttons(("a", "A"), ("b", "B"), ("x", "C"), ("y", "Z"), ("r", "+"), ("l", "-"), ("start", "1"), ("select", "2"), ("r3", "Home"), ("r2", "Shake Wiimote"), ("l2", "Shake Nunchuk")))
                    .Concat(Stick("l", "Nunchuk Stick")).Concat(Stick("r", "Tilt")).ToArray(),
            },
            "psx" or "ps2" => PlayStation,
            // FinalBurn Neo's classic RetroPad
            "neogeoaes" => Pad.Concat(Buttons(("b", "A"), ("a", "B"), ("y", "C"), ("x", "D"), ("select", "Coin"), ("start", "Start"))).ToArray(),
            _ => Array.Empty<Button>(),
        };

        /// <summary>The key of a button: the one chosen for the platform, else RetroArch's, else none.</summary>
        public static Key? KeyOf(IReadOnlyDictionary<string, Key>? chosen, string bind) =>
            chosen != null && chosen.TryGetValue(bind, out Key? key) ? key : Defaults.GetValueOrDefault(bind);

        /// <summary>
        /// A key given to a button: the button that had it takes the button's previous key (or none), so no key plays two
        /// buttons. Null when RetroArch keeps it (<see cref="Reserved"/>).
        /// </summary>
        public static Dictionary<string, Key>? Assign(string slug, int? device, IReadOnlyDictionary<string, Key>? chosen, string bind, Key key)
        {
            if (Reserved.ContainsKey(key.Name))
                return null;
            var buttons = Of(slug, device);
            var keys = buttons.ToDictionary(b => b.Bind, b => KeyOf(chosen, b.Bind));
            Key? previous = keys.GetValueOrDefault(bind);
            foreach (var other in keys.Where(k => k.Key != bind && k.Value?.Name == key.Name).ToList())
                keys[other.Key] = previous ?? None;
            keys[bind] = key;
            return keys.Where(k => k.Value != null).ToDictionary(k => k.Key, k => k.Value!);
        }

        /// <summary>No key ("nul" in RetroArch's config).</summary>
        public static readonly Key None = new("nul", "");

        /// <summary>
        /// The lines of RetroArch's config for a platform's game: its buttons' keys, the RetroPad's other buttons without one
        /// (a key of RetroArch's own does not play a button the core does not show), the shortcuts on these keys off. None
        /// when nothing is chosen for it: RetroArch's keys, and the player's own override, stay.
        /// </summary>
        public static IEnumerable<(string Key, string Value)> Config(string slug, int? device, IReadOnlyDictionary<string, Key>? chosen)
        {
            if (chosen == null || chosen.Count == 0)
                yield break;
            var buttons = Of(slug, device);
            foreach (Button button in buttons)
                yield return ($"input_player1_{button.Bind}", (KeyOf(chosen, button.Bind) ?? None).Name);
            foreach (string bind in Defaults.Keys.Where(b => buttons.All(button => button.Bind != b)))
                yield return ($"input_player1_{bind}", None.Name);
            var used = buttons.Select(b => KeyOf(chosen, b.Bind)?.Name).ToHashSet();
            foreach (var shortcut in Shortcuts.Where(s => used.Contains(s.Key)))
                yield return ($"input_{shortcut.Value}", None.Name);
        }

        /// <summary>The choices as kept in the profile, by platform: {"snes":{"a":{"Name":"x","Label":"X"}}}.</summary>
        public static Dictionary<string, Dictionary<string, Key>> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Key>>>(text) ?? new(); }
            catch (JsonException) { return new(); }
        }

        public static string Format(IReadOnlyDictionary<string, Dictionary<string, Key>> choices) =>
            JsonSerializer.Serialize(choices.Where(c => c.Value.Count > 0).OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value));
    }
}
