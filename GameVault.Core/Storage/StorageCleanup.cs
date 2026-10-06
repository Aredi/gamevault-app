using System.Text.RegularExpressions;

namespace GameVault.Core.Storage
{
    public enum CleanupKind
    {
        /// <summary>Downloaded archive of a game that is installed: only needed to reinstall without downloading.</summary>
        InstalledGameArchive,
        /// <summary>Downloaded but never installed.</summary>
        NotInstalledDownload,
        /// <summary>Separate Wine prefix of a game that is no longer installed.</summary>
        OrphanPrefix,
        /// <summary>Proton/Wine build downloaded by GameVault that no game uses.</summary>
        UnusedCompatibilityTool,
    }

    public record CleanupCandidate(CleanupKind Kind, string Title, string Path, long Size)
    {
        /// <summary>Safe items are selected by default; downloads waiting for installation are not.</summary>
        public bool SelectedByDefault => Kind != CleanupKind.NotInstalledDownload;
    }

    public class CleanupInputs
    {
        /// <summary>Root directories of the library (each contains GameVault/Downloads and GameVault/Installations).</summary>
        public IReadOnlyList<string> RootDirectories { get; init; } = Array.Empty<string>();
        /// <summary>Ids of the installed games.</summary>
        public IReadOnlySet<int> InstalledGameIds { get; init; } = new HashSet<int>();
        /// <summary>Ids of the games being downloaded right now (never offered).</summary>
        public IReadOnlySet<int> ActiveDownloadIds { get; init; } = new HashSet<int>();
        /// <summary>~/.local/share/GameVault/prefixes (Linux), null elsewhere.</summary>
        public string? PrefixesDirectory { get; init; }
        /// <summary>~/.local/share/GameVault/compatibilitytools.d (Linux), null elsewhere.</summary>
        public string? ManagedToolsDirectory { get; init; }
        /// <summary>Tool folders used by an installed game or as the default tool.</summary>
        public IReadOnlySet<string> UsedToolDirectories { get; init; } = new HashSet<string>();
    }

    public static class StorageCleanup
    {
        private static readonly Regex GameFolder = new(@"^\((\d+)\)(.*)$");

        public static List<CleanupCandidate> Scan(CleanupInputs inputs)
        {
            var result = new List<CleanupCandidate>();
            var installedIds = new HashSet<int>(inputs.InstalledGameIds);
            installedIds.UnionWith(FindInstallations(inputs.RootDirectories).Select(i => i.Id));
            foreach (string root in inputs.RootDirectories)
            {
                string downloads = Path.Combine(root, "GameVault", "Downloads");
                foreach (string dir in SubDirectories(downloads))
                {
                    Match match = GameFolder.Match(Path.GetFileName(dir));
                    if (!match.Success)
                        continue;
                    int id = int.Parse(match.Groups[1].Value);
                    if (inputs.ActiveDownloadIds.Contains(id))
                        continue;
                    long size = DirectorySize(dir);
                    if (size == 0)
                        continue;
                    var kind = installedIds.Contains(id) ? CleanupKind.InstalledGameArchive : CleanupKind.NotInstalledDownload;
                    result.Add(new CleanupCandidate(kind, match.Groups[2].Value, dir, size));
                }
            }
            if (inputs.PrefixesDirectory != null)
            {
                foreach (string dir in SubDirectories(inputs.PrefixesDirectory))
                {
                    if (int.TryParse(Path.GetFileName(dir), out int id) && !installedIds.Contains(id))
                        result.Add(new CleanupCandidate(CleanupKind.OrphanPrefix, $"Game #{id}", dir, DirectorySize(dir)));
                }
            }
            if (inputs.ManagedToolsDirectory != null)
            {
                var used = new HashSet<string>(inputs.UsedToolDirectories.Select(Normalize));
                foreach (string dir in SubDirectories(inputs.ManagedToolsDirectory))
                {
                    if (Path.GetFileName(dir).StartsWith('.') || used.Contains(Normalize(dir)))
                        continue;
                    result.Add(new CleanupCandidate(CleanupKind.UnusedCompatibilityTool, Path.GetFileName(dir), dir, DirectorySize(dir)));
                }
            }
            return result.OrderBy(c => c.Kind).ThenByDescending(c => c.Size).ToList();
        }

        /// <summary>Game installations on disk: non-empty "(id)Title" folders in GameVault/Installations of each root.</summary>
        public static List<(int Id, string Directory)> FindInstallations(IEnumerable<string> rootDirectories)
        {
            var result = new List<(int, string)>();
            foreach (string root in rootDirectories)
            {
                foreach (string dir in SubDirectories(Path.Combine(root, "GameVault", "Installations")))
                {
                    Match match = GameFolder.Match(Path.GetFileName(dir));
                    try
                    {
                        if (match.Success && Directory.EnumerateFileSystemEntries(dir).Any())
                            result.Add((int.Parse(match.Groups[1].Value), dir));
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
            }
            return result;
        }

        public static long DirectorySize(string directory)
        {
            long size = 0;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", options))
                    size += file.Length;
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return size;
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
        }

        private static IEnumerable<string> SubDirectories(string parent)
        {
            try { return Directory.Exists(parent) ? Directory.GetDirectories(parent) : Array.Empty<string>(); }
            catch (Exception ex) { Log.Ignored(ex); return Array.Empty<string>(); }
        }

        private static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    }
}
