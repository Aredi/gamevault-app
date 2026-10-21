namespace GameVault.Core.Retro
{
    /// <summary>
    /// RetroArch plays the games of the retro platforms (from RomM): one stable build kept in the client's data folder,
    /// and a libretro core per platform, fetched the first time a game of that platform is installed. The client
    /// writes its own configuration (folders, full screen, quit with Select+Start), so nothing is set up by hand.
    /// </summary>
    public sealed class RetroArchLayout(string directory, bool windows)
    {
        public const string Version = "1.22.2";
        private const string Buildbot = "https://buildbot.libretro.com";

        /// <summary>Azahar publishes its core itself (the buildbot has none): this version.</summary>
        public const string AzaharVersion = "2126.2";

        private string Os => windows ? "windows" : "linux";

        public string Directory => directory;
        public string PackageUrl => $"{Buildbot}/stable/{Version}/{Os}/x86_64/RetroArch.7z";
        /// <summary>The folder the package unpacks to.</summary>
        public string AppDirectory => Path.Combine(directory, windows ? "RetroArch-Win64" : "RetroArch-Linux-x86_64");
        public string Executable => Path.Combine(AppDirectory, windows ? "retroarch.exe" : "RetroArch-Linux-x86_64.AppImage");
        /// <summary>Where the package keeps its menus, controller profiles and core descriptions (the AppImage's portable home on Linux).</summary>
        private string DataDirectory => windows ? AppDirectory : Path.Combine(AppDirectory, "RetroArch-Linux-x86_64.AppImage.home", ".config", "retroarch");
        /// <summary>What the player changed in RetroArch's menu: core options, controls remapped, overrides.</summary>
        public string MenuSettingsDirectory => Path.Combine(DataDirectory, "config");
        public string CoresDirectory => Path.Combine(directory, "cores");
        /// <summary>Outside the package (an update of RetroArch keeps them), one folder per GameVault player of this PC.</summary>
        public string SavesDirectory(string player) => Path.Combine(directory, "saves", player);
        public string StatesDirectory(string player) => Path.Combine(directory, "states", player);
        public string SaveBackupsDirectory(string player) => Path.Combine(directory, "save-backups", player);
        /// <summary>The BIOS files, for the platforms that need some.</summary>
        public string SystemDirectory => Path.Combine(directory, "system");
        /// <summary>RetroArch ignores a folder of its configuration that does not exist (and saves in its own): made first.</summary>
        public IEnumerable<string> Folders(string player) => new[] { CoresDirectory, SavesDirectory(player), StatesDirectory(player), SystemDirectory };

        /// <summary>The first version kept one saves folder for all: the first player to start a game here gets it.</summary>
        public void AdoptSharedSaves(string player)
        {
            foreach (string kind in new[] { "saves", "states" })
            {
                string root = Path.Combine(directory, kind), own = Path.Combine(root, player);
                if (!System.IO.Directory.Exists(root) || System.IO.Directory.Exists(own))
                    continue;
                var shared = System.IO.Directory.EnumerateFileSystemEntries(root).Where(e => !Path.GetFileName(e).All(char.IsDigit)).ToList();
                System.IO.Directory.CreateDirectory(own);
                foreach (string entry in shared)
                    System.IO.Directory.Move(entry, Path.Combine(own, Path.GetFileName(entry)));
            }
        }
        public string ConfigFile => Path.Combine(directory, "sanctuaryvault.cfg");
        public string VersionFile => Path.Combine(directory, "version");

        /// <summary>
        /// Left in the package: 13 000 of its 15 000 files (other menu themes, shaders, touch overlays, the game
        /// database), which the client does not use. On Windows each written file is scanned, which took minutes.
        /// </summary>
        public static readonly IReadOnlyList<string> UnusedPackageFolders = new[] { "xmb", "glui", "shaders", "overlays", "database", "filters" };

        public bool IsInstalled => File.Exists(Executable) && File.Exists(VersionFile) && File.ReadAllText(VersionFile).Trim() == Version;

        public string CoreFile(string core) => Path.Combine(CoresDirectory, windows ? $"{core}_libretro.dll" : $"{core}_libretro.so");
        // shortcut: the buildbot keeps no stable file per core, so cores are the nightly "latest"; pin them if one breaks
        public string CoreUrl(string core) => core == "azahar"
            ? $"https://github.com/azahar-emu/azahar/releases/download/{AzaharVersion}/azahar-libretro-{Os}-x86_64-{AzaharVersion}.zip"
            : $"{Buildbot}/nightly/{Os}/x86_64/latest/{Path.GetFileName(CoreFile(core))}.zip";

        /// <summary>A core's data files (Dolphin's Sys folder...), as RetroArch's own "core system files" downloader takes them.</summary>
        public static string SystemFilesUrl(string name) => $"{Buildbot}/assets/system/{Uri.EscapeDataString(name)}.zip";
        /// <summary>Written once the files of a name are in the system folder.</summary>
        public string SystemFilesMarker(string name) => Path.Combine(SystemDirectory, $".{name}.installed");

        public string Config(string player)
        {
            var settings = new (string Key, string Value)[]
            {
                ("libretro_directory", CoresDirectory),
                ("libretro_info_path", Path.Combine(DataDirectory, windows ? "info" : "cores")),
                ("assets_directory", Path.Combine(DataDirectory, "assets")),
                ("joypad_autoconfig_dir", Path.Combine(DataDirectory, "autoconfig")),
                ("system_directory", SystemDirectory),
                ("savefile_directory", SavesDirectory(player)),
                ("savestate_directory", StatesDirectory(player)),
                // The menu theme kept in the package (the others are not unpacked)
                ("menu_driver", "ozone"),
                ("video_fullscreen", "true"),
                // RetroPad combinations: 4 is Select+Start, 2 is L3+R3
                ("input_quit_gamepad_combo", "4"),
                ("input_menu_toggle_gamepad_combo", "2"),
                ("quit_press_twice", "false"),
                ("pause_nonactive", "false"),
                // Written again before each game: a change made in the menu lasts until RetroArch closes
                ("config_save_on_exit", "false"),
            };
            return string.Concat(settings.Select(s => $"{s.Key} = \"{s.Value}\"\n"));
        }

        public IReadOnlyList<string> Arguments(string core, string rom) => new[] { "-L", CoreFile(core), "--config", ConfigFile, rom };
        /// <summary>RetroArch's own menu, without a game: to change the controls.</summary>
        public IReadOnlyList<string> MenuArguments => new[] { "--config", ConfigFile, "--menu" };
    }
}
