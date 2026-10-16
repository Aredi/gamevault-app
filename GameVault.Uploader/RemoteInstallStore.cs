using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// "Install on my PC", asked from the web page (a phone...): one list of games per player, which the player's
    /// SanctuaryVault client takes, downloads and removes. One JSON file per player, written whole.
    /// </summary>
    public sealed class RemoteInstallStore
    {
        public const int MaxPerUser = 20;

        private readonly string directory;
        private readonly object writing = new();

        public RemoteInstallStore(UploaderOptions options, ILogger<RemoteInstallStore> logger)
        {
            directory = options.RemoteInstallsDirectory;
            Enabled = ProfileStore.IsWritable(directory, out string error);
            if (!Enabled)
                logger.LogWarning("Remote installs are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", directory, error);
        }

        public bool Enabled { get; }

        public sealed record Entry(int GameId, string Title, DateTime RequestedAt);

        private string FileOf(int userId) => Path.Combine(directory, $"{userId}.json");

        public List<Entry> List(int userId)
        {
            lock (writing)
            {
                string file = FileOf(userId);
                return Enabled && File.Exists(file) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(file)) ?? new() : new();
            }
        }

        public enum AddResult { Added, AlreadyAsked, Full, Disabled }

        public AddResult Add(int userId, int gameId, string title)
        {
            if (!Enabled)
                return AddResult.Disabled;
            lock (writing)
            {
                List<Entry> entries = List(userId);
                if (entries.Any(e => e.GameId == gameId))
                    return AddResult.AlreadyAsked;
                if (entries.Count >= MaxPerUser)
                    return AddResult.Full;
                entries.Add(new Entry(gameId, title, DateTime.UtcNow));
                Write(userId, entries);
                return AddResult.Added;
            }
        }

        public void Remove(int userId, int gameId)
        {
            if (!Enabled)
                return;
            lock (writing)
            {
                List<Entry> entries = List(userId);
                if (entries.RemoveAll(e => e.GameId == gameId) > 0)
                    Write(userId, entries);
            }
        }

        private void Write(int userId, List<Entry> entries)
        {
            // Written aside then moved: never half written
            string file = FileOf(userId), part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(entries));
            File.Move(part, file, overwrite: true);
        }
    }
}
