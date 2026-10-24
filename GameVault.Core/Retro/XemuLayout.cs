namespace GameVault.Core.Retro
{
    /// <summary>
    /// xemu plays the Xbox games: an XISO in its drive (see <see cref="XboxDisc"/>), with the console's boot ROM and BIOS
    /// from RomM and xemu's own hard disk, free of copyrighted content (its dashboard). Each GameVault player has their
    /// hard disk, where the games keep their saves. Its settings file is found by xemu itself: beside it on Windows
    /// (its portable mode), in the folder the client gives it on Linux (XDG_DATA_HOME).
    /// </summary>
    public sealed class XemuLayout(string directory, bool windows) : StandaloneLayout(directory, windows)
    {
        private const string Release = "0.8.136";
        /// <summary>xemu's hard disk with its dashboard (8 GB, nearly empty: 1.6 MB), copied for each player.</summary>
        public const string HardDiskUrl = "https://github.com/xemu-project/xemu-dashboard/releases/download/v20260516-0955/xbox_hdd.qcow2";

        public override string Version => Release;
        public override string PackageUrl => Windows
            ? $"https://github.com/xemu-project/xemu/releases/download/v{Release}/xemu-{Release}-windows-x86_64.zip"
            : $"https://github.com/xemu-project/xemu/releases/download/v{Release}/xemu-{Release}-x86_64.AppImage";
        protected override string WindowsExecutable => "xemu.exe";

        /// <summary>On Linux, its settings and caches go in its folder, not the player's home.</summary>
        public override IReadOnlyDictionary<string, string> Environment => Windows ? new Dictionary<string, string>() : new Dictionary<string, string>
        {
            ["XDG_DATA_HOME"] = Path.Combine(Directory, "data"),
        };
        /// <summary>Where xemu looks for it: beside xemu.exe, else SDL's folder of the app ("xemu", "xemu").</summary>
        public string ConfigFile => Windows ? Path.Combine(Directory, "xemu.toml") : Path.Combine(Directory, "data", "xemu", "xemu", "xemu.toml");

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

        /// <summary>shortcut: the console's language is in its EEPROM (with a checksum), not written: its games start in English, until asked.</summary>
        public override string Language(string existing, string language) => existing;
    }
}
