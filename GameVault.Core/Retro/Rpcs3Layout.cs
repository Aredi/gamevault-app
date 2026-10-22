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
        public override string Resolution(string existing, int scale)
        {
            var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            string line = $"  Resolution Scale: {scale * 100}";
            int video = lines.IndexOf("Video:");
            if (video < 0)
            {
                lines.Add("Video:");
                lines.Add(line);
            }
            else
            {
                int end = lines.FindIndex(video + 1, l => l.Length > 0 && !l.StartsWith(' '));
                if (end < 0)
                    end = lines.Count;
                int at = lines.FindIndex(video + 1, end - video - 1, l => l.StartsWith("  Resolution Scale:", StringComparison.Ordinal));
                if (at >= 0)
                    lines[at] = line;
                else
                    lines.Insert(video + 1, line);
            }
            return string.Join('\n', lines) + "\n";
        }
        public static IReadOnlyList<string> FirmwareArguments(string pup) => new[] { "--installfw", pup };
    }
}
