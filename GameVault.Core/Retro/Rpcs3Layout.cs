namespace GameVault.Core.Retro
{
    /// <summary>
    /// RPCS3 plays the PlayStation 3 games (RetroArch has no core for it), with the console's firmware installed into
    /// it once (PS3UPDAT.PUP, from RomM). Its console's storage (dev_hdd0: the saves, one user per GameVault player)
    /// stays when RPCS3 is uninstalled or updated.
    /// </summary>
    public sealed class Rpcs3Layout(string directory, bool windows) : StandaloneLayout(directory, windows)
    {
        private const string Release = "0.0.43-20210";
        private const string Build = "fcbed0bc6d803abc4d7ed1a722f2ac54d4591eee";

        public override string Version => Release;
        public override string PackageUrl => Windows
            ? $"https://github.com/RPCS3/rpcs3-binaries-win/releases/download/build-{Build}/rpcs3-v{Release}-{Build[..8]}_win64_msvc.7z"
            : $"https://github.com/RPCS3/rpcs3-binaries-linux/releases/download/build-{Build}/rpcs3-v{Release}-{Build[..8]}_linux64.AppImage";
        protected override string WindowsExecutable => "rpcs3.exe";

        /// <summary>Its configuration and console storage: beside the program on Windows, in the folder the client gives it on Linux (XDG_CONFIG_HOME).</summary>
        public string ConfigDirectory => Windows ? Directory : Path.Combine(Directory, "config", "rpcs3");
        /// <summary>On Linux, its configuration and caches go in its folder, not the player's home.</summary>
        public override IReadOnlyDictionary<string, string> Environment => Windows ? new Dictionary<string, string>() : new Dictionary<string, string>
        {
            ["XDG_CONFIG_HOME"] = Path.Combine(Directory, "config"),
            ["XDG_CACHE_HOME"] = Path.Combine(Directory, "cache"),
        };
        /// <summary>Where it tells what it did: the firmware's installation is read there.</summary>
        public string LogFile => Windows ? Path.Combine(Directory, "RPCS3.log") : Path.Combine(Directory, "cache", "rpcs3", "RPCS3.log");

        /// <summary>RPCS3 checks this module of the firmware before any game.</summary>
        public override bool HasFirmware => File.Exists(Path.Combine(ConfigDirectory, "dev_flash", "vsh", "module", "vsh.self"));
        public const string FirmwareFile = "PS3UPDAT.PUP";
        public const string FirmwareInstalledLine = "Successfully installed PS3 firmware";

        /// <summary>A console user per GameVault player ("00000005"): RPCS3 starts the game as that user.</summary>
        public static string UserId(string player) => int.TryParse(player, out int id) && id is > 0 and <= 99999999 ? id.ToString("D8") : "00000001";
        public string UserDirectory(string player) => Path.Combine(ConfigDirectory, "dev_hdd0", "home", UserId(player));
        public override string SavesDirectory(string player) => Path.Combine(UserDirectory(player), "savedata");
        public override string StatesDirectory => Path.Combine(ConfigDirectory, "savestates");

        /// <summary>The console's storage and the save states.</summary>
        public override IEnumerable<string> KeptPaths => new[] { Path.Combine(ConfigDirectory, "dev_hdd0"), StatesDirectory };

        /// <summary>
        /// Its window's settings, the welcome and "installed" boxes off (nothing to answer but the firmware's "Install?"),
        /// and no update check (the client keeps the version it knows). On Windows the program's folder may hold them
        /// at its root or in config/: written in both.
        /// </summary>
        public override IEnumerable<string> SettingsFiles => Windows
            ? new[] { Path.Combine(Directory, "GuiConfigs", "CurrentSettings.ini"), Path.Combine(Directory, "config", "GuiConfigs", "CurrentSettings.ini") }
            : new[] { Path.Combine(ConfigDirectory, "GuiConfigs", "CurrentSettings.ini") };

        private static readonly (string Key, string Value)[] MainWindow =
        {
            ("infoBoxEnabledWelcome", "false"), ("infoBoxEnabledInstallPUP", "false"), ("infoBoxEnabledInstallPKG", "false"),
            ("confirmationBoxExitGame", "false"), ("checkUpdateStart", "false"),
        };

        /// <summary>RPCS3's settings with the client's in its [main_window] section, the others kept.</summary>
        public override string Settings(string existing) => MergeIni(existing, "main_window", MainWindow);

        /// <summary>
        /// What RPCS3 starts: an ISO as it is, a game in a folder by its folder (the one holding PS3_GAME), which tells
        /// RPCS3 it is a disc.
        /// </summary>
        public static string BootTarget(string launchFile)
        {
            var usrdir = System.IO.Directory.GetParent(launchFile);
            return Path.GetFileName(launchFile).Equals("EBOOT.BIN", StringComparison.OrdinalIgnoreCase)
                && usrdir?.Name.Equals("USRDIR", StringComparison.OrdinalIgnoreCase) == true
                && usrdir.Parent?.Name.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase) == true
                ? usrdir.Parent.Parent!.FullName
                : launchFile;
        }

        public override IReadOnlyList<string> Arguments(string player, string launchFile) => new[] { "--no-gui", "--user-id", UserId(player), BootTarget(launchFile) };

        /// <summary>Its config.yml (in config/ too on Windows, as its GUI settings).</summary>
        public override IEnumerable<string> ResolutionFiles => Windows
            ? new[] { Path.Combine(Directory, "config.yml"), Path.Combine(Directory, "config", "config.yml") }
            : new[] { Path.Combine(ConfigDirectory, "config.yml") };

        /// <summary>Its "Resolution Scale" (a percentage) under "Video:", the rest of the YAML kept; RPCS3 adds what is missing.</summary>
        public override string Resolution(string existing, int scale) => MergeYaml(existing, "Video", "Resolution Scale", (scale * 100).ToString());

        /// <summary>Its "Language" under "System:", by its own names ("English (US)").</summary>
        public override string Language(string existing, string language) => MergeYaml(existing, "System", "Language", language == "English" ? "English (US)" : language);

        /// <summary>A setting of its config.yml in its section, the rest kept.</summary>
        private static string MergeYaml(string existing, string section, string key, string value)
        {
            var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            string line = $"  {key}: {value}";
            int start = lines.IndexOf(section + ":");
            if (start < 0)
            {
                lines.Add(section + ":");
                lines.Add(line);
            }
            else
            {
                int end = lines.FindIndex(start + 1, l => l.Length > 0 && !l.StartsWith(' '));
                if (end < 0)
                    end = lines.Count;
                int at = lines.FindIndex(start + 1, end - start - 1, l => l.StartsWith($"  {key}:", StringComparison.Ordinal));
                if (at >= 0)
                    lines[at] = line;
                else
                    lines.Insert(start + 1, line);
            }
            return string.Join('\n', lines) + "\n";
        }
        /// <summary>Its global input configuration ("Default"), which it reads before each game.</summary>
        public string InputFile => Windows
            ? Path.Combine(Directory, "config", "input_configs", "global", "Default.yml")
            : Path.Combine(ConfigDirectory, "input_configs", "global", "Default.yml");

        private const string InputMark = "# SanctuaryVault";

        /// <summary>
        /// Without one RPCS3 plays with the keyboard: the first controller as player 1 (XInput's on Windows, the Linux one by
        /// its name), its buttons RPCS3's defaults. Null keeps the file: the player's own (saved from RPCS3's window), or no controller.
        /// </summary>
        /// <param name="linuxGamepad">The first gamepad's name on Linux (see <see cref="FirstGamepad"/>).</param>
        public string? Input(string? existing, string? linuxGamepad)
        {
            if (existing != null && !existing.StartsWith(InputMark, StringComparison.Ordinal))
                return null;
            var (handler, device) = Windows ? ("XInput", "XInput Pad #1") : ("Evdev", linuxGamepad);
            if (device == null)
                return null;
            return $"{InputMark}: the first controller as player 1; RPCS3's own controller settings replace this file\n"
                + $"Player 1 Input:\n  Handler: {handler}\n  Device: \"{device.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"\n";
        }

        /// <summary>
        /// The name of Linux's first gamepad (one with a south button, as SDL tells them), as RPCS3's evdev handler names it;
        /// null without one. A keyboard's or a LED controller's input devices are not gamepads.
        /// </summary>
        /// <param name="inputClass">"/sys/class/input".</param>
        public static string? FirstGamepad(string inputClass)
        {
            const int BtnSouth = 0x130;
            if (!System.IO.Directory.Exists(inputClass))
                return null;
            foreach (string device in System.IO.Directory.EnumerateDirectories(inputClass, "event*")
                .OrderBy(d => int.TryParse(Path.GetFileName(d)[5..], out int n) ? n : int.MaxValue))
            {
                try
                {
                    // The key bits in hexadecimal words of 64 bits, the highest first
                    string[] words = File.ReadAllText(Path.Combine(device, "device", "capabilities", "key")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    int word = words.Length - 1 - BtnSouth / 64;
                    if (word < 0 || (Convert.ToUInt64(words[word], 16) >> (BtnSouth % 64) & 1) == 0)
                        continue;
                    string name = File.ReadAllText(Path.Combine(device, "device", "name")).Trim();
                    if (name.Length > 0)
                        return name;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { }
            }
            return null;
        }

        public static IReadOnlyList<string> FirmwareArguments(string pup) => new[] { "--installfw", pup };
    }
}
