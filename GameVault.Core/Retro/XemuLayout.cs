namespace GameVault.Core.Retro
{
    /// <summary>
    /// xemu plays the Xbox games: an XISO in its drive (see <see cref="XboxDisc"/>), with the console's boot ROM and BIOS
    /// from RomM and xemu's own hard disk, free of copyrighted content (its dashboard). Each GameVault player has their
    /// hard disk, where the games keep their saves. Its settings file is found by xemu itself: beside it on Windows
    /// (its portable mode), in the folder the client gives it on Linux (XDG_DATA_HOME); a Mac's is given at each start.
    /// </summary>
    public sealed class XemuLayout(string directory, bool windows, bool mac = false) : StandaloneLayout(directory, windows, mac)
    {
        private const string Release = "0.8.136";
        /// <summary>xemu's hard disk with its dashboard (8 GB, nearly empty: 1.6 MB), copied for each player.</summary>
        public const string HardDiskUrl = "https://github.com/xemu-project/xemu-dashboard/releases/download/v20260516-0955/xbox_hdd.qcow2";

        public override string Version => Release;
        public override string PackageUrl => Windows
            ? $"https://github.com/xemu-project/xemu/releases/download/v{Release}/xemu-{Release}-windows-x86_64.zip"
            : Mac ? $"https://github.com/xemu-project/xemu/releases/download/v{Release}/xemu-{Release}-macos-universal.zip"
            : $"https://github.com/xemu-project/xemu/releases/download/v{Release}/xemu-{Release}-x86_64.AppImage";
        protected override string WindowsExecutable => "xemu.exe";
        protected override string MacExecutable => Path.Combine("xemu.app", "Contents", "MacOS", "xemu");

        /// <summary>On Linux, its settings and caches go in its folder, not the player's home.</summary>
        public override IReadOnlyDictionary<string, string> Environment => Windows || Mac ? new Dictionary<string, string>() : new Dictionary<string, string>
        {
            ["XDG_DATA_HOME"] = Path.Combine(Directory, "data"),
        };
        /// <summary>Where xemu looks for it: beside xemu.exe, else SDL's folder of the app ("xemu", "xemu"); on a Mac (whose folder is in ~/Library) the one given to it.</summary>
        public string ConfigFile => Windows || Mac ? Path.Combine(Directory, "xemu.toml") : Path.Combine(Directory, "data", "xemu", "xemu", "xemu.toml");
        public override IReadOnlyList<string> StartArguments => Mac ? new[] { "-config_path", ConfigFile } : Array.Empty<string>();

        private string SystemFolder => Path.Combine(Directory, "system");
        /// <summary>The MCPX boot ROM: xemu takes only one of 512 bytes.</summary>
        public string BootRom => Path.Combine(SystemFolder, "mcpx_1.0.bin");
        public const int BootRomSize = 512;
        /// <summary>The BIOS: a modified one (Complex 4627), a retail one starts no game in xemu. 256 KB or 1 MB.</summary>
        public string FlashRom => Path.Combine(SystemFolder, "bios.bin");
        public static bool IsFlashRomSize(long size) => size is 256 * 1024 or 1024 * 1024;
        /// <summary>The console's settings (its language, its video), made by xemu at the first start.</summary>
        public string Eeprom => Path.Combine(SystemFolder, "eeprom.bin");
        /// <summary>The empty hard disk the players' are copied from.</summary>
        public string BaseHardDisk => Path.Combine(SystemFolder, "xbox_hdd.qcow2");

        public override bool HasFirmware => File.Exists(BootRom) && File.Exists(FlashRom) && File.Exists(BaseHardDisk);

        private string HardDisks => Path.Combine(Directory, "hdd");
        private static int PlayerNumber(string player) => int.TryParse(player, out int id) && id > 0 ? id : 1;
        /// <summary>The player's hard disk: their saves of all the games, and xemu's save states.</summary>
        public string HardDisk(string player) => Path.Combine(HardDisks, $"player-{PlayerNumber(player)}.qcow2");

        /// <summary>
        /// shortcut: the saves stay in the player's hard disk on this PC, not sent to the cloud (a folder never written):
        /// a game's saves are files inside its FATX partition, to read from the image before they can be sent alone.
        /// </summary>
        public override string SavesDirectory(string player) => Path.Combine(Directory, "saves");
        /// <summary>xemu keeps its save states in the hard disk: a folder never written.</summary>
        public override string StatesDirectory => Path.Combine(Directory, "states");

        /// <summary>The players' hard disks, and the console's settings.</summary>
        public override IEnumerable<string> KeptPaths => new[] { HardDisks, Eeprom };

        public override IEnumerable<string> SettingsFiles => new[] { ConfigFile };

        /// <summary>A TOML string: xemu reads the paths of Windows with their backslashes.</summary>
        private static string Text(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>
        /// The console's files, no welcome (which also keeps the game from starting), no boot animation and no update
        /// check (the client keeps the version it knows). xemu writes its file back as "key = value" in [tables].
        /// </summary>
        public override string Settings(string existing)
        {
            string config = MergeIni(existing, "general", new[] { ("show_welcome", "false"), ("skip_boot_anim", "true") }, " = ");
            config = MergeIni(config, "general.updates", new[] { ("check", "false") }, " = ");
            return MergeIni(config, "sys.files", new[] { ("bootrom_path", Text(BootRom)), ("flashrom_path", Text(FlashRom)), ("eeprom_path", Text(Eeprom)) }, " = ");
        }

        /// <summary>The player's hard disk in the console.</summary>
        public string WithHardDisk(string existing, string player) => MergeIni(existing, "sys.files", new[] { ("hdd_path", Text(HardDisk(player))) }, " = ");

        public override IReadOnlyList<string> Arguments(string player, string launchFile) => new[] { "-dvd_path", launchFile };

        public override IEnumerable<string> ResolutionFiles => SettingsFiles;

        /// <summary>Its [display.quality] surface_scale: a multiple of the console's 480 lines.</summary>
        public override string Resolution(string existing, int scale) => MergeIni(existing, "display.quality", new[] { ("surface_scale", scale.ToString()) }, " = ");

        /// <summary>xemu's Xbox buttons and the RetroPad's they are set as (<see cref="RetroKeyboard.Of"/>): its keyboard's names.</summary>
        private static readonly (string Xemu, string Bind)[] KeyboardButtons =
        {
            ("a", "b"), ("b", "a"), ("x", "y"), ("y", "x"), ("white", "l"), ("black", "r"), ("back", "select"), ("start", "start"),
            ("lstick_btn", "l3"), ("rstick_btn", "r3"), ("dpad_up", "up"), ("dpad_down", "down"), ("dpad_left", "left"), ("dpad_right", "right"),
            ("ltrigger", "l2"), ("rtrigger", "r2"), ("lstick_up", "l_y_minus"), ("lstick_down", "l_y_plus"), ("lstick_left", "l_x_minus"), ("lstick_right", "l_x_plus"),
            ("rstick_up", "r_y_minus"), ("rstick_down", "r_y_plus"), ("rstick_left", "r_x_minus"), ("rstick_right", "r_x_plus"),
        };

        /// <summary>The gamepad's: SDL's game controller numbers, as the client reads them (<see cref="RetroPad"/>); White and Black are its shoulders.</summary>
        private static readonly (string Xemu, string Bind)[] PadButtons =
        {
            ("a", "b"), ("b", "a"), ("x", "y"), ("y", "x"), ("back", "select"), ("start", "start"), ("lstick_btn", "l3"), ("rstick_btn", "r3"),
            ("lshoulder", "l"), ("rshoulder", "r"), ("dpad_up", "up"), ("dpad_down", "down"), ("dpad_left", "left"), ("dpad_right", "right"),
        };

        /// <summary>
        /// The gamepad's axes: a stick's by its → and ↓ (its other side inverted, an axis pushed back).
        /// shortcut: a stick's ← and ↑ given another axis than its → and ↓ are not read; xemu takes one axis a side, upgrade if players ask.
        /// </summary>
        private static readonly (string Xemu, string Bind, string? Invert)[] PadAxes =
        {
            ("axis_trigger_left", "l2", null), ("axis_trigger_right", "r2", null),
            ("axis_left_x", "l_x_plus", "invert_axis_left_x"), ("axis_left_y", "l_y_plus", "invert_axis_left_y"),
            ("axis_right_x", "r_x_plus", "invert_axis_right_x"), ("axis_right_y", "r_y_plus", "invert_axis_right_y"),
        };

        /// <summary>A button xemu plays with a stick or a trigger of the gamepad, not a button.</summary>
        public static bool TakesAxis(string bind) => PadAxes.Any(a => a.Bind == bind) || bind.Length > 2 && bind[1] == '_';

        /// <summary>SDL's scancodes (the USB keyboard's codes, xemu's) of RetroArch's names of the keys' places (<see cref="RetroKeyboard.Key"/>).</summary>
        internal static int? Scancode(string name) => name switch
        {
            { Length: 1 } when name[0] is >= 'a' and <= 'z' => 4 + name[0] - 'a',
            "num0" => 39,
            _ when name.StartsWith("num") && name.Length == 4 && name[3] is >= '1' and <= '9' => 30 + name[3] - '1',
            "keypad0" => 98,
            _ when name.StartsWith("keypad") && name.Length == 7 && name[6] is >= '1' and <= '9' => 89 + name[6] - '1',
            _ when name[0] == 'f' && int.TryParse(name[1..], out int f) && f is >= 1 and <= 15 => f <= 12 ? 57 + f : 91 + f,
            _ => Named.TryGetValue(name, out int code) ? code : null,
        };

        private static readonly Dictionary<string, int> Named = new()
        {
            ["enter"] = 40, ["escape"] = 41, ["backspace"] = 42, ["tab"] = 43, ["space"] = 44, ["minus"] = 45, ["equals"] = 46, ["leftbracket"] = 47,
            ["rightbracket"] = 48, ["backslash"] = 49, ["semicolon"] = 51, ["quote"] = 52, ["backquote"] = 53, ["comma"] = 54, ["period"] = 55, ["slash"] = 56,
            ["capslock"] = 57, ["print_screen"] = 70, ["scroll_lock"] = 71, ["pause"] = 72, ["insert"] = 73, ["home"] = 74, ["pageup"] = 75, ["del"] = 76,
            ["end"] = 77, ["pagedown"] = 78, ["right"] = 79, ["left"] = 80, ["down"] = 81, ["up"] = 82, ["numlock"] = 83, ["divide"] = 84, ["multiply"] = 85,
            ["kp_minus"] = 86, ["kp_plus"] = 87, ["kp_enter"] = 88, ["kp_period"] = 99, ["oem102"] = 100, ["kp_equals"] = 103,
            ["ctrl"] = 224, ["shift"] = 225, ["alt"] = 226, ["rctrl"] = 228, ["rshift"] = 229, ["ralt"] = 230,
        };

        private static readonly System.Text.RegularExpressions.Regex Mappings = new(@"^gamepad_mappings = \[[^\]]*\]\n?", System.Text.RegularExpressions.RegexOptions.Multiline);
        private static readonly System.Text.RegularExpressions.Regex GamepadId = new(@"gamepad_id = '([0-9a-fA-F]+)'");

        /// <summary>
        /// The keys and the gamepad's buttons chosen in Régler mes touches, as the client shows them: a key none plays is
        /// none (0) in xemu, never its own default. The gamepad's are written for each gamepad xemu knows (by SDL's id,
        /// it adds one the first time it sees it) and the one plugged in now, read with RetroArch's SDL; none chosen, xemu's
        /// own (the same buttons) stay.
        /// </summary>
        public string Controls(string existing, IReadOnlyDictionary<string, RetroKeyboard.Key>? keys, IReadOnlyDictionary<string, RetroPad.Input>? pad, string? plugged)
        {
            string config = MergeIni(existing, "input.keyboard_controller_scancode_map",
                KeyboardButtons.Select(k => (k.Xemu, (RetroKeyboard.KeyOf(keys, k.Bind) is { } key ? Scancode(key.Name) ?? 0 : 0).ToString())), " = ");
            if (pad == null)
                return config;
            var known = Mappings.Match(config) is { Success: true } block ? GamepadId.Matches(block.Value).Select(m => m.Groups[1].Value).ToList() : new List<string>();
            if (plugged != null && !known.Contains(plugged, StringComparer.OrdinalIgnoreCase))
                known.Add(plugged);
            if (known.Count == 0)
                return config;
            static int? Number(RetroPad.Input? input) => input != null && int.TryParse(input.Name, out int n) ? Math.Abs(n) : null;
            var mapping = PadButtons.Select(b => $"{b.Xemu} = {(RetroPad.InputOf(pad, b.Bind) is { IsAxis: false } input ? Number(input) ?? -1 : -1)}")
                .Concat(PadAxes.SelectMany(a =>
                {
                    RetroPad.Input? input = RetroPad.InputOf(pad, a.Bind);
                    input = input?.IsAxis == true ? input : null;
                    var values = new List<string> { $"{a.Xemu} = {Number(input) ?? -1}" };
                    if (a.Invert != null)
                        values.Add($"{a.Invert} = {(input?.Name[0] == '-' ? "true" : "false")}");
                    return values;
                }));
            string controller = string.Join(", ", mapping);
            string mappings = "[" + string.Join(", ", known.Select(id => $"{{ gamepad_id = '{id}', controller_mapping = {{ {controller} }} }}")) + "]";
            return MergeIni(Mappings.Replace(config, ""), "input", new[] { ("gamepad_mappings", mappings) }, " = ");
        }

        /// <summary>shortcut: the console's language is in its EEPROM (with a checksum), not written: its games start in English, until asked.</summary>
        public override string Language(string existing, string language) => existing;
    }
}
