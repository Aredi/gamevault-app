namespace GameVault.Core.Retro
{
    /// <summary>
    /// A fork of its console's emulator (Eden, Eglantine) an administrator gives some games, one that plays them better: its
    /// build for this system from the link given, kept in its own folder, with the console's emulator's "user" folder linked
    /// beside it (the same saves, players, keys, settings and controller) and started as it.
    /// </summary>
    /// <param name="executable">Its program in a Windows or Mac build when it is not named as the console emulator's ("lichii.exe"); an AppImage starts itself.</param>
    public sealed class CustomSwitchLayout(string directory, SwitchLayout console, string? windowsUrl, string? linuxUrl, string? macUrl, bool windows, bool mac = false, string? executable = null)
        : SwitchLayout(directory, windows, mac, console.UserDirectory)
    {
        private string? Url => windows ? windowsUrl : mac ? macUrl : linuxUrl;

        /// <summary>It has a build for this system: else its games are played by their console's emulator.</summary>
        public bool IsAvailable => !string.IsNullOrWhiteSpace(Url);

        public override string PackageUrl => Url ?? throw new InvalidOperationException("No build of this emulator for this system");
        /// <summary>Its link: another link given, it is installed again.</summary>
        public override string Version => PackageUrl;

        /// <summary>A Windows or a Mac build in a folder (as forks pack them) or not: see the unpacking.</summary>
        public override bool PackageInFolder => Windows || Mac;
        /// <summary>The program given, else the console's emulator's name ("eglantine.exe"), which forks keep.</summary>
        protected override string WindowsExecutable => Program ?? Path.GetRelativePath(console.AppDirectory, console.Executable);
        protected override string MacExecutable => Program ?? Path.GetRelativePath(console.AppDirectory, console.Executable);

        /// <summary>The program given, by this system's separators; null when none or when it would leave the build's folder.</summary>
        private string? Program
        {
            get
            {
                if (string.IsNullOrWhiteSpace(executable))
                    return null;
                string[] parts = executable.Trim().Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 0 || parts.Any(p => p.Trim() is "." or "..") || Path.IsPathRooted(executable.Trim()) ? null : Path.Combine(parts);
            }
        }

        public override bool HasFirmware => console.HasFirmware;
        /// <summary>The console's storage is the other emulator's (linked): nothing of it here.</summary>
        public override IEnumerable<string> KeptPaths => Array.Empty<string>();
        public override IReadOnlyDictionary<string, string> Environment => console.Environment;
        public override IReadOnlyList<string> StartArguments => console.StartArguments;
        public override string Settings(string existing) => console.Settings(existing);
        public override IReadOnlyList<string> Arguments(string player, string launchFile) => console.Arguments(player, launchFile);
    }
}
