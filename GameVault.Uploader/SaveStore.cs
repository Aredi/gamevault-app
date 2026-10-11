using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// The cloud saves the GameVault server keeps (SAVEFILES_MAX_SAVES per game, newest first), read from its savefiles
    /// volume mounted here read-only. The server only gives the newest one; the client restores an older one from here.
    /// Layout: {SAVEFILES_DIRECTORY}/users/{user}/games/{game}/{upload time in ms}_{installation id}.zip
    /// </summary>
    public sealed class SaveStore
    {
        private static readonly Regex SaveName = new(@"^(\d{10,16})_([0-9A-Za-z-]{1,64})\.zip$");
        private readonly string directory;

        public SaveStore(UploaderOptions options, ILogger<SaveStore> logger)
        {
            directory = options.SavefilesDirectory;
            Enabled = Directory.Exists(Path.Combine(directory, "users"));
            if (!Enabled)
                logger.LogInformation("Save history is off: {Directory} does not hold the saves of the GameVault server. Mount its savefiles volume there (read-only) to offer older saves.", directory);
        }

        public bool Enabled { get; }

        public sealed record SaveVersion(string Name, long SavedAt, long Size, string InstallationId);

        private string GameDirectory(int userId, int gameId) => Path.Combine(directory, "users", userId.ToString(), "games", gameId.ToString());

        /// <summary>The saves of a player for a game, the newest first.</summary>
        public List<SaveVersion> List(int userId, int gameId)
        {
            string folder = GameDirectory(userId, gameId);
            if (!Enabled || !Directory.Exists(folder))
                return new();
            return Directory.EnumerateFiles(folder, "*.zip")
                .Select(path => (Path: path, Match: SaveName.Match(Path.GetFileName(path))))
                .Where(f => f.Match.Success)
                .Select(f => new SaveVersion(Path.GetFileName(f.Path), long.Parse(f.Match.Groups[1].Value), new FileInfo(f.Path).Length, f.Match.Groups[2].Value))
                .OrderByDescending(s => s.SavedAt)
                .ToList();
        }

        /// <summary>One save of the player, by its file name only (never a path).</summary>
        public string? PathOf(int userId, int gameId, string name)
        {
            if (!Enabled || !SaveName.IsMatch(name))
                return null;
            string path = Path.Combine(GameDirectory(userId, gameId), name);
            return File.Exists(path) ? path : null;
        }
    }
}
