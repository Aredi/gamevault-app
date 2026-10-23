namespace GameVault.Core.Retro
{
    /// <summary>
    /// An emulator of its own, not a RetroArch core (RPCS3, Eden): one build kept in the client's data folder, beside
    /// RetroArch, with its console's storage (the saves, a user per GameVault player) kept when it is uninstalled or updated.
    /// </summary>
    public abstract class StandaloneLayout(string directory, bool windows)
    {
        public string Directory => directory;
        protected bool Windows => windows;
        public abstract string Version { get; }
        public abstract string PackageUrl { get; }
        protected abstract string WindowsExecutable { get; }
        /// <summary>The AppImage is unpacked once (no FUSE needed, nothing unpacked at each start, nothing left in /tmp when it is closed).</summary>
        public string Executable => windows ? Path.Combine(directory, WindowsExecutable) : Path.Combine(AppDirectory, "AppRun");
        public string AppDirectory => windows ? directory : Path.Combine(directory, "app");
        public virtual string WorkingDirectory => AppDirectory;
        public string VersionFile => Path.Combine(directory, "version");
        public virtual bool IsInstalled => File.Exists(Executable) && File.Exists(VersionFile) && File.ReadAllText(VersionFile).Trim() == Version;
        public virtual IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>();

        /// <summary>The console's system files, from RomM, are in place.</summary>
        public abstract bool HasFirmware { get; }
        public abstract string SavesDirectory(string player);
        public abstract string StatesDirectory { get; }
        /// <summary>The console's storage, with the saves: kept by an uninstall.</summary>
        public abstract IEnumerable<string> KeptPaths { get; }

        /// <summary>Its settings files, given the client's (no question, no update check) by <see cref="Settings"/>.</summary>
        public abstract IEnumerable<string> SettingsFiles { get; }
        public abstract string Settings(string existing);

        public abstract IReadOnlyList<string> Arguments(string player, string launchFile);

        /// <summary>Where it keeps its definition (a multiple of the console's) and its console's language, and that file with one: see <see cref="RetroResolution"/>, <see cref="RetroLanguage"/>.</summary>
        public abstract IEnumerable<string> ResolutionFiles { get; }
        public abstract string Resolution(string existing, int scale);
        public abstract string Language(string existing, string language);

        /// <summary>An INI with these values in their section, the others kept.</summary>
        protected static string MergeIni(string existing, string section, IEnumerable<(string Key, string Value)> values)
        {
            var lines = existing.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
            int start = lines.IndexOf($"[{section}]");
            if (start < 0)
            {
                lines.Add($"[{section}]");
                start = lines.Count - 1;
            }
            int end = lines.FindIndex(start + 1, l => l.StartsWith('['));
            if (end < 0)
                end = lines.Count;
            foreach (var (key, value) in values)
            {
                int line = lines.FindIndex(start + 1, end - start - 1, l => l.StartsWith(key + "=", StringComparison.Ordinal));
                if (line >= 0)
                    lines[line] = $"{key}={value}";
                else
                    lines.Insert(end++, $"{key}={value}");
            }
            return string.Join('\n', lines) + "\n";
        }
    }
}
