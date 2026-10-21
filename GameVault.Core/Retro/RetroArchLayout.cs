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

        /// <summary>RomM platform slug → libretro core. These need no BIOS.</summary>
        public static readonly IReadOnlyDictionary<string, string> Cores = new Dictionary<string, string>
        {
            ["nes"] = "mesen",
            ["snes"] = "snes9x",
            ["gba"] = "mgba",
            ["nds"] = "melondsds",
        };

        private string Os => windows ? "windows" : "linux";

        public string Directory => directory;
        public string PackageUrl => $"{Buildbot}/stable/{Version}/{Os}/x86_64/RetroArch.7z";
        /// <summary>The folder the package unpacks to.</summary>
        public string AppDirectory => Path.Combine(directory, windows ? "RetroArch-Win64" : "RetroArch-Linux-x86_64");
        public string Executable => Path.Combine(AppDirectory, windows ? "retroarch.exe" : "RetroArch-Linux-x86_64.AppImage");
        /// <summary>Where the package keeps its menus, controller profiles and core descriptions (the AppImage's portable home on Linux).</summary>
        private string DataDirectory => windows ? AppDirectory : Path.Combine(AppDirectory, "RetroArch-Linux-x86_64.AppImage.home", ".config", "retroarch");
        public string CoresDirectory => Path.Combine(directory, "cores");
        /// <summary>Outside the package: an update of RetroArch keeps the saves.</summary>
        public string SavesDirectory => Path.Combine(directory, "saves");
        public string StatesDirectory => Path.Combine(directory, "states");
        /// <summary>The BIOS files, for the platforms that need some.</summary>
        public string SystemDirectory => Path.Combine(directory, "system");
        /// <summary>RetroArch ignores a folder of its configuration that does not exist (and saves in its own): made first.</summary>
        public IEnumerable<string> Folders => new[] { CoresDirectory, SavesDirectory, StatesDirectory, SystemDirectory };
        public string ConfigFile => Path.Combine(directory, "sanctuaryvault.cfg");
        public string VersionFile => Path.Combine(directory, "version");

        public bool IsInstalled => File.Exists(Executable) && File.Exists(VersionFile) && File.ReadAllText(VersionFile).Trim() == Version;

        public string CoreFile(string core) => Path.Combine(CoresDirectory, windows ? $"{core}_libretro.dll" : $"{core}_libretro.so");
        // shortcut: the buildbot keeps no stable file per core, so cores are the nightly "latest"; pin them if one breaks
        public string CoreUrl(string core) => $"{Buildbot}/nightly/{Os}/x86_64/latest/{Path.GetFileName(CoreFile(core))}.zip";

        public string Config()
        {
            var settings = new (string Key, string Value)[]
            {
                ("libretro_directory", CoresDirectory),
                ("libretro_info_path", Path.Combine(DataDirectory, windows ? "info" : "cores")),
                ("assets_directory", Path.Combine(DataDirectory, "assets")),
                ("joypad_autoconfig_dir", Path.Combine(DataDirectory, "autoconfig")),
                ("system_directory", SystemDirectory),
                ("savefile_directory", SavesDirectory),
                ("savestate_directory", StatesDirectory),
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
    }
}
