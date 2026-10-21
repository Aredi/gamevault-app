namespace GameVault.Core.Retro
{
    /// <summary>
    /// RPCS3 plays the PlayStation 3 games (RetroArch has no core for it): one build kept in the client's data folder,
    /// beside RetroArch, with the console's firmware installed into it once (PS3UPDAT.PUP, from RomM). Its console's
    /// storage (dev_hdd0: the saves, one user per GameVault player) stays when RPCS3 is uninstalled or updated.
    /// </summary>
    public sealed class Rpcs3Layout(string directory, bool windows)
    {
        public const string Version = "0.0.43-20210";
        private const string Build = "fcbed0bc6d803abc4d7ed1a722f2ac54d4591eee";

        public string Directory => directory;
        public string PackageUrl => windows
            ? $"https://github.com/RPCS3/rpcs3-binaries-win/releases/download/build-{Build}/rpcs3-v{Version}-{Build[..8]}_win64_msvc.7z"
            : $"https://github.com/RPCS3/rpcs3-binaries-linux/releases/download/build-{Build}/rpcs3-v{Version}-{Build[..8]}_linux64.AppImage";
        /// <summary>The AppImage is unpacked once (no FUSE needed, nothing unpacked at each start, nothing left in /tmp when it is closed).</summary>
        public string Executable => windows ? Path.Combine(directory, "rpcs3.exe") : Path.Combine(AppDirectory, "AppRun");
        public string AppDirectory => windows ? directory : Path.Combine(directory, "app");
        public string VersionFile => Path.Combine(directory, "version");
        public bool IsInstalled => File.Exists(Executable) && File.Exists(VersionFile) && File.ReadAllText(VersionFile).Trim() == Version;

        /// <summary>Its configuration and console storage: beside the program on Windows, in the folder the client gives it on Linux (XDG_CONFIG_HOME).</summary>
        public string ConfigDirectory => windows ? directory : Path.Combine(directory, "config", "rpcs3");
        /// <summary>On Linux, its configuration and caches go in its folder, not the player's home.</summary>
        public IReadOnlyDictionary<string, string> Environment => windows ? new Dictionary<string, string>() : new Dictionary<string, string>
        {
            ["XDG_CONFIG_HOME"] = Path.Combine(directory, "config"),
            ["XDG_CACHE_HOME"] = Path.Combine(directory, "cache"),
        };
        /// <summary>Where it tells what it did: the firmware's installation is read there.</summary>
        public string LogFile => windows ? Path.Combine(directory, "RPCS3.log") : Path.Combine(directory, "cache", "rpcs3", "RPCS3.log");

        /// <summary>RPCS3 checks this module of the firmware before any game.</summary>
        public bool HasFirmware => File.Exists(Path.Combine(ConfigDirectory, "dev_flash", "vsh", "module", "vsh.self"));
        public const string FirmwareFile = "PS3UPDAT.PUP";
        public const string FirmwareInstalledLine = "Successfully installed PS3 firmware";

        /// <summary>A console user per GameVault player ("00000005"): RPCS3 starts the game as that user.</summary>
        public static string UserId(string player) => int.TryParse(player, out int id) && id is > 0 and <= 99999999 ? id.ToString("D8") : "00000001";
        public string UserDirectory(string player) => Path.Combine(ConfigDirectory, "dev_hdd0", "home", UserId(player));
        public string SavesDirectory(string player) => Path.Combine(UserDirectory(player), "savedata");
        public string StatesDirectory => Path.Combine(ConfigDirectory, "savestates");

        /// <summary>The console's storage and the save states: kept by an uninstall.</summary>
        public IEnumerable<string> KeptPaths => new[] { Path.Combine(ConfigDirectory, "dev_hdd0"), StatesDirectory };

        /// <summary>
        /// Its window's settings, the welcome and "installed" boxes off (nothing to answer but the firmware's "Install?"),
        /// and no update check (the client keeps the version it knows). On Windows the program's folder may hold them
        /// at its root or in config/: written in both.
        /// </summary>
        public IEnumerable<string> GuiSettingsFiles => windows
            ? new[] { Path.Combine(directory, "GuiConfigs", "CurrentSettings.ini"), Path.Combine(directory, "config", "GuiConfigs", "CurrentSettings.ini") }
            : new[] { Path.Combine(ConfigDirectory, "GuiConfigs", "CurrentSettings.ini") };

        private static readonly (string Key, string Value)[] MainWindow =
        {
            ("infoBoxEnabledWelcome", "false"), ("infoBoxEnabledInstallPUP", "false"), ("infoBoxEnabledInstallPKG", "false"),
            ("confirmationBoxExitGame", "false"), ("checkUpdateStart", "false"),
        };

        /// <summary>RPCS3's settings with the client's in its [main_window] section, the others kept.</summary>
        public static string GuiSettings(string existing)
        {
            var lines = existing.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
            int section = lines.IndexOf("[main_window]");
            if (section < 0)
            {
                lines.Add("[main_window]");
                section = lines.Count - 1;
            }
            int end = lines.FindIndex(section + 1, l => l.StartsWith('['));
            if (end < 0)
                end = lines.Count;
            foreach (var (key, value) in MainWindow)
            {
                int line = lines.FindIndex(section + 1, end - section - 1, l => l.StartsWith(key + "=", StringComparison.Ordinal));
                if (line >= 0)
                    lines[line] = $"{key}={value}";
                else
                    lines.Insert(end++, $"{key}={value}");
            }
            return string.Join('\n', lines) + "\n";
        }

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

        public IReadOnlyList<string> Arguments(string player, string launchFile) => new[] { "--no-gui", "--user-id", UserId(player), BootTarget(launchFile) };
        public static IReadOnlyList<string> FirmwareArguments(string pup) => new[] { "--installfw", pup };
    }
}
