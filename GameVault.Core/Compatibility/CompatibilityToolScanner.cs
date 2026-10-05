using System.Text.RegularExpressions;

namespace GameVault.Core.Compatibility
{
    /// <summary>
    /// Folders searched for compatibility tools. <see cref="ForUser"/> gives the usual Linux locations
    /// (GameVault, Steam, Steam Flatpak, Lutris); tests pass their own.
    /// </summary>
    public class ToolSearchLocations
    {
        /// <summary>Folder where GameVault downloads tools to. Tools found there can be deleted.</summary>
        public string ManagedDirectory { get; init; } = "";
        /// <summary>Folders whose sub folders are Proton builds (compatibilitytools.d, steamapps/common).</summary>
        public List<(string Directory, string Source)> ProtonParents { get; init; } = new();
        /// <summary>Folders whose sub folders are Wine builds (e.g. Lutris runners).</summary>
        public List<(string Directory, string Source)> WineParents { get; init; } = new();

        public static ToolSearchLocations ForUser(string home, string managedDirectory)
        {
            var steamRoots = new[]
            {
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
            };
            var locations = new ToolSearchLocations { ManagedDirectory = managedDirectory };
            locations.ProtonParents.Add((managedDirectory, "GameVault"));
            foreach (string steam in steamRoots)
            {
                locations.ProtonParents.Add((Path.Combine(steam, "compatibilitytools.d"), "Steam"));
                locations.ProtonParents.Add((Path.Combine(steam, "steamapps", "common"), "Steam"));
            }
            string flatpakSteam = Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
            locations.ProtonParents.Add((Path.Combine(flatpakSteam, "compatibilitytools.d"), "Steam (Flatpak)"));
            locations.ProtonParents.Add((Path.Combine(flatpakSteam, "steamapps", "common"), "Steam (Flatpak)"));

            locations.WineParents.Add((managedDirectory, "GameVault"));
            locations.WineParents.Add((Path.Combine(home, ".local", "share", "lutris", "runners", "wine"), "Lutris"));
            locations.WineParents.Add((Path.Combine(home, ".var", "app", "net.lutris.Lutris", "data", "lutris", "runners", "wine"), "Lutris (Flatpak)"));
            return locations;
        }
    }

    public static class CompatibilityToolScanner
    {
        /// <summary>
        /// Lists the Proton and Wine builds found in <paramref name="locations"/>, without duplicates
        /// (~/.steam/root and ~/.local/share/Steam are usually the same folder), sorted newest first.
        /// </summary>
        public static List<CompatibilityTool> Scan(ToolSearchLocations locations)
        {
            var tools = new List<CompatibilityTool>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string managed = string.IsNullOrEmpty(locations.ManagedDirectory) ? "" : Path.GetFullPath(locations.ManagedDirectory);

            foreach (var (parent, source) in locations.ProtonParents)
            {
                foreach (string dir in SubDirectories(parent))
                {
                    if (!File.Exists(Path.Combine(dir, "proton")))
                        continue;
                    if (!seen.Add(RealPath(dir)))
                        continue;
                    tools.Add(new CompatibilityTool(CompatibilityToolId.ForProton(dir), ReadProtonName(dir), CompatibilityToolKind.Proton, dir, source, IsInside(dir, managed)));
                }
            }
            foreach (var (parent, source) in locations.WineParents)
            {
                foreach (string dir in SubDirectories(parent))
                {
                    if (!File.Exists(Path.Combine(dir, "bin", "wine")) || File.Exists(Path.Combine(dir, "proton")))
                        continue;
                    if (!seen.Add(RealPath(dir)))
                        continue;
                    tools.Add(new CompatibilityTool(CompatibilityToolId.ForWine(dir), Path.GetFileName(dir), CompatibilityToolKind.Wine, dir, source, IsInside(dir, managed)));
                }
            }
            return tools
                .OrderBy(t => t.Kind)
                .ThenByDescending(t => t.Name, NaturalComparer.Instance)
                .ToList();
        }

        /// <summary>
        /// Name shown for a Proton build: display_name of compatibilitytool.vdf (custom builds),
        /// else the folder name ("Proton 9.0" for Valve's builds).
        /// </summary>
        public static string ReadProtonName(string directory)
        {
            string vdf = Path.Combine(directory, "compatibilitytool.vdf");
            if (File.Exists(vdf))
            {
                try
                {
                    Match match = Regex.Match(File.ReadAllText(vdf), "\"display_name\"\\s+\"([^\"]+)\"");
                    if (match.Success)
                        return match.Groups[1].Value;
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            return Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        }

        private static IEnumerable<string> SubDirectories(string parent)
        {
            try
            {
                return Directory.Exists(parent) ? Directory.GetDirectories(parent) : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return Array.Empty<string>();
            }
        }

        private static string RealPath(string dir)
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(ResolveLinks(new DirectoryInfo(Path.GetFullPath(dir))));
            }
            catch
            {
                return dir;
            }
        }

        private static string ResolveLinks(DirectoryInfo directory)
        {
            // The folder or one of its parents may be a symlink (e.g. ~/.steam/root -> ~/.local/share/Steam)
            var parts = new Stack<string>();
            DirectoryInfo? current = directory;
            while (current != null)
            {
                FileSystemInfo? target = current.LinkTarget != null ? current.ResolveLinkTarget(true) : null;
                if (target != null)
                {
                    string path = target.FullName;
                    while (parts.Count > 0)
                        path = Path.Combine(path, parts.Pop());
                    return path;
                }
                parts.Push(current.Name);
                current = current.Parent;
            }
            return directory.FullName;
        }

        private static bool IsInside(string dir, string parent)
        {
            if (string.IsNullOrEmpty(parent))
                return false;
            string full = Path.GetFullPath(dir);
            return full.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
    }

    /// <summary>Orders "GE-Proton10-9" before "GE-Proton10-10" (numbers compared by value).</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x == null || y == null)
                return string.Compare(x, y, StringComparison.Ordinal);
            var px = Regex.Split(x, "([0-9]+)");
            var py = Regex.Split(y, "([0-9]+)");
            for (int i = 0; i < Math.Min(px.Length, py.Length); i++)
            {
                int result = long.TryParse(px[i], out long nx) && long.TryParse(py[i], out long ny)
                    ? nx.CompareTo(ny)
                    : string.Compare(px[i], py[i], StringComparison.OrdinalIgnoreCase);
                if (result != 0)
                    return result;
            }
            return px.Length.CompareTo(py.Length);
        }
    }
}
