using System.Text.Json;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// The gamepad's buttons of a console's games (Régler mes touches): RetroArch's first player's RetroPad, by platform, the
    /// RetroPad's buttons being the ones of <see cref="RetroKeyboard.Of"/>. A console set here is played with RetroArch's
    /// SDL2 driver, the one the client reads the gamepad with (SDL's numbers, the same in both); the others keep RetroArch's
    /// own driver and profiles. Checked in RetroArch's sources and its SDL2 profiles (1.22.2).
    /// </summary>
    public static class RetroPad
    {
        /// <summary>
        /// An input of the gamepad as RetroArch's config writes it: a button ("0"), an axis and its side ("+4", "-1") or a
        /// hat's direction ("h0up"), and its name.
        /// </summary>
        public sealed record Input(string Name, string Label)
        {
            [System.Text.Json.Serialization.JsonIgnore]
            public bool IsAxis => Name.Length > 1 && Name[0] is '+' or '-';
        }

        /// <summary>No input.</summary>
        public static readonly Input None = new("nul", "");

        // SDL's game controller: its buttons and axes
        private static readonly string[] ButtonLabels =
            { "A", "B", "X", "Y", "View", "Guide", "Menu", "L3", "R3", "LB", "RB", "D-Pad ↑", "D-Pad ↓", "D-Pad ←", "D-Pad →" };

        /// <summary>A button of SDL's game controller ("0" is A, the bottom one): what RetroArch's SDL2 profiles give.</summary>
        public static Input Button(int index, bool standard = true) =>
            new(index.ToString(), standard && index < ButtonLabels.Length ? ButtonLabels[index] : $"Button {index}");

        public static Input Axis(int axis, bool positive, bool standard = true)
        {
            string arrowX = positive ? "→" : "←", arrowY = positive ? "↓" : "↑";
            string label = !standard ? $"Axis {axis}{(positive ? '+' : '-')}" : axis switch
            {
                0 => $"Left Stick {arrowX}", 1 => $"Left Stick {arrowY}", 2 => $"Right Stick {arrowX}", 3 => $"Right Stick {arrowY}",
                4 => "LT", 5 => "RT", _ => $"Axis {axis}{(positive ? '+' : '-')}",
            };
            return new($"{(positive ? '+' : '-')}{axis}", label);
        }

        /// <summary>A hat's direction, a raw joystick's cross ("h0up").</summary>
        public static Input Hat(int hat, string direction) => new($"h{hat}{direction}", $"Hat {hat} " + direction switch { "up" => "↑", "down" => "↓", "left" => "←", _ => "→" });

        /// <summary>RetroArch's SDL2 profiles of a game controller (Xbox, PlayStation, Switch alike): the RetroPad's buttons there.</summary>
        public static readonly IReadOnlyDictionary<string, Input> Defaults = new Dictionary<string, Input>
        {
            ["b"] = Button(0), ["a"] = Button(1), ["y"] = Button(2), ["x"] = Button(3), ["select"] = Button(4), ["start"] = Button(6),
            ["l3"] = Button(7), ["r3"] = Button(8), ["l"] = Button(9), ["r"] = Button(10),
            ["up"] = Button(11), ["down"] = Button(12), ["left"] = Button(13), ["right"] = Button(14),
            ["l2"] = Axis(4, true), ["r2"] = Axis(5, true),
            ["l_x_plus"] = Axis(0, true), ["l_x_minus"] = Axis(0, false), ["l_y_plus"] = Axis(1, true), ["l_y_minus"] = Axis(1, false),
            ["r_x_plus"] = Axis(2, true), ["r_x_minus"] = Axis(2, false), ["r_y_plus"] = Axis(3, true), ["r_y_minus"] = Axis(3, false),
        };

        /// <summary>The input of a button: the one chosen for the platform, else the profiles'.</summary>
        public static Input? InputOf(IReadOnlyDictionary<string, Input>? chosen, string bind) =>
            chosen != null && chosen.TryGetValue(bind, out Input? input) ? input : Defaults.GetValueOrDefault(bind);

        /// <summary>An input given to a button: the button that had it takes the button's previous one, so no input plays two buttons.</summary>
        public static Dictionary<string, Input> Assign(string slug, int? device, IReadOnlyDictionary<string, Input>? chosen, string bind, Input input)
        {
            var inputs = RetroKeyboard.Of(slug, device).ToDictionary(b => b.Bind, b => InputOf(chosen, b.Bind));
            Input? previous = inputs.GetValueOrDefault(bind);
            foreach (var other in inputs.Where(i => i.Key != bind && i.Value?.Name == input.Name).ToList())
                inputs[other.Key] = previous ?? None;
            inputs[bind] = input;
            return inputs.Where(i => i.Value != null).ToDictionary(i => i.Key, i => i.Value!);
        }

        /// <summary>
        /// The lines of RetroArch's config for a platform's game, none when nothing is chosen for it. Else SDL2, every RetroPad
        /// button written (RetroArch's profile would play a button left empty: it is not read): the console's, and the
        /// others as in the profiles unless one of the console's has their input (Select and Start stay: Select+Start quits).
        /// </summary>
        public static IEnumerable<(string Key, string Value)> Config(string slug, int? device, IReadOnlyDictionary<string, Input>? chosen)
        {
            if (chosen == null || chosen.Count == 0)
                yield break;
            yield return ("input_joypad_driver", "sdl2");
            yield return ("input_autodetect_enable", "false");
            var buttons = RetroKeyboard.Of(slug, device).Select(b => b.Bind).ToList();
            var inputs = buttons.ToDictionary(b => b, b => InputOf(chosen, b) ?? None);
            var used = inputs.Values.Select(i => i.Name).ToHashSet();
            foreach (var (bind, input) in Defaults.Where(d => !buttons.Contains(d.Key)))
                inputs[bind] = bind is "select" or "start" || !used.Contains(input.Name) ? input : None;
            foreach (var (bind, input) in inputs)
            {
                yield return ($"input_player1_{bind}_btn", input.IsAxis ? None.Name : input.Name);
                yield return ($"input_player1_{bind}_axis", input.IsAxis ? input.Name : None.Name);
            }
        }

        /// <summary>The choices as kept in the profile, by platform: {"snes":{"a":{"Name":"3","Label":"Y"}}}.</summary>
        public static Dictionary<string, Dictionary<string, Input>> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Input>>>(text) ?? new(); }
            catch (JsonException) { return new(); }
        }

        public static string Format(IReadOnlyDictionary<string, Dictionary<string, Input>> choices) =>
            JsonSerializer.Serialize(choices.Where(c => c.Value.Count > 0).OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value));
    }
}
