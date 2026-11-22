using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// The saves of the retro games, per GameVault player: the client sends the whole folder of an emulator as one zip
    /// when a game ends, and takes it back on another PC before a game starts. One file per player and set, written whole.
    /// </summary>
    public sealed partial class RetroSaveStore
    {
        public const long MaxSize = 1L << 30;
        /// <summary>The versions kept before the current one, to go back to.</summary>
        public const int KeptVersions = 10;
        /// <summary>A new set is refused past these: far more than any player's emulators, but a cap on a token's misuse.</summary>
        public const int MaxSets = 200;
        public const long MaxTotal = 20L << 30;

        private readonly string directory;
        private readonly ILogger<RetroSaveStore> logger;
        /// <summary>One per player and set: two saves at once would both move the same current file to the history.</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> moving = new();

        public RetroSaveStore(UploaderOptions options, ILogger<RetroSaveStore> logger)
        {
            directory = options.RetroSavesDirectory;
            this.logger = logger;
            Enabled = ProfileStore.IsWritable(directory, out string error);
            if (!Enabled)
                logger.LogWarning("Retro saves are off: {Directory} can not be written ({Error}). Mount a volume there to keep them.", directory, error);
        }

        public bool Enabled { get; }

        [GeneratedRegex("^[a-z0-9-]{1,40}$")]
        private static partial Regex SetPattern();

        public static bool IsValidSet(string set) => SetPattern().IsMatch(set);

        private string FileOf(int userId, string set) => Path.Combine(directory, userId.ToString(), $"{set}.zip");

        /// <summary>The set's file and when it was saved (unix ms); null when the player has none.</summary>
        public (string File, long SavedAt)? Get(int userId, string set)
        {
            string file = FileOf(userId, set);
            return Enabled && File.Exists(file) ? (file, new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds()) : null;
        }

        private string HistoryOf(int userId) => Path.Combine(directory, userId.ToString(), "history");

        public sealed record Version(string Name, long SavedAt, long Size);

        /// <summary>The older versions of a set, newest first.</summary>
        public List<Version> History(int userId, string set)
        {
            string history = HistoryOf(userId);
            if (!Enabled || !Directory.Exists(history))
                return new();
            return Directory.GetFiles(history, $"{set}-*.zip")
                .Select(f => (File: f, Stamp: Path.GetFileNameWithoutExtension(f)[(set.Length + 1)..]))
                .Where(f => long.TryParse(f.Stamp, out _))
                .Select(f => new Version(Path.GetFileName(f.File), long.Parse(f.Stamp), new FileInfo(f.File).Length))
                .OrderByDescending(v => v.SavedAt).ToList();
        }

        /// <summary>The file of an older version; null when there is no such version.</summary>
        public string? HistoryFile(int userId, string set, string name) =>
            History(userId, set).Any(v => v.Name == name) ? Path.Combine(HistoryOf(userId), name) : null;

        /// <summary>Why the player may not start this set (one they do not have yet); null when they may.</summary>
        public string? NewSetRefusal(int userId, string set)
        {
            string folder = Path.GetDirectoryName(FileOf(userId, set))!;
            // A set being saved is in its history for a moment, not at its place
            if (File.Exists(FileOf(userId, set)) || !Directory.Exists(folder) || History(userId, set).Count > 0)
                return null;
            if (Directory.GetFiles(folder, "*.zip").Length >= MaxSets)
                return $"You already have {MaxSets} sets of retro saves.";
            static long SizeOf(string file) { try { return new FileInfo(file).Length; } catch (FileNotFoundException) { return 0; } }
            if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(SizeOf) >= MaxTotal)
                return $"Your retro saves already take {MaxTotal >> 30} GB.";
            return null;
        }

        /// <returns>When it was saved (unix ms); null when it is larger than <see cref="MaxSize"/>.</returns>
        public async Task<long?> SaveAsync(int userId, string set, Stream body, CancellationToken cancellationToken)
        {
            string file = FileOf(userId, set);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // Written aside then moved: a set being read is never half written, and a cut upload leaves the old one
            string part = $"{file}.{Guid.NewGuid():N}.part";
            try
            {
                await using (FileStream target = File.Create(part))
                {
                    byte[] buffer = new byte[1 << 16];
                    int read;
                    while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        if (target.Length + read > MaxSize)
                            return null;
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
                SemaphoreSlim gate = moving.GetOrAdd($"{userId}/{set}", _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(CancellationToken.None);
                try
                {
                    // The current one goes to the history (named by when it was saved)
                    if (File.Exists(file))
                    {
                        string history = HistoryOf(userId);
                        Directory.CreateDirectory(history);
                        File.Move(file, Path.Combine(history, $"{set}-{new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds()}.zip"), overwrite: true);
                        // Its own versions only: "switch-2-*.zip" also names the Switch 2's sets ("switch-2-2777-...")
                        foreach (Version old in History(userId, set).Skip(KeptVersions))
                            File.Delete(Path.Combine(history, old.Name));
                    }
                    File.Move(part, file, overwrite: true);
                }
                finally { gate.Release(); }
            }
            finally
            {
                File.Delete(part);
            }
            logger.LogInformation("Retro saves '{Set}' of user {User} saved", set, userId);
            return new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds();
        }
    }
}
