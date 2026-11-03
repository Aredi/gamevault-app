using System.Text.Json.Nodes;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// Eglantine plays the Nintendo Switch 2 games: PokeDinner's fork of it (Franseven's), an Eden with the Switch 2's
    /// services, Ampere GPU and memory, built for Windows, Linux (glibc 2.39) and an Apple silicon Mac (macOS 26). Its
    /// games are decrypted dumps: a NUP (the dump with its updates and DLC in one file), a Switch 2 NSP, or the dump's
    /// folder (exefs/main, exefs/main.npdm and romfs); none needs keys or firmware.
    /// </summary>
    public sealed class EglantineLayout(string directory, bool windows, bool mac = false) : SwitchLayout(directory, windows, mac)
    {
        /// <summary>The release the client's settings were checked with: the oldest one it plays with.</summary>
        public const string Pinned = "2.5.0";
        private const string Releases = "https://git.eden-emu.dev/Franseven/PokeDinner-a-Eglantine-Fork/releases/download/";
        /// <summary>Its releases, newest first, the pre-releases too: each one starts more games (Forgejo's API).</summary>
        public const string ReleasesApi = "https://git.eden-emu.dev/api/v1/repos/Franseven/PokeDinner-a-Eglantine-Fork/releases?limit=10";

        private (string Tag, string Url)? chosen;
        private string? InstalledTag => File.Exists(VersionFile) && File.Exists(Executable) ? File.ReadAllText(VersionFile).Trim() : null;

        /// <summary>The release played with: one the player updated to, else the newer of the installed one and <see cref="Pinned"/>.</summary>
        public string Tag => chosen?.Tag ?? (InstalledTag is string installed && IsNewer(installed, Pinned) ? installed : Pinned);
        public override string Version => Tag;

        /// <summary>Its files are named differently from a release to another (the Mac's by its commit): the pinned one's here, a newer one's from <see cref="ReleasesApi"/>.</summary>
        public override string PackageUrl => chosen?.Url ?? Releases + Pinned + "/" + (Windows ? "PokeDinner-Eglantine-2.5.0-win64.zip"
            : Mac ? "PokeDinner-macOS-arm64-216b503.zip"
            : "Eglantine-PokeDinner-2.5.0-linux-x86_64.AppImage");

        /// <summary>Installed over this one at the next start, the console's storage kept.</summary>
        public void Update(string tag, string url) => chosen = (tag, url);

        /// <summary>"2.1.0a" is 2.1.0: the numbers at its start.</summary>
        public static System.Version VersionOf(string tag)
        {
            string numbers = new string(tag.TrimStart('v').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            return System.Version.TryParse(numbers.Contains('.') ? numbers : numbers + ".0", out var version) ? version : new System.Version(0, 0);
        }
        public static bool IsNewer(string tag, string than) => VersionOf(tag) > VersionOf(than);

        /// <summary>This PC's file of a release: Windows' whole zip (not the "exe-only" one), Linux's AppImage, the Mac's zip.</summary>
        private string? AssetOf(JsonNode release) => release["assets"]?.AsArray()
            .Select(a => (Name: a?["name"]?.GetValue<string>() ?? "", Url: a?["browser_download_url"]?.GetValue<string>()))
            .FirstOrDefault(a => Windows ? a.Name.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase)
                : Mac ? a.Name.Contains("macOS-arm64", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                : a.Name.EndsWith("linux-x86_64.AppImage", StringComparison.OrdinalIgnoreCase)).Url;

        /// <summary>The newest release of <see cref="ReleasesApi"/>'s answer newer than the one played with that has this PC's file; else null.</summary>
        public (string Tag, string Url)? NewerRelease(string json)
        {
            foreach (JsonNode? release in JsonNode.Parse(json)?.AsArray() ?? new JsonArray())
                if (release?["tag_name"]?.GetValue<string>() is string tag && IsNewer(tag, Tag) && AssetOf(release) is string url)
                    return (tag, url);
            return null;
        }

        /// <summary>Windows' zip and the Mac's hold a folder named after the release: its contents are what is installed.</summary>
        public override bool PackageInFolder => Windows || Mac;
        protected override string WindowsExecutable => "eglantine.exe";
        protected override string MacExecutable => Path.Combine("PokeDinner.app", "Contents", "MacOS", "eglantine");

        /// <summary>Its games are decrypted: no keys nor firmware to take from RomM.</summary>
        public override bool HasFirmware => true;

        /// <summary>
        /// The Switch 2's 12 GB of memory (its [Core] memory_layout_mode: 4, 6, 8, 10, 12 GB), which its games plan all of
        /// (it needs about 20 GB of memory and swap free): the console's default 4 GB stops them in their first minutes.
        /// </summary>
        public override string Settings(string existing) =>
            MergeIni(base.Settings(existing), "Core", new[] { ("memory_layout_mode\\default", "false"), ("memory_layout_mode", "4") });

        /// <summary>A dump's folder is started by its exefs/main, found by its main.npdm (a name with a dot, see <see cref="RetroSystem.Extensions"/>).</summary>
        public override IReadOnlyList<string> Arguments(string player, string launchFile) =>
            base.Arguments(player, string.Equals(Path.GetFileName(launchFile), "main.npdm", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(launchFile, null) : launchFile);
    }
}
