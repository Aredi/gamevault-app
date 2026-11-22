using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The passwords of the games' archives, set by an administrator on a game's page: the clients extract with them
    /// without asking. One JSON file for all, written whole; a player reads only the game's he downloads.
    /// </summary>
    public sealed class ArchivePasswordStore
    {
        private readonly string file;
        private readonly object writing = new();
        public const int MaxLength = 500;

        public ArchivePasswordStore(UploaderOptions options, ILogger<ArchivePasswordStore> logger)
        {
            file = Path.Combine(options.ArchivePasswordsDirectory, "archive-passwords.json");
            Enabled = ProfileStore.IsWritable(options.ArchivePasswordsDirectory, out string error);
            if (!Enabled)
                logger.LogWarning("Archive passwords are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", options.ArchivePasswordsDirectory, error);
        }

        public bool Enabled { get; }

        private Dictionary<int, string> All() =>
            File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(file)) ?? new() : new();

        public string? Get(int gameId)
        {
            lock (writing)
                return All().TryGetValue(gameId, out string? password) ? password : null;
        }

        /// <summary>Empty: none any more.</summary>
        public void Set(int gameId, string? password)
        {
            lock (writing)
            {
                var all = All();
                if (string.IsNullOrEmpty(password))
                    all.Remove(gameId);
                else
                    all[gameId] = password;
                // Written aside then moved: never half written
                string part = file + ".part";
                File.WriteAllText(part, JsonSerializer.Serialize(all));
                File.Move(part, file, overwrite: true);
            }
        }
    }
}
