using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// The cloud saves of the retro games, on the client's side: a player's whole saves folder goes to the server as one
    /// zip when it changed since the last sync, and is replaced by the server's when that one is newer. What is replaced
    /// is first kept in a backup zip, so two PCs played on without network lose nothing.
    /// </summary>
    public sealed class RetroSaveSync(string folder, string backupFolder)
    {
        /// <summary>Rebuilt by the emulators (shader caches, logs): never sent.</summary>
        private static readonly string[] Skipped = { "Cache", "shaders", "Logs", "log" };
        private const int KeptBackups = 5;

        private sealed record State(long ServerSavedAt, string Fingerprint);

        private string StateFile => Path.Combine(folder, ".cloud-sync.json");

        private State Read()
        {
            try { return JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) ?? new State(0, ""); }
            catch (Exception ex) when (ex is IOException or JsonException) { return new State(0, ""); }
        }

        private IEnumerable<string> Files() => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => f != StateFile && !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(part => Skipped.Contains(part)))
            : Enumerable.Empty<string>();

        /// <summary>What the saves are now: their paths, sizes and times.</summary>
        public string Fingerprint()
        {
            var text = new StringBuilder();
            foreach (string file in Files().OrderBy(f => f, StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                text.Append(Path.GetRelativePath(folder, file)).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        public bool ChangedSinceSync => Fingerprint() != Read().Fingerprint;

        public bool IsOlderThan(long serverSavedAt) => serverSavedAt > Read().ServerSavedAt;

        public void Pack(Stream output)
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (string file in Files())
                zip.CreateEntryFromFile(file, Path.GetRelativePath(folder, file).Replace('\\', '/'), CompressionLevel.Fastest);
        }

        /// <summary>The server's saves in place of these, which go to a backup first.</summary>
        /// <returns>The backup made, or null when there was nothing to keep.</returns>
        public string? Unpack(Stream zip)
        {
            string? backup = null;
            if (Files().Any())
            {
                Directory.CreateDirectory(backupFolder);
                backup = Path.Combine(backupFolder, $"saves-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
                using (FileStream target = File.Create(backup))
                    Pack(target);
                foreach (string old in Directory.GetFiles(backupFolder, "saves-*.zip").OrderByDescending(f => f).Skip(KeptBackups))
                    File.Delete(old);
                foreach (string file in Files().ToList())
                    File.Delete(file);
            }
            Directory.CreateDirectory(folder);
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.Name.Length > 0))
            {
                string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                // A zip from the network does not write outside the saves folder
                if (!target.StartsWith(root, StringComparison.Ordinal))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            return backup;
        }

        /// <summary>After a sync: these saves are the server's of that time.</summary>
        public void Remember(long serverSavedAt)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(new State(serverSavedAt, Fingerprint())));
        }
    }
}
