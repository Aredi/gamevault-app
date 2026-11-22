using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// The mods of a Switch game for Eden (and its forks), as Eden reads them: user/load/&lt;game's id&gt;/&lt;mod&gt;/ with a
    /// romfs, exefs or cheats folder in it (src/core/file_sys/patch_manager.cpp). A mod turned off waits in
    /// user/load-off/, which Eden does not read; the mods put by SanctuaryVault are listed in one file beside them.
    /// </summary>
    public static partial class SwitchMods
    {
        /// <summary>A file of a mod (GameBanana's): a mod often has one per version of the game, or per variant.</summary>
        public sealed record ModFile(int Id, string Name, long Size, string? Description, long AddedAt);

        /// <summary>A mod of the game on this PC: its folder's name, the one shown; its GameBanana mod, when SanctuaryVault put it.</summary>
        public sealed record InstalledMod(string Folder, bool Enabled, int? ModId);

        private static readonly string[] Parts = { "romfs", "exefs", "cheats", "romfs_ext", "exefs_ext", "romfslite" };

        #region The game's id
        /// <summary>"[0100F2C0115B6000]" in a file's name, as most dumps are named: the base game's id.</summary>
        [GeneratedRegex(@"\[(01[0-9A-Fa-f]{14})\]")]
        private static partial Regex IdInName();

        /// <summary>
        /// The base game's id of an id: a game's is a multiple of 0x2000, its update's ends in 800, its other programs in
        /// 001..., its DLC's are 0x1000 after (0100670014483001 is 0100670014482000's).
        /// </summary>
        public static string BaseId(ulong id) => (id & ~0x1FFFUL).ToString("X16");

        public static string? TitleIdFromName(string path) =>
            IdInName().Match(Path.GetFileName(path)) is { Success: true } match ? BaseId(Convert.ToUInt64(match.Groups[1].Value, 16)) : null;

        /// <summary>The key the contents' headers are ciphered with, from the console's keys (prod.keys).</summary>
        public static byte[]? HeaderKey(string prodKeys)
        {
            if (!File.Exists(prodKeys))
                return null;
            foreach (string line in File.ReadLines(prodKeys))
            {
                string[] pair = line.Split('=', 2);
                if (pair.Length == 2 && pair[0].Trim().Equals("header_key", StringComparison.OrdinalIgnoreCase) && pair[1].Trim() is { Length: 64 } hex)
                {
                    try { return Convert.FromHexString(hex); }
                    catch (FormatException) { return null; }
                }
            }
            return null;
        }

        /// <summary>
        /// The game's id, as its mods' folder is named: from the file's name when it has it, else read in the game (an
        /// .nsp, an .xci): the id in the header of its first content, deciphered with the console's key.
        /// </summary>
        public static string? TitleIdOf(string gameFile, byte[]? headerKey)
        {
            if (TitleIdFromName(gameFile) is string named)
                return named;
            if (headerKey is not { Length: 32 })
                return null;
            string extension = Path.GetExtension(gameFile).ToLowerInvariant();
            if (extension is not (".nsp" or ".xci"))
                return null;
            try
            {
                using FileStream file = File.OpenRead(gameFile);
                return TitleIdOf(file, extension == ".xci", headerKey);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        internal static string? TitleIdOf(Stream package, bool card, byte[] headerKey)
        {
            var contents = (card ? SwitchCompressed.SecurePartition(package) : SwitchCompressed.Partition(package, 0, hashed: false))
                .Where(e => e.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase) && e.Size >= 0x400);
            foreach (SwitchCompressed.Entry content in contents)
            {
                var header = new byte[0x400];
                package.Seek(content.Offset, SeekOrigin.Begin);
                package.ReadExactly(header);
                byte[] plain = DecipherXts(headerKey, header, firstSector: 0);
                string magic = Encoding.ASCII.GetString(plain, 0x200, 4);
                if (magic is "NCA3" or "NCA2")
                    return BaseId(BinaryPrimitives.ReadUInt64LittleEndian(plain.AsSpan(0x210)));
            }
            return null;
        }

        /// <summary>AES-128-XTS on 0x200 sectors, the sector's number big-endian in its tweak (Nintendo's way).</summary>
        internal static byte[] DecipherXts(byte[] key, byte[] data, long firstSector, bool decipher = true)
        {
            using Aes data1 = Aes.Create(), tweak1 = Aes.Create();
            data1.Key = key[..16];
            tweak1.Key = key[16..32];
            var output = new byte[data.Length];
            var tweak = new byte[16];
            for (int sector = 0; sector * 0x200 < data.Length; sector++)
            {
                Array.Clear(tweak);
                BinaryPrimitives.WriteInt64BigEndian(tweak.AsSpan(8), firstSector + sector);
                tweak = tweak1.EncryptEcb(tweak, PaddingMode.None);
                for (int block = sector * 0x200; block < Math.Min(data.Length, (sector + 1) * 0x200); block += 16)
                {
                    var buffer = new byte[16];
                    for (int i = 0; i < 16; i++)
                        buffer[i] = (byte)(data[block + i] ^ tweak[i]);
                    buffer = decipher ? data1.DecryptEcb(buffer, PaddingMode.None) : data1.EncryptEcb(buffer, PaddingMode.None);
                    for (int i = 0; i < 16; i++)
                        output[block + i] = (byte)(buffer[i] ^ tweak[i]);
                    // The next block's tweak: times x in GF(2^128), little-endian
                    int carry = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        int next = tweak[i] >> 7;
                        tweak[i] = (byte)((tweak[i] << 1) | carry);
                        carry = next;
                    }
                    if (carry != 0)
                        tweak[0] ^= 0x87;
                }
            }
            return output;
        }
        #endregion

        #region The file for this game
        [GeneratedRegex(@"(?i)(?<![0-9a-z])v(\d+\.\d+(?:\.\d+)?)")]
        private static partial Regex VersionInName();

        /// <summary>The game's version, from its update's file's name ("... v1.2.1[0100...][v393216].nsp"): the newest; null when none says it.</summary>
        public static string? GameVersion(IEnumerable<string> files) => files
            .Select(f => VersionInName().Match(Path.GetFileName(f)))
            .Where(m => m.Success && Version.TryParse(m.Groups[1].Value, out _))
            .Select(m => m.Groups[1].Value)
            .OrderByDescending(v => Version.Parse(v))
            .FirstOrDefault();

        /// <summary>Whether a text names this version: "1.2.1", "1_2_1", "v121".</summary>
        public static bool Names(string text, string version)
        {
            string[] parts = version.Split('.');
            string apart = string.Join(@"[._ -]", parts.Select(Regex.Escape)), together = string.Concat(parts);
            return Regex.IsMatch(text, $@"(?i)(?<![0-9])({apart}|v{together})(?![0-9]|\.[0-9])");
        }

        /// <summary>
        /// The file to install for this version of the game: the one that names it, else one "for all versions", else
        /// the newest. The files made for another mod manager (TKMM) last.
        /// </summary>
        public static ModFile? BestFile(IReadOnlyList<ModFile> files, string? version) => files
            .OrderByDescending(f => Score(f, version))
            .ThenByDescending(f => f.AddedAt)
            .FirstOrDefault();

        private static int Score(ModFile file, string? version)
        {
            string text = file.Name + " " + file.Description;
            int score = 0;
            if (version != null && Names(text, version))
                score += 4;
            else if (Regex.IsMatch(text, @"(?i)\ball\b.*\b(versions?|updates?)\b"))
                score += 2;
            if (MadeForAnother(file, version) != null)
                score -= 4;
            if (NeedsAnotherManager(file))
                score -= 8;
            return score;
        }

        [GeneratedRegex(@"(?i)\b(?:game|update)\s+(?:version\s+)?v?(\d+\.\d+\.\d+)|\bversion\s+v?(\d+\.\d+\.\d+)\s+of\s+the\s+game")]
        private static partial Regex GameVersionInText();

        /// <summary>
        /// The game's version a file says it is made for ("for game version 1.4.2"), when it is not this one: its data
        /// does not fit this version and the game stops as it starts. Null when it names no game's version, or this one too;
        /// a mod's own version ("v1.8.0") is not the game's.
        /// </summary>
        public static string? MadeForAnother(ModFile file, string? version)
        {
            string text = file.Name + " " + file.Description;
            if (version == null || Names(text, version))
                return null;
            Match named = GameVersionInText().Match(text);
            return !named.Success ? null : named.Groups[1].Success ? named.Groups[1].Value : named.Groups[2].Value;
        }

        /// <summary>A file made for a mod manager of its own (TKMM's .tkcl), which Eden can not read as it is.</summary>
        public static bool NeedsAnotherManager(ModFile file) =>
            Regex.IsMatch(file.Name + " " + file.Description, @"(?i)\btkmm\b|\.tkcl\b") && !Regex.IsMatch(file.Description ?? "", @"(?i)\b(not|without)\b[^.]*\btkmm\b");
        #endregion

        #region On this PC
        public static string LoadDirectory(SwitchLayout layout, string titleId) => Path.Combine(layout.UserDirectory, "load", titleId);
        public static string OffDirectory(SwitchLayout layout, string titleId) => Path.Combine(layout.UserDirectory, "load-off", titleId);
        private static string ListFile(SwitchLayout layout) => Path.Combine(layout.UserDirectory, "sanctuaryvault-mods.json");

        private sealed record Listed(
            [property: JsonPropertyName("game")] string Game,
            [property: JsonPropertyName("folder")] string Folder,
            [property: JsonPropertyName("modId")] int ModId,
            [property: JsonPropertyName("fileId")] int FileId,
            [property: JsonPropertyName("title")] string Title);

        private static List<Listed> List(SwitchLayout layout)
        {
            try { return File.Exists(ListFile(layout)) ? JsonSerializer.Deserialize<List<Listed>>(File.ReadAllText(ListFile(layout))) ?? new() : new(); }
            catch (JsonException ex)
            {
                Log.Ignored(ex);
                return new();
            }
        }

        private static void Save(SwitchLayout layout, List<Listed> list)
        {
            Directory.CreateDirectory(layout.UserDirectory);
            File.WriteAllText(ListFile(layout) + ".part", JsonSerializer.Serialize(list));
            File.Move(ListFile(layout) + ".part", ListFile(layout), overwrite: true);
        }

        /// <summary>The game's mods here, the ones put by hand too: on, then off, by name.</summary>
        public static List<InstalledMod> Installed(SwitchLayout layout, string titleId)
        {
            // The last one put first: a list written by an older client may name a folder twice
            var listed = List(layout).Where(l => l.Game == titleId).Reverse()
                .GroupBy(l => l.Folder, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            IEnumerable<InstalledMod> In(string directory, bool enabled) => !Directory.Exists(directory) ? Enumerable.Empty<InstalledMod>()
                : Directory.EnumerateDirectories(directory).Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .Select(n => new InstalledMod(n, enabled, listed.TryGetValue(n, out Listed? l) ? l.ModId : null));
            return In(LoadDirectory(layout, titleId), true).Concat(In(OffDirectory(layout, titleId), false)).ToList();
        }

        /// <summary>
        /// The folders of an archive opened that are mods: the ones with a romfs, exefs or cheats folder in them (the
        /// archive's top itself, a mod's folder, atmosphere/contents/&lt;id&gt;/), with the name they get here.
        /// </summary>
        public static List<(string Directory, string Name)> ModRoots(string extracted, string title)
        {
            var roots = new List<(string, string)>();
            void Walk(string directory, int depth)
            {
                bool isMod = Directory.EnumerateDirectories(directory).Any(d => Parts.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase));
                if (isMod)
                {
                    string name = Path.GetFileName(directory);
                    bool named = directory != extracted && !Regex.IsMatch(name, "^01[0-9A-Fa-f]{14}$");
                    roots.Add((directory, named ? name : title));
                    return;
                }
                if (depth < 6)
                    foreach (string child in Directory.EnumerateDirectories(directory).OrderBy(d => d, StringComparer.Ordinal))
                        Walk(child, depth + 1);
            }
            Walk(extracted, 0);
            // Several in one archive ("Amiibo_Equipment", "Unbreakable_Equipment"): their folder's name after the mod's
            if (roots.Count > 1)
                return roots.Select(r => (r.Item1, r.Item2 == title ? title : $"{title} · {r.Item2}")).ToList();
            return roots.Count == 1 ? new() { (roots[0].Item1, title) } : roots;
        }

        public static string FolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
            string clean = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray()).Trim().TrimEnd('.');
            clean = Regex.Replace(clean, @"\s+", " ");
            return clean.Length == 0 ? "Mod" : clean.Length > 80 ? clean[..80].Trim() : clean;
        }

        /// <summary>
        /// Puts the mods of an archive opened (on the same drive as Eden's folder) in the game's mods, all turned on: an
        /// archive's folders are most often parts that go together (Unbreakable Equipment's four). One installed again
        /// takes its place.
        /// </summary>
        /// <returns>The folders put; none when the archive holds no mod Eden reads.</returns>
        public static List<string> Install(SwitchLayout layout, string titleId, string extracted, string title, int modId, int fileId)
        {
            var roots = ModRoots(extracted, title);
            var list = List(layout);
            // The mod installed again: its previous file goes
            foreach (Listed old in list.Where(l => l.Game == titleId && l.ModId == modId).ToList())
            {
                Delete(Path.Combine(LoadDirectory(layout, titleId), old.Folder));
                Delete(Path.Combine(OffDirectory(layout, titleId), old.Folder));
                list.Remove(old);
            }
            var put = new List<string>();
            for (int i = 0; i < roots.Count; i++)
            {
                // Two of the archive's folders of one name ("1.2.1/UltraCam", "1.4.2/UltraCam"): the second is "UltraCam 2"
                string folder = FolderName(roots[i].Name);
                for (int n = 2; put.Contains(folder, StringComparer.OrdinalIgnoreCase); n++)
                    folder = FolderName($"{roots[i].Name} {n}");
                string target = Path.Combine(LoadDirectory(layout, titleId), folder);
                Delete(Path.Combine(LoadDirectory(layout, titleId), folder));
                Delete(Path.Combine(OffDirectory(layout, titleId), folder));
                // Another mod of this name (two "60 FPS"): this one takes its place
                list.RemoveAll(l => l.Game == titleId && l.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(roots[i].Directory, target);
                list.Add(new Listed(titleId, folder, modId, fileId, title));
                put.Add(folder);
            }
            Save(layout, list);
            return put;
        }

        /// <summary>Turns a mod on or off: its folder goes where Eden reads it, or beside.</summary>
        public static void SetEnabled(SwitchLayout layout, string titleId, string folder, bool enabled)
        {
            string on = Path.Combine(LoadDirectory(layout, titleId), FolderName(folder)), off = Path.Combine(OffDirectory(layout, titleId), FolderName(folder));
            (string from, string to) = enabled ? (off, on) : (on, off);
            if (!Directory.Exists(from))
                return;
            Delete(to);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
        }

        public static void Remove(SwitchLayout layout, string titleId, string folder)
        {
            Delete(Path.Combine(LoadDirectory(layout, titleId), FolderName(folder)));
            Delete(Path.Combine(OffDirectory(layout, titleId), FolderName(folder)));
            var list = List(layout);
            if (list.RemoveAll(l => l.Game == titleId && l.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)) > 0)
                Save(layout, list);
        }

        private static void Delete(string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        #endregion
    }
}
