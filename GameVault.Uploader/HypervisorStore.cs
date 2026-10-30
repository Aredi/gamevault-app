using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The games an administrator marked as using a hypervisor, and the one text shown to the players downloading them.
    /// One JSON file for all, written whole.
    /// </summary>
    public sealed class HypervisorStore
    {
        private readonly string file;
        private readonly object writing = new();

        public HypervisorStore(UploaderOptions options, ILogger<HypervisorStore> logger)
        {
            file = Path.Combine(options.HypervisorDirectory, "hypervisor.json");
            Enabled = ProfileStore.IsWritable(options.HypervisorDirectory, out string error);
            if (!Enabled)
                logger.LogWarning("Hypervisor notices are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", options.HypervisorDirectory, error);
        }

        public bool Enabled { get; }

        public sealed record Notice(List<int> Games, string Text);

        public Notice Get()
        {
            lock (writing)
                return File.Exists(file) ? JsonSerializer.Deserialize<Notice>(File.ReadAllText(file)) ?? new(new(), "") : new(new(), "");
        }

        public void SetGame(int gameId, bool on)
        {
            lock (writing)
            {
                Notice notice = Get();
                notice.Games.Remove(gameId);
                if (on)
                    notice.Games.Add(gameId);
                Write(notice);
            }
        }

        public void SetText(string text)
        {
            lock (writing)
                Write(Get() with { Text = text });
        }

        private void Write(Notice notice)
        {
            // Written aside then moved: never half written
            string part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(notice));
            File.Move(part, file, overwrite: true);
        }
    }
}
