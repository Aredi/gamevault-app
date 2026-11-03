namespace GameVault.Core.Retro
{
    /// <summary>
    /// Eden (a nightly build) plays the Nintendo Switch games, the console's keys and firmware from RomM copied in its
    /// "user" folder as Eden's own installation does (see <see cref="SwitchLayout"/>).
    /// </summary>
    /// <param name="x86V3">The processor runs Eden's main builds (<see cref="EmulatorExit.ProcessorIsX86V3"/>), else the build for older ones is taken.</param>
    public sealed class EdenLayout(string directory, bool windows, bool mac = false, bool x86V3 = true) : SwitchLayout(directory, windows, mac)
    {
        /// <summary>The nightly the client's settings were checked with: the oldest one it plays with.</summary>
        public const string Pinned = "v1792353251.de7e6e01d4";
        /// <summary>Eden's nightly builds, newest first (Forgejo's API).</summary>
        public const string LatestNightlyApi = "https://git.eden-emu.dev/api/v1/repos/eden-ci/nightly/releases/latest";

        private string? chosen;
        /// <summary>The nightly played with: one the player updated to, else the newer of the installed one and <see cref="Pinned"/>.</summary>
        public string Tag
        {
            get => chosen ?? (InstalledTag is string installed && IsNewer(installed, Pinned) ? installed : Pinned);
            set => chosen = value;
        }

        /// <summary>After the tag in the version file: the build for older processors, so that the other one, which dies at its start on them, is replaced.</summary>
        private const string OlderProcessorBuild = "+older-processor";
        private bool ForOlderProcessor => !x86V3 && !Mac;

        public override string Version => ForOlderProcessor ? Tag + OlderProcessorBuild : Tag;
        private string? InstalledVersion => File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null;
        /// <summary>The nightly in the folder: its tag (before 1.26.3, the commit of <see cref="Pinned"/>).</summary>
        private string? InstalledTag => InstalledVersion?.Replace(OlderProcessorBuild, "") is string text
            ? (Pinned.EndsWith("." + text, StringComparison.Ordinal) ? Pinned : text)
            : null;
        public override bool IsInstalled => File.Exists(Executable) && InstalledTag == Tag && InstalledVersion!.EndsWith(OlderProcessorBuild, StringComparison.Ordinal) == ForOlderProcessor;

        /// <summary>"v1792353251.de7e6e01d4": built at that Unix time.</summary>
        public static DateTimeOffset BuiltAt(string tag) => DateTimeOffset.FromUnixTimeSeconds(long.TryParse(tag.TrimStart('v').Split('.')[0], out long time) ? time : 0);
        public static bool IsNewer(string tag, string than) => BuiltAt(tag) > BuiltAt(than);

        /// <summary>
        /// The latest nightly of <see cref="LatestNightlyApi"/>'s answer when it is newer than the one played with and has
        /// this PC's build; else null. The release's files are not attached to it, its notes link to them.
        /// </summary>
        public string? NewerNightly(string json)
        {
            var release = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (release?["tag_name"]?.GetValue<string>() is not string tag || !IsNewer(tag, Tag))
                return null;
            string package = Path.GetFileName(PackageUrlOf(tag));
            return release["body"]?.GetValue<string>().Contains(package, StringComparison.Ordinal) == true ? tag : null;
        }

        public override string PackageUrl => PackageUrlOf(Tag);
        private string PackageUrlOf(string tag)
        {
            string commit = tag[(tag.IndexOf('.') + 1)..];
            // Windows' build for older processors is MSVC's: it needs the Visual C++ Redistributable, the others nothing
            return Windows
                ? $"https://nightly.eden-emu.dev/{tag}/Eden-Windows-{commit}-{(ForOlderProcessor ? "amd64-msvc-standard" : "amd64-clang-pgo")}.zip"
                : Mac ? $"https://nightly.eden-emu.dev/{tag}/Eden-macOS-{commit}.dmg"
                : $"https://nightly.eden-emu.dev/{tag}/Eden-Linux-{commit}-{(ForOlderProcessor ? "legacy" : "amd64")}-clang-pgo.AppImage";
        }
        protected override string WindowsExecutable => "eden.exe";
        protected override string MacExecutable => Path.Combine("eden.app", "Contents", "MacOS", "eden");
    }
}
