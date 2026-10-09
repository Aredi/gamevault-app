using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The players' profiles (showcases, favorite game, texts, colors...) chosen in the SanctuaryVault client: one JSON
    /// document per GameVault user, written whole. The client reads and checks its content; the store only keeps
    /// documents that are JSON objects of a reasonable size.
    /// </summary>
    public sealed class ProfileStore
    {
        public const int MaxSize = 64 * 1024;

        private readonly string directory;
        private readonly ILogger<ProfileStore> logger;
        private readonly object writing = new();

        public ProfileStore(UploaderOptions options, ILogger<ProfileStore> logger)
        {
            directory = options.ProfilesDirectory;
            this.logger = logger;
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                Enabled = true;
            }
            catch (Exception ex)
            {
                // Uploads keep working without the volume; the clients then show the profiles as they were
                logger.LogWarning("Profiles are off: {Directory} can not be written ({Error}). Mount a volume there to keep the players' profiles.", directory, ex.Message);
            }
        }

        public bool Enabled { get; }

        private string FileOf(int userId) => Path.Combine(directory, $"{userId}.json");

        public string? Get(int userId)
        {
            string file = FileOf(userId);
            return Enabled && File.Exists(file) ? File.ReadAllText(file) : null;
        }

        public enum SaveResult { Saved, TooLarge, NotAnObject, Disabled }

        public async Task<SaveResult> SaveAsync(int userId, Stream body, CancellationToken cancellationToken)
        {
            if (!Enabled)
                return SaveResult.Disabled;
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxSize)
                    return SaveResult.TooLarge;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return SaveResult.NotAnObject;
            }
            catch (JsonException)
            {
                return SaveResult.NotAnObject;
            }
            lock (writing)
            {
                // Written aside then moved: a profile being read is never half written
                string file = FileOf(userId), part = file + ".part";
                File.WriteAllBytes(part, buffer.ToArray());
                File.Move(part, file, overwrite: true);
            }
            logger.LogInformation("Profile of user {User} saved", userId);
            return SaveResult.Saved;
        }
    }
}
