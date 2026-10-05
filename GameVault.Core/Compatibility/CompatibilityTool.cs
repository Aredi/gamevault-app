namespace GameVault.Core.Compatibility
{
    public enum CompatibilityToolKind
    {
        /// <summary>A Proton build directory (contains the "proton" script), started through umu-run or directly.</summary>
        Proton,
        /// <summary>A Wine build directory (contains bin/wine), or the Wine found in PATH.</summary>
        Wine,
        /// <summary>umu-run without PROTONPATH: umu downloads and updates GE-Proton by itself.</summary>
        UmuLatest,
        /// <summary>The user's own command line.</summary>
        Custom,
    }

    /// <param name="Id">Stable identifier stored in the settings, see <see cref="CompatibilityToolId"/>.</param>
    /// <param name="Source">Where the tool was found (GameVault, Steam, Lutris, System, ...).</param>
    /// <param name="IsManaged">Installed by GameVault, so GameVault may delete it.</param>
    public record CompatibilityTool(string Id, string Name, CompatibilityToolKind Kind, string? Path, string Source, bool IsManaged)
    {
        public string Description => Path == null ? Source : $"{Source} - {Path}";
        public override string ToString() => Name;
    }

    /// <summary>
    /// Identifiers of compatibility tools as stored in the settings. Builds are referenced by their directory,
    /// so a tool keeps its identity no matter which other tools are installed.
    /// </summary>
    public static class CompatibilityToolId
    {
        /// <summary>Per game: use the global default. Globally: umu-run if installed, otherwise the system Wine.</summary>
        public const string Default = "";
        public const string Auto = "auto";
        public const string UmuLatest = "umu-latest";
        public const string SystemWine = "wine:system";
        public const string Custom = "custom";

        private const string ProtonPrefix = "proton:";
        private const string WinePrefix = "wine:";

        public static string ForProton(string directory) => ProtonPrefix + Normalize(directory);
        public static string ForWine(string directory) => WinePrefix + Normalize(directory);

        public static bool IsProton(string id, out string directory) => TryGetPath(id, ProtonPrefix, out directory);
        public static bool IsWineBuild(string id, out string directory)
        {
            if (id == SystemWine)
            {
                directory = "";
                return false;
            }
            return TryGetPath(id, WinePrefix, out directory);
        }

        private static bool TryGetPath(string id, string prefix, out string directory)
        {
            directory = id.StartsWith(prefix, StringComparison.Ordinal) ? id[prefix.Length..] : "";
            return directory.Length > 0;
        }

        private static string Normalize(string directory) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));
    }
}
