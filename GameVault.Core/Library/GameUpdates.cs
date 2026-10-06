namespace GameVault.Core.Library
{
    /// <summary>What was installed: the server file of the game at installation time.</summary>
    public record InstalledVersion(string? Version, string? FilePath, string? Size);

    public static class GameUpdates
    {
        /// <summary>
        /// The server offers another build than the installed one: a new version (another file, GameVault 17 keeps
        /// all versions of a game and serves the newest) or the same file replaced (another size).
        /// Installations from before the file was recorded only compare the version text.
        /// </summary>
        public static bool IsUpdateAvailable(InstalledVersion installed, string? serverVersion, string? serverFilePath, string? serverSize)
        {
            if (!string.IsNullOrEmpty(installed.FilePath) && !string.IsNullOrEmpty(serverFilePath))
            {
                if (!SamePath(installed.FilePath, serverFilePath))
                    return true;
                return !string.IsNullOrEmpty(installed.Size) && !string.IsNullOrEmpty(serverSize) && installed.Size != serverSize;
            }
            return !string.IsNullOrWhiteSpace(installed.Version) && !string.IsNullOrWhiteSpace(serverVersion)
                && !string.Equals(installed.Version.Trim(), serverVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string a, string b) => a.Replace('\\', '/') == b.Replace('\\', '/');
    }

    /// <summary>
    /// The files a portable game installed ("gamevault-files" next to its settings). An update replaces them with
    /// the new version, deletes the ones the new version no longer has and leaves every other file alone:
    /// saves and settings the game wrote into its folder survive.
    /// </summary>
    public static class InstallManifest
    {
        public const string FileName = "gamevault-files";

        public static string PathIn(string installationDirectory) => Path.Combine(installationDirectory, FileName);

        /// <summary>Paths relative to <paramref name="filesDirectory"/>, with "/" separators.</summary>
        public static List<string> List(string filesDirectory) =>
            Directory.EnumerateFiles(filesDirectory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
                .Select(file => Path.GetRelativePath(filesDirectory, file).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

        public static void Write(string installationDirectory, IEnumerable<string> files)
        {
            string temp = PathIn(installationDirectory) + ".tmp";
            File.WriteAllLines(temp, files);
            File.Move(temp, PathIn(installationDirectory), true);
        }

        /// <summary>The files of the installed version, or null for installations made before manifests existed.</summary>
        public static List<string>? Read(string installationDirectory)
        {
            string file = PathIn(installationDirectory);
            return File.Exists(file) ? File.ReadAllLines(file).Where(line => line.Length > 0).ToList() : null;
        }

        /// <summary>
        /// Moves the extracted new version (<paramref name="newFiles"/>) over the installed one (<paramref name="installedFiles"/>).
        /// Files of the old version that are not in the new one are deleted (only when the old file list is known).
        /// Returns the file list of the new version.
        /// </summary>
        public static List<string> ApplyUpdate(string newFiles, string installedFiles, IReadOnlyCollection<string>? oldVersionFiles)
        {
            List<string> newVersionFiles = List(newFiles);
            var keep = new HashSet<string>(newVersionFiles, StringComparer.Ordinal);
            Directory.CreateDirectory(installedFiles);
            foreach (string relative in newVersionFiles)
            {
                string target = Path.Combine(installedFiles, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(Path.Combine(newFiles, relative), target, true);
            }
            if (oldVersionFiles != null)
            {
                foreach (string relative in oldVersionFiles.Where(file => !keep.Contains(file)))
                {
                    string obsolete = Path.GetFullPath(Path.Combine(installedFiles, relative));
                    // Only inside the installation, whatever the list says
                    if (!obsolete.StartsWith(Path.GetFullPath(installedFiles) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        continue;
                    try
                    {
                        if (File.Exists(obsolete))
                            File.Delete(obsolete);
                        RemoveEmptyParents(Path.GetDirectoryName(obsolete)!, installedFiles);
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
            }
            return newVersionFiles;
        }

        private static void RemoveEmptyParents(string directory, string root)
        {
            string stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            while (Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) != stop && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                directory = Path.GetDirectoryName(directory)!;
            }
        }
    }
}
