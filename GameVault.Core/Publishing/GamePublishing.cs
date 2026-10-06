using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GameVault.Core.Publishing
{
    /// <summary>The type tags the GameVault server reads from file names.</summary>
    public enum PublishedGameType
    {
        /// <summary>(W_P) unpack and play.</summary>
        WindowsPortable,
        /// <summary>(W_S) runs an installer.</summary>
        WindowsSetup,
        /// <summary>(L_P) native Linux game.</summary>
        LinuxPortable,
    }

    public enum InstallerKind
    {
        Unknown,
        InnoSetup,
        Nsis,
        Msi,
        InstallShield,
    }

    public record InstallerInfo(InstallerKind Kind, string? SilentParameters)
    {
        public string Description => Kind switch
        {
            InstallerKind.InnoSetup => "Inno Setup installer: installs silently",
            InstallerKind.Nsis => "NSIS installer: installs silently",
            InstallerKind.Msi => "Windows Installer package (MSI): installs silently",
            InstallerKind.InstallShield => "InstallShield installer: silent installation is not reliable, the user follows the wizard",
            _ => "Unknown installer: the user follows the wizard",
        };
    }

    public static class GameFileName
    {
        public static string Tag(PublishedGameType type) => type switch
        {
            PublishedGameType.WindowsSetup => "W_S",
            PublishedGameType.LinuxPortable => "L_P",
            _ => "W_P",
        };

        /// <summary>
        /// "Title (v1.2) (EA) (W_P) (2020).zip" as the server expects it. Parentheses in the title would be read as
        /// tags, so they become brackets; characters not allowed in file names are removed.
        /// </summary>
        public static string Build(string title, string? version, PublishedGameType type, int? year, bool earlyAccess, string extension = ".zip")
        {
            string clean = title.Replace('(', '[').Replace(')', ']');
            foreach (char c in Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct())
                clean = clean.Replace(c.ToString(), "");
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            if (clean.Length == 0)
                throw new ArgumentException("The title is empty.");
            var name = new StringBuilder(clean);
            if (!string.IsNullOrWhiteSpace(version))
            {
                string v = version.Trim().TrimStart('v', 'V').Replace('(', '[').Replace(')', ']');
                name.Append($" (v{v})");
            }
            if (earlyAccess)
                name.Append(" (EA)");
            name.Append($" ({Tag(type)})");
            if (year is >= 1950 and <= 2100)
                name.Append($" ({year})");
            return name + extension;
        }

        /// <summary>
        /// The name that describes the game: the file name, or the folder name for generic installers
        /// ("Nile Adventure (2021)/setup.exe" → "Nile Adventure (2021)").
        /// </summary>
        public static string DescriptiveName(string path)
        {
            path = path.TrimEnd('/', '\\');
            string name = Path.GetFileName(path);
            if (Regex.IsMatch(name, @"^(setup|install|installer|autorun|start|launcher|game)(\.(exe|msi))?$", RegexOptions.IgnoreCase)
                && Path.GetFileName(Path.GetDirectoryName(path) ?? "") is string parent && parent.Length > 0)
                return parent;
            return name;
        }

        /// <summary>A readable title from a folder or file name ("Hades_v1.38-GOG" → "Hades").</summary>
        public static string GuessTitle(string folderOrFileName)
        {
            string name = DescriptiveName(folderOrFileName);
            // Only real extensions: "A.Short.Hike" is a name, not "A.Short" + ".Hike"
            name = Regex.Replace(name, @"\.(zip|7z|rar|iso|exe|msi|tar|gz|xz|bz2|tar\.gz|tar\.xz)$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\.tar$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"[\(\[].*?[\)\]]", " ");// (2020), [GOG], ...
            name = Regex.Replace(name, @"[\s._-]+v?\d+([._]\d+)+.*$", "", RegexOptions.IgnoreCase);// trailing version (and what follows)
            name = Regex.Replace(name, @"[._]+", " ").Trim();
            name = Regex.Replace(name, @"[-\s]+(GOG|Steam|Portable|Setup|Installer|Win(64|32)?|x64|x86)$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(name, @"\s+", " ").Trim(' ', '-');
        }
    }

    public static class InstallerDetector
    {
        /// <summary>Recognizes common installer builders from the executable's content.</summary>
        public static InstallerInfo Detect(string file)
        {
            if (Path.GetExtension(file).Equals(".msi", StringComparison.OrdinalIgnoreCase))
                return new InstallerInfo(InstallerKind.Msi, "/qn TARGETDIR=\"%INSTALLDIR%\"");
            try
            {
                // The markers are in the first megabytes of the stub
                using FileStream stream = File.OpenRead(file);
                byte[] buffer = new byte[Math.Min(stream.Length, 8 * 1024 * 1024)];
                int read = stream.Read(buffer, 0, buffer.Length);
                string text = Encoding.Latin1.GetString(buffer, 0, read);
                if (text.Contains("Inno Setup Setup Data") || text.Contains("Inno Setup"))
                    return new InstallerInfo(InstallerKind.InnoSetup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR=\"%INSTALLDIR%\"");
                if (text.Contains("Nullsoft.NSIS") || text.Contains("NullsoftInst"))
                    return new InstallerInfo(InstallerKind.Nsis, "/S /D=%INSTALLDIR%");// /D must come last and stay unquoted
                if (text.Contains("InstallShield"))
                    return new InstallerInfo(InstallerKind.InstallShield, null);
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return new InstallerInfo(InstallerKind.Unknown, null);
        }
    }

    public record ExecutableCandidate(string RelativePath, long Size, int Score);

    public static class ExecutableFinder
    {
        private static readonly string[] SkippedFolders = { "redist", "_commonredist", "commonredist", "directx", "dotnet", "vcredist", "support", "prerequisites", "__installer", "installers", "engine/extras", "_redist" };
        private static readonly string[] InstallerNames = { "setup", "install", "installer", "unins", "uninstall", "autorun" };

        /// <summary>
        /// The executables of a folder, most likely main game executable first: names close to the title,
        /// shallow and large files win; launchers, crash reporters and redistributables (ignore list) lose.
        /// </summary>
        public static List<ExecutableCandidate> Rank(string folder, string title, IEnumerable<string> ignoredNames, bool linux = false)
        {
            var ignored = new HashSet<string>(ignoredNames.Select(n => n.ToLowerInvariant()));
            string[] titleWords = Words(title);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            var patterns = linux ? new[] { "*.sh", "*.x86_64", "*.AppImage" } : new[] { "*.exe" };
            var result = new List<ExecutableCandidate>();
            foreach (string pattern in patterns)
            {
                foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles(pattern, options))
                {
                    string relative = Path.GetRelativePath(folder, file.FullName).Replace('\\', '/');
                    string name = Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant();
                    string lowerPath = relative.ToLowerInvariant();
                    int score = 0;
                    if (ignored.Contains(name) || InstallerNames.Any(n => name.StartsWith(n)))
                        score -= 100;
                    if (SkippedFolders.Any(f => lowerPath.Contains(f + "/")))
                        score -= 80;
                    if (Regex.IsMatch(name, "crash|report|launcher|update|helper|config|settings|server|editor|benchmark"))
                        score -= 25;
                    string[] nameWords = Words(name);
                    score += 20 * titleWords.Count(w => nameWords.Contains(w) || name.Contains(w));
                    if (titleWords.Length > 0 && string.Concat(titleWords) == string.Concat(nameWords))
                        score += 30;
                    score -= 5 * (relative.Count(c => c == '/'));// shallow first
                    if (Regex.IsMatch(name, "(win64|x64|shipping)"))
                        score += 10;
                    score += (int)Math.Min(30, file.Length / (5 * 1024 * 1024));// up to 30 points for 150 MB
                    result.Add(new ExecutableCandidate(relative, file.Length, score));
                }
            }
            return result.OrderByDescending(c => c.Score).ThenByDescending(c => c.Size).ToList();
        }

        /// <summary>Installers at the top of a folder (setup.exe, install.exe or a recognized installer).</summary>
        public static List<string> FindInstallers(string folder)
        {
            var found = new List<string>();
            foreach (string file in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                         .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
            {
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                bool named = InstallerNames.Take(3).Any(n => name.StartsWith(n));
                if (named || InstallerDetector.Detect(file).Kind != InstallerKind.Unknown)
                    found.Add(Path.GetFileName(file));
            }
            // "setup.exe" before other installers
            return found.OrderBy(f => f.StartsWith("setup", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
        }

        private static string[] Words(string text) =>
            Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+").Where(w => w.Length > 1 && w != "the").ToArray();
    }

    public static class GamePackager
    {
        /// <summary>
        /// Zips <paramref name="sourceFolder"/> into <paramref name="targetFile"/>. The archive is written under a
        /// temporary name and renamed at the end, so the server never indexes a half written file.
        /// Games are mostly compressed already: storing is much faster for little size difference.
        /// </summary>
        public static async Task CreateArchiveAsync(string sourceFolder, string targetFile, bool compress, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            string partial = targetFile + ".partial";
            var files = new DirectoryInfo(sourceFolder).GetFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint });
            long total = Math.Max(1, files.Sum(f => f.Length));
            long done = 0;
            try
            {
                await using (FileStream output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
                {
                    byte[] buffer = new byte[1 << 20];
                    foreach (FileInfo file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string entryName = Path.GetRelativePath(sourceFolder, file.FullName).Replace('\\', '/');
                        ZipArchiveEntry entry = zip.CreateEntry(entryName, compress ? CompressionLevel.Fastest : CompressionLevel.NoCompression);
                        entry.LastWriteTime = file.LastWriteTime;
                        await using Stream target = entry.Open();
                        await using FileStream source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
                        int read;
                        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            done += read;
                            progress?.Report((double)done / total);
                        }
                    }
                }
                File.Move(partial, targetFile, true);
            }
            catch
            {
                try { File.Delete(partial); } catch { }
                throw;
            }
        }

        /// <summary>Copies an archive or ISO that is published as it is, with the same temporary-name protection.</summary>
        public static async Task CopyFileAsync(string sourceFile, string targetFile, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            string partial = targetFile + ".partial";
            try
            {
                long total = Math.Max(1, new FileInfo(sourceFile).Length);
                long done = 0;
                byte[] buffer = new byte[1 << 20];
                await using (FileStream source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
                await using (FileStream target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        done += read;
                        progress?.Report((double)done / total);
                    }
                }
                File.Move(partial, targetFile, true);
            }
            catch
            {
                try { File.Delete(partial); } catch { }
                throw;
            }
        }
    }
}
