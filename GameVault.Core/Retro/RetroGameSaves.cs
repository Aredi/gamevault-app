using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// The cloud saves of one retro game, as the library has them per game. Its files are found two ways: by name
    /// (RetroArch names the saves and states after the game's file: "Zelda (E).srm", "Zelda (E).state1"), and by what
    /// changed while it ran (memory cards, a console's storage: Dolphin, PCSX2, Azahar), remembered in a list. They go
    /// to the server as one zip when they changed since the last sync, and come back when the server's are newer;
    /// what is replaced is first kept in a backup zip.
    /// </summary>
    public sealed class RetroGameSaves
    {
        /// <summary>Rebuilt by the emulators (shader caches, logs): never sent.</summary>
        private static readonly string[] Skipped = { "Cache", "shaders", "Logs", "log", CloudFolderName };
        private const string CloudFolderName = ".cloud";
        private const int KeptBackups = 5;

        private readonly (string Prefix, string Folder)[] roots;
        private readonly string cloud, backups, set, romStem;

        /// <param name="set">The game's name on the server ("nes-40").</param>
        /// <param name="romStem">The game's file without extension: the saves are named after it.</param>
        public RetroGameSaves(string savesFolder, string statesFolder, string backupFolder, string set, string romStem)
        {
            roots = new[] { ("saves", savesFolder), ("states", statesFolder) };
            cloud = Path.Combine(savesFolder, CloudFolderName);
            backups = backupFolder;
            this.set = set;
            this.romStem = romStem;
        }

        public string Set => set;

        private sealed record State(long ServerSavedAt, string Fingerprint);

        private string StateFile => Path.Combine(cloud, $"{set}.json");
        private string ListFile => Path.Combine(cloud, $"{set}.files.json");

        private State Read()
        {
            try { return JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) ?? new State(0, ""); }
            catch (Exception ex) when (ex is IOException or JsonException) { return new State(0, ""); }
        }

        private HashSet<string> Listed()
        {
            try { return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(ListFile)) ?? new(); }
            catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
        }

        private void List(IEnumerable<string> entries)
        {
            var listed = Listed();
            bool added = false;
            foreach (string entry in entries)
                added |= listed.Add(entry);
            if (!added)
                return;
            Directory.CreateDirectory(cloud);
            File.WriteAllText(ListFile, JsonSerializer.Serialize(listed));
        }

        private bool IsSkipped(string folder, string file) => Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar).Any(part => Skipped.Contains(part));

        private bool NamedAfterGame(string file)
        {
            string name = Path.GetFileName(file);
            return romStem.Length > 0 && (string.Equals(Path.GetFileNameWithoutExtension(name), romStem, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(romStem + ".", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The game's files: (entry in the zip, full path).</summary>
        private List<(string Entry, string File)> Entries()
        {
            var listed = Listed();
            var files = new List<(string, string)>();
            foreach (var (prefix, folder) in roots)
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    if (IsSkipped(folder, file))
                        continue;
                    string entry = $"{prefix}/{Path.GetRelativePath(folder, file).Replace('\\', '/')}";
                    if (NamedAfterGame(file) || listed.Contains(entry))
                        files.Add((entry, file));
                }
            }
            return files.OrderBy(f => f.Item1, StringComparer.Ordinal).ToList();
        }

        public bool HasFiles => Entries().Count > 0;
        /// <summary>The paths of the game's save files on this PC.</summary>
        public IReadOnlyList<string> Files() => Entries().Select(e => e.File).ToList();

        /// <summary>What the game's saves are now: their paths, sizes and times.</summary>
        public string Fingerprint()
        {
            var text = new StringBuilder();
            foreach (var (entry, file) in Entries())
            {
                var info = new FileInfo(file);
                text.Append(entry).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        public bool ChangedSinceSync => Fingerprint() != Read().Fingerprint;
        public bool IsOlderThan(long serverSavedAt) => serverSavedAt > Read().ServerSavedAt;
        /// <summary>When these saves were last sent or taken (unix ms); 0 never.</summary>
        public long LastSync => Read().ServerSavedAt;

        /// <summary>The time of every file of the folders, before the game starts.</summary>
        public Dictionary<string, long> Snapshot()
        {
            var times = new Dictionary<string, long>();
            foreach (var (_, folder) in roots.Where(r => Directory.Exists(r.Folder)))
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    times[file] = File.GetLastWriteTimeUtc(file).Ticks;
            return times;
        }

        /// <summary>After the game: what it wrote (outside the files named after it) belongs to its saves from now on.</summary>
        public void Track(Dictionary<string, long> before)
        {
            var written = new List<string>();
            foreach (var (prefix, folder) in roots.Where(r => Directory.Exists(r.Folder)))
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    if (!IsSkipped(folder, file) && !NamedAfterGame(file) && (!before.TryGetValue(file, out long time) || time != File.GetLastWriteTimeUtc(file).Ticks))
                        written.Add($"{prefix}/{Path.GetRelativePath(folder, file).Replace('\\', '/')}");
            List(written);
        }

        public void Pack(Stream output)
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var (entry, file) in Entries())
                zip.CreateEntryFromFile(file, entry, CompressionLevel.Fastest);
        }

        /// <summary>The server's saves of the game in place of these, which go to a backup first.</summary>
        /// <returns>The backup made, or null when there was nothing to keep.</returns>
        public string? Unpack(Stream zip)
        {
            string? backup = null;
            var current = Entries();
            if (current.Count > 0)
            {
                Directory.CreateDirectory(backups);
                backup = Path.Combine(backups, $"{set}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
                using (FileStream target = File.Create(backup))
                    Pack(target);
                foreach (string old in Directory.GetFiles(backups, $"{set}-*.zip").OrderByDescending(f => f, StringComparer.Ordinal).Skip(KeptBackups))
                    File.Delete(old);
                foreach (var (_, file) in current)
                    File.Delete(file);
            }
            var extracted = new List<string>();
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.Name.Length > 0))
            {
                string[] parts = entry.FullName.Split('/', 2);
                var root = roots.FirstOrDefault(r => r.Prefix == parts[0]);
                if (root.Folder == null || parts.Length < 2)
                    continue;
                string folder = Path.GetFullPath(root.Folder) + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(Path.Combine(folder, parts[1]));
                // A zip from the network does not write outside the saves folders
                if (!target.StartsWith(folder, StringComparison.Ordinal))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                extracted.Add(entry.FullName);
            }
            // Files not named after the game (a memory card) are its saves on this PC too
            List(extracted.Where(e => !NamedAfterGame(e)));
            return backup;
        }

        /// <summary>After a sync: these saves are the server's of that time.</summary>
        public void Remember(long serverSavedAt)
        {
            Directory.CreateDirectory(cloud);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(new State(serverSavedAt, Fingerprint())));
        }
    }
}
