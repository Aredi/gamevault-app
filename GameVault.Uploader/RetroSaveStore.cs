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

        private readonly string directory;
        private readonly ILogger<RetroSaveStore> logger;

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
                File.Move(part, file, overwrite: true);
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
