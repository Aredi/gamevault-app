namespace GameVault.Core.Retro
{
    /// <summary>
    /// RetroArch plays the games of the retro platforms (from RomM): one stable build kept in the client's data folder,
    /// and a libretro core per platform, fetched the first time a game of that platform is installed. The client
    /// writes its own configuration (folders, full screen, quit with Select+Start), so nothing is set up by hand.
    /// On a Mac: its app from the disk image, its menus and profiles from the app's assets.zip, the Apple Silicon cores.
    /// </summary>
    public sealed class RetroArchLayout(string directory, bool windows, bool mac = false)
    {
        public const string Version = "1.22.2";
        private const string Buildbot = "https://buildbot.libretro.com";

        /// <summary>Azahar publishes its core itself (the buildbot has none): this version.</summary>
        public const string AzaharVersion = "2126.2";

        private string Os => windows ? "windows" : "linux";

        public string Directory => directory;
        public string PackageUrl => mac ? $"{Buildbot}/stable/{Version}/apple/osx/universal/RetroArch_Metal.dmg" : $"{Buildbot}/stable/{Version}/{Os}/x86_64/RetroArch.7z";
        /// <summary>The folder the package unpacks to.</summary>
        public string AppDirectory => Path.Combine(directory, windows ? "RetroArch-Win64" : mac ? "RetroArch-macOS" : "RetroArch-Linux-x86_64");
        /// <summary>The Mac's app, copied from the disk image.</summary>
        public string MacApp => Path.Combine(AppDirectory, "RetroArch.app");
        public string Executable => windows ? Path.Combine(AppDirectory, "retroarch.exe")
            : mac ? Path.Combine(MacApp, "Contents", "MacOS", "RetroArch") : Path.Combine(AppDirectory, "RetroArch-Linux-x86_64.AppImage");
        /// <summary>Where the package keeps its menus, controller profiles and core descriptions (the AppImage's portable home on Linux, what the client takes out of the app's assets.zip on a Mac).</summary>
        public string DataDirectory => windows ? AppDirectory : mac ? Path.Combine(AppDirectory, "data")
            : Path.Combine(AppDirectory, "RetroArch-Linux-x86_64.AppImage.home", ".config", "retroarch");
        private string InfoDirectory => Path.Combine(DataDirectory, windows || mac ? "info" : "cores");
        /// <summary>The Mac's app keeps its menus, controller profiles and core descriptions in this zip.</summary>
        public string MacAssets => Path.Combine(MacApp, "Contents", "Resources", "assets.zip");

        /// <summary>What the client takes out of the Mac's assets.zip: the menus, the controller profiles and the core descriptions, without the folders the other packages leave out.</summary>
        public static bool IsKeptAsset(string entry)
        {
            string[] parts = entry.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && parts[0] is "assets" or "autoconfig" or "info" && !parts.Any(p => p == ".." || UnusedPackageFolders.Contains(p));
        }
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

        /// <summary>
        /// The folder RetroArch keeps a core's saves in (its saves are sorted by core): the core's name in its
        /// description ("Mesen", "mGBA"), else the core.
        /// </summary>
        /// <summary>The cores whose description (.info) gives another name than the one RetroArch names their folders after.</summary>
        private static readonly IReadOnlyDictionary<string, string> LibraryNames = new Dictionary<string, string> { ["dolphin"] = "dolphin-emu" };

        /// <summary>A core's options file (what the player chose in RetroArch's menu, and the definition chosen here).</summary>
        public string CoreOptionsFile(string core) => Path.Combine(MenuSettingsDirectory, SaveFolderName(core), $"{SaveFolderName(core)}.opt");

        /// <summary>
        /// The remap files RetroArch reads at a game's start, the first found only: the game's, its folder's, its core's
        /// (what the player saves in its menu: controls, controller type).
        /// </summary>
        public string GameRemapFile(string core, string rom) => Path.Combine(MenuSettingsDirectory, "remaps", SaveFolderName(core), Path.GetFileNameWithoutExtension(rom) + ".rmp");
        public string FolderRemapFile(string core, string rom) => Path.Combine(MenuSettingsDirectory, "remaps", SaveFolderName(core), Path.GetFileName(Path.GetDirectoryName(rom)) + ".rmp");
        public string CoreRemapFile(string core) => Path.Combine(MenuSettingsDirectory, "remaps", SaveFolderName(core), SaveFolderName(core) + ".rmp");

        public string SaveFolderName(string core)
        {
            if (LibraryNames.TryGetValue(core, out string? name))
                return name;
            if (NewNames.TryGetValue(core, out string? renamed) && HasName(CoreFile(core), renamed))
                return renamed;
            string info = Path.Combine(InfoDirectory, $"{core}_libretro.info");
            try
            {
                foreach (string line in File.Exists(info) ? File.ReadLines(info) : Enumerable.Empty<string>())
                    if (line.Split('=', 2) is [var key, var value] && key.Trim() == "corename")
                        return value.Trim().Trim('"');
            }
            catch (IOException) { }
            return core;
        }

        /// <summary>
        /// Cores that took a new name after RetroArch's info files (LRPS2 is "ParaLLEl PS2" since October 2026): RetroArch
        /// sorts saves and options by the core's own name, so a core downloaded before keeps the old one.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> NewNames = new Dictionary<string, string> { ["pcsx2"] = "ParaLLEl PS2" };
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, DateTime), bool> named = new();

        /// <summary>The core gives this name (in its file); a core not downloaded yet will be the newest.</summary>
        private static bool HasName(string coreFile, string name)
        {
            if (!File.Exists(coreFile))
                return true;
            return named.GetOrAdd((coreFile, File.GetLastWriteTimeUtc(coreFile)), _ =>
                File.ReadAllBytes(coreFile).AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(name + "\0")) >= 0);
        }

        public string CoreFile(string core) => Path.Combine(CoresDirectory, windows ? $"{core}_libretro.dll" : mac ? $"{core}_libretro.dylib" : $"{core}_libretro.so");
        // shortcut: the buildbot keeps no stable file per core, so cores are the nightly "latest"; pin them if one breaks
        // shortcut: a Mac's cores are Apple Silicon's (arm64), none runs on an Intel Mac; add osx/x86_64 if one asks
        public string CoreUrl(string core) => core == "azahar"
            ? $"https://github.com/azahar-emu/azahar/releases/download/{AzaharVersion}/azahar-libretro-{(mac ? "macos-arm64" : Os + "-x86_64")}-{AzaharVersion}.zip"
            : $"{Buildbot}/nightly/{(mac ? "apple/osx/arm64" : Os + "/x86_64")}/latest/{Path.GetFileName(CoreFile(core))}.zip";

        /// <summary>A core's data files (Dolphin's Sys folder...), as RetroArch's own "core system files" downloader takes them.</summary>
        public static string SystemFilesUrl(string name) => $"{Buildbot}/assets/system/{Uri.EscapeDataString(name)}.zip";
        /// <summary>Written once the files of a name are in the system folder.</summary>
        public string SystemFilesMarker(string name) => Path.Combine(SystemDirectory, $".{name}.installed");

        /// <summary>The CRT look (crt-easymode, one light pass), for both kinds of RetroArch's video drivers (OpenGL, the others).</summary>
        private const string GlslShaders = "https://raw.githubusercontent.com/libretro/glsl-shaders/ad947689af01544f2857591a0e3e171ac30e9dff/crt/shaders/crt-easymode.glsl";
        private const string SlangShaders = "https://raw.githubusercontent.com/libretro/slang-shaders/eef0df221e440b1ad1bd2f9019552ea3359a51e2/crt/shaders/crt-easymode.slang";
        public static readonly IReadOnlyList<(string Extension, string Url)> CrtShaderUrls = new[] { ("glsl", GlslShaders), ("slang", SlangShaders) };

        public string ShadersDirectory => Path.Combine(directory, "shaders");
        public string CrtShaderFile(string extension) => Path.Combine(ShadersDirectory, "crt", $"crt-easymode.{extension}");

        /// <summary>
        /// A game's own picture setting, where RetroArch looks for it (after the presets saved in its menu, which stay
        /// the player's): shaders/presets/&lt;core's name&gt;/&lt;game&gt;.glslp or .slangp.
        /// </summary>
        public string GamePreset(string core, string rom, string extension) =>
            Path.Combine(ShadersDirectory, "presets", SaveFolderName(core), $"{Path.GetFileNameWithoutExtension(rom)}.{extension}p");

        public string CrtPreset(string extension) =>
            $"shaders = \"1\"\nshader0 = \"{CrtShaderFile(extension)}\"\nfilter_linear0 = \"false\"\n" + (extension == "slang" ? "scale_type0 = \"viewport\"\n" : "");

        /// <param name="achievements">The player's RetroAchievements account (its token, never its password), or null.</param>
        public string Config(string player, (string User, string Token)? achievements = null)
        {
            var settings = new (string Key, string Value)[]
            {
                ("libretro_directory", CoresDirectory),
                ("libretro_info_path", InfoDirectory),
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
                // A picture of the game with each save state: the game's page shows them
                ("savestate_thumbnail_enable", "true"),
                ("video_shader_dir", ShadersDirectory),
                ("video_shader_enable", "true"),
                // Not hardcore: the save states stay allowed
                ("cheevos_enable", achievements != null ? "true" : "false"),
                ("cheevos_username", achievements?.User ?? ""),
                ("cheevos_token", achievements?.Token ?? ""),
                ("cheevos_hardcore_mode_enable", "false"),
            };
            // A Mac's RetroArch keeps its menu's settings in ~/Library and unpacks its assets.zip there: in its folder instead, like the others
            if (mac)
                settings = settings.Concat(new[] { ("rgui_config_directory", MenuSettingsDirectory), ("input_remapping_directory", Path.Combine(MenuSettingsDirectory, "remaps")), ("bundle_assets_extract_enable", "false") }).ToArray();
            return string.Concat(settings.Select(s => $"{s.Key} = \"{s.Value}\"\n"));
        }

        /// <param name="stateSlot">The save state the game starts from, or none.</param>
        public IReadOnlyList<string> Arguments(string core, string rom, int? stateSlot = null) => stateSlot is int slot
            ? new[] { "-L", CoreFile(core), "--config", ConfigFile, "--entryslot", slot.ToString(), rom }
            : new[] { "-L", CoreFile(core), "--config", ConfigFile, rom };

        /// <summary>A save state of a game: its slot (0 is ".state"), when, and its picture when RetroArch took one.</summary>
        public sealed record SaveState(int Slot, string File, DateTime Saved, string? Picture);

        /// <summary>The game's save states (RetroArch sorts them by core), the newest first; the automatic one is not a slot.</summary>
        public IReadOnlyList<SaveState> SaveStates(string player, string core, string rom)
        {
            string folder = Path.Combine(StatesDirectory(player), SaveFolderName(core)), stem = Path.GetFileNameWithoutExtension(rom) + ".state";
            if (!System.IO.Directory.Exists(folder))
                return Array.Empty<SaveState>();
            return System.IO.Directory.EnumerateFiles(folder, stem + "*")
                .Select(file => (file, suffix: Path.GetFileName(file)[stem.Length..]))
                .Where(f => f.suffix.Length == 0 || f.suffix.All(char.IsDigit))
                .Select(f => new SaveState(f.suffix.Length == 0 ? 0 : int.Parse(f.suffix), f.file, File.GetLastWriteTimeUtc(f.file), File.Exists(f.file + ".png") ? f.file + ".png" : null))
                .Where(s => s.Slot <= 999)
                .OrderByDescending(s => s.Saved).ToList();
        }
        /// <summary>RetroArch's own menu, without a game: to change the controls.</summary>
        public IReadOnlyList<string> MenuArguments => new[] { "--config", ConfigFile, "--menu" };
    }
}
