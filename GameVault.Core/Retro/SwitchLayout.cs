using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// What Eden and its forks (Eglantine for the Switch 2) share, from yuzu: a portable "user" folder beside the emulator
    /// (its working folder on Linux and a Mac) with the console's storage, keys and firmware; each GameVault player is the
    /// console's first user while they play, so each has their saves.
    /// </summary>
    /// <param name="userDirectory">Another emulator's "user" folder, linked beside this one (see <see cref="CustomSwitchLayout"/>); else its own.</param>
    public abstract class SwitchLayout(string directory, bool windows, bool mac = false, string? userDirectory = null) : StandaloneLayout(directory, windows, mac)
    {
        /// <summary>On Linux Eden looks for its "user" folder in its working folder.</summary>
        public override string WorkingDirectory => Directory;
        /// <summary>
        /// A Mac's Eden looks for it beside its app (it goes there itself as it starts): a link there to the one kept
        /// outside the app's folder, which an update replaces.
        /// </summary>
        public override IReadOnlyList<(string Link, string Target)> Links => Mac ? new[] { (Path.Combine(AppDirectory, "user"), UserDirectory) }
            : userDirectory != null ? new[] { (Path.Combine(Directory, "user"), UserDirectory) }
            : base.Links;

        public string UserDirectory => userDirectory ?? Path.Combine(Directory, "user");
        /// <summary>The newest of its "log" folder: it keeps the previous start's log there too.</summary>
        public override string? LogFile
        {
            get
            {
                var logs = new DirectoryInfo(Path.Combine(UserDirectory, "log"));
                return logs.Exists ? logs.GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName : null;
            }
        }
        private string Nand => Path.Combine(UserDirectory, "nand");
        public string KeysDirectory => Path.Combine(UserDirectory, "keys");
        public const string KeysFile = "prod.keys", TitleKeysFile = "title.keys";
        public string FirmwareDirectory => Path.Combine(Nand, "system", "Contents", "registered");
        public override bool HasFirmware => File.Exists(Path.Combine(KeysDirectory, KeysFile))
            && System.IO.Directory.Exists(FirmwareDirectory) && System.IO.Directory.EnumerateFiles(FirmwareDirectory, "*.nca").Any();

        /// <summary>The firmware's contents (a zip of .nca, as dumped) in place of the ones there.</summary>
        public void InstallFirmware(Stream zip)
        {
            if (System.IO.Directory.Exists(FirmwareDirectory))
                System.IO.Directory.Delete(FirmwareDirectory, true);
            System.IO.Directory.CreateDirectory(FirmwareDirectory);
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
            // By name only: a zip from the network writes nowhere else
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase)))
                entry.ExtractToFile(Path.Combine(FirmwareDirectory, entry.Name), overwrite: true);
        }

        /// <summary>"SANCTUAR" then the player's number: a console user per GameVault player.</summary>
        private const ulong UserTag = 0x53414E4354554152;
        private static ulong PlayerNumber(string player) => ulong.TryParse(player, out ulong id) && id > 0 ? id : 1;

        public static byte[] UserId(string player)
        {
            var id = new byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(id, PlayerNumber(player));
            BinaryPrimitives.WriteUInt64LittleEndian(id.AsSpan(8), UserTag);
            return id;
        }

        /// <summary>Eden names a user's folder by its id read as two numbers, the high one first.</summary>
        public override string SavesDirectory(string player) => Path.Combine(Nand, "user", "save", "0000000000000000", $"{UserTag:X16}{PlayerNumber(player):X16}");
        /// <summary>Eden has no save states: a folder never written.</summary>
        public override string StatesDirectory => Path.Combine(UserDirectory, "states");

        /// <summary>The users' saves, and the console's users with its own saves.</summary>
        public override IEnumerable<string> KeptPaths => new[] { Path.Combine(Nand, "user"), Path.Combine(Nand, "system", "save") };

        public string ProfilesFile => Path.Combine(Nand, "system", "save", "8000000000000010", "su", "avators", "profiles.dat");
        private const int UserSize = 0xC8, Users = 8, ProfilesSize = 0x10 + Users * UserSize;

        /// <summary>
        /// The console's users (profiles.dat) with the player first, the one Eden starts the game as (-u 0), named as in
        /// GameVault. The other players stay after: Eden warns of saves whose user is gone.
        /// </summary>
        public static byte[] Profiles(byte[]? existing, string player, string name)
        {
            var profiles = new byte[ProfilesSize];
            byte[] id = UserId(player);
            Span<byte> mine = profiles.AsSpan(0x10, UserSize);
            // shortcut: the console has 8 users, a 9th player on this PC drops the last one (Eden then warns of its saves), until it happens
            int count = 1;
            for (int i = 0; existing?.Length >= ProfilesSize && i < Users; i++)
            {
                ReadOnlySpan<byte> user = existing.AsSpan(0x10 + i * UserSize, UserSize);
                if (user[..16].SequenceEqual(id))
                    user.CopyTo(mine);
                else if (user[..16].ContainsAnyExcept((byte)0) && count < Users)
                    user.CopyTo(profiles.AsSpan(0x10 + count++ * UserSize));
            }
            id.CopyTo(mine);
            id.CopyTo(mine[16..]);
            if (BinaryPrimitives.ReadUInt64LittleEndian(mine[32..]) == 0)
                BinaryPrimitives.WriteUInt64LittleEndian(mine[32..], (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // Its 32 bytes, a character not cut in two
            Span<byte> username = mine.Slice(40, 32);
            username.Clear();
            string shown = name;
            while (Encoding.UTF8.GetByteCount(shown) > username.Length)
                shown = shown[..^1];
            Encoding.UTF8.GetBytes(shown, username);
            return profiles;
        }

        /// <summary>No update check nor question (stopping a game, a missing Wayland driver): the client keeps the version it knows.</summary>
        public override IEnumerable<string> SettingsFiles => new[] { Path.Combine(UserDirectory, "config", "qt-config.ini") };

        private static readonly (string Key, string Value)[] Ui =
        {
            ("check_for_updates", "false"), ("confirmStop", "2"), ("gui_hide_backend_warning", "true"), ("select_user_on_boot", "false"),
        };

        /// <summary>
        /// The textures kept: Conservative empties the VRAM as soon as it fills, its textures vanish and come back a few
        /// seconds later (Pokémon Legends Z-A). Decoded by the GPU, not late by the CPU (accelerate_astc: Cpu, Gpu, CpuAsynchronous).
        /// </summary>
        private static readonly (string Key, string Value)[] Renderer = { ("vram_usage_mode", "1"), ("accelerate_astc", "1") };

        /// <summary>Eden reads a value only when its "\default" says it is not the default.</summary>
        public override string Settings(string existing) =>
            MergeIni(MergeIni(existing, "UI", Ui.SelectMany(s => new[] { ($"{s.Key}\\default", "false"), s })),
                "Renderer", Renderer.SelectMany(s => new[] { ($"{s.Key}\\default", "false"), s }));

        /// <summary>
        /// Where Eden finds the games' updates and DLC at a game's start (it takes the newest update): the installed
        /// Switch games' folders, nothing installed into the console. Its paths are in [UI], under "Paths\".
        /// </summary>
        public static string ContentFolders(string existing, IReadOnlyList<string> folders) =>
            MergeIni(existing, "UI", folders.Select((f, i) => ($"Paths\\external_content_dirs\\{i + 1}\\path", $"\"{f.Replace('\\', '/')}\""))
                .Append(("Paths\\external_content_dirs\\size", folders.Count.ToString())));

        /// <summary>
        /// Eden plays with the keyboard until a controller is chosen in its window (its "Input Device"): no setting written
        /// here could name it, Eden finds controllers by SDL's id of their model, driver and connection.
        /// </summary>
        public static bool HasController(string config) => config.Replace("\r\n", "\n").Split('\n')
            .Any(l => l.StartsWith("player_0_button_a=", StringComparison.Ordinal) && !l.Contains("engine:keyboard", StringComparison.Ordinal));

        public override IReadOnlyList<string> Arguments(string player, string launchFile) => new[] { "-u", "0", "-g", launchFile };

        public override IEnumerable<string> ResolutionFiles => SettingsFiles;

        /// <summary>Its [Renderer] resolution_setup: Res1_4X, Res1_2X, Res3_4X, Res1X (3), Res5_4X, Res3_2X, Res2X (6), Res3X, Res4X.</summary>
        public override string Resolution(string existing, int scale) =>
            MergeIni(existing, "Renderer", new[] { ("resolution_setup\\default", "false"), ("resolution_setup", (scale switch { >= 4 => 8, 3 => 7, 2 => 6, _ => 3 }).ToString()) });

        /// <summary>Its [System] language_index: Japanese, EnglishAmerican, French, German, Italian, Spanish...</summary>
        public override string Language(string existing, string language) =>
            MergeIni(existing, "System", new[] { ("language_index\\default", "false"), ("language_index", (language switch { "Japanese" => 0, "French" => 2, "German" => 3, "Italian" => 4, "Spanish" => 5, _ => 1 }).ToString()) });
    }
}
