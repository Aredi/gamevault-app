using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// What each player does with the retro games: the time played per game (shown on their profile and in the recent
    /// activity, as the GameVault progresses of the library's games) and their favorites. One JSON file per player.
    /// </summary>
    public sealed partial class RetroPlayerStore
    {
        private const int MaxFavorites = 2000;

        private readonly string directory;
        private readonly object writing = new();

        public RetroPlayerStore(UploaderOptions options, ILogger<RetroPlayerStore> logger)
        {
            directory = options.RetroPlayersDirectory;
            Enabled = ProfileStore.IsWritable(directory, out string error);
            if (!Enabled)
                logger.LogWarning("Retro play times are off: {Directory} can not be written ({Error}). Mount a volume there to keep them.", directory, error);
        }

        public bool Enabled { get; }

        public sealed class Play
        {
            [JsonPropertyName("key")] public string Key { get; set; } = "";
            [JsonPropertyName("romId")] public int RomId { get; set; }
            [JsonPropertyName("platform")] public string Platform { get; set; } = "";
            [JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("cover")] public string? Cover { get; set; }
            [JsonPropertyName("minutes")] public int Minutes { get; set; }
            [JsonPropertyName("lastPlayed")] public DateTime LastPlayed { get; set; }
        }

        /// <summary>An achievement earned (RetroAchievements), with its badge.</summary>
        public sealed class Unlock
        {
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("badge")] public string? Badge { get; set; }
            [JsonPropertyName("date")] public DateTime Date { get; set; }
        }

        /// <summary>A game's achievements of a player: how many earned, and the last ones.</summary>
        public sealed class GameAchievements
        {
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";
            [JsonPropertyName("cover")] public string? Cover { get; set; }
            [JsonPropertyName("earned")] public int Earned { get; set; }
            [JsonPropertyName("total")] public int Total { get; set; }
            [JsonPropertyName("unlocks")] public List<Unlock> Unlocks { get; set; } = new();
        }

        public sealed class Player
        {
            [JsonPropertyName("username")] public string Username { get; set; } = "";
            [JsonPropertyName("plays")] public List<Play> Plays { get; set; } = new();
            [JsonPropertyName("favorites")] public List<string> Favorites { get; set; } = new();
            /// <summary>By game ("psx:2161").</summary>
            [JsonPropertyName("achievements")] public Dictionary<string, GameAchievements> Achievements { get; set; } = new();
        }

        public sealed record Activity(int UserId, string Username, Play Play);
        public sealed record UnlockActivity(int UserId, string Username, string Key, GameAchievements Game, Unlock Unlock);

        /// <summary>The badges are RetroAchievements' pictures: nothing else is shown to the other players.</summary>
        public static bool IsBadge(string? url) => url != null && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "media.retroachievements.org";

        public const int KeptUnlocks = 20;

        /// <summary>"psx:2161": a game of RomM, by platform and id.</summary>
        [GeneratedRegex("^[a-z0-9]{1,20}:[0-9]{1,10}$")]
        private static partial Regex KeyPattern();

        public static bool IsValidKey(string? key) => key != null && KeyPattern().IsMatch(key);

        private string FileOf(int userId) => Path.Combine(directory, $"{userId}.json");

        public Player Get(int userId)
        {
            string file = FileOf(userId);
            lock (writing)
                return Enabled && File.Exists(file) ? JsonSerializer.Deserialize<Player>(File.ReadAllText(file)) ?? new Player() : new Player();
        }

        private void Write(int userId, Player player)
        {
            // Written aside then moved: never half written
            string file = FileOf(userId), part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(player));
            File.Move(part, file, overwrite: true);
        }

        /// <summary>A game was played for some minutes: added to its total, and it becomes the last played.</summary>
        public void AddPlay(int userId, string username, Play played)
        {
            lock (writing)
            {
                Player player = Get(userId);
                player.Username = username;
                Play? known = player.Plays.FirstOrDefault(p => p.Key == played.Key);
                if (known == null)
                    player.Plays.Add(known = played);
                else
                {
                    known.Name = played.Name;
                    known.PlatformName = played.PlatformName;
                    known.Cover = played.Cover;
                    known.Minutes += played.Minutes;
                }
                known.LastPlayed = DateTime.UtcNow;
                Write(userId, player);
            }
        }

        public void SetFavorites(int userId, string username, IEnumerable<string> favorites)
        {
            lock (writing)
            {
                Player player = Get(userId);
                player.Username = username;
                player.Favorites = favorites.Where(IsValidKey).Distinct().Take(MaxFavorites).ToList();
                Write(userId, player);
            }
        }

        /// <summary>A game's achievements of the player, as RetroAchievements has them now.</summary>
        public void SetAchievements(int userId, string username, string key, GameAchievements game)
        {
            lock (writing)
            {
                Player player = Get(userId);
                player.Username = username;
                if (game.Earned == 0)
                    player.Achievements.Remove(key);
                else
                    player.Achievements[key] = game;
                Write(userId, player);
            }
        }

        /// <summary>The achievements earned lately by all the players, newest first.</summary>
        public List<UnlockActivity> RecentUnlocks(int count)
        {
            if (!Enabled)
                return new();
            var all = new List<UnlockActivity>();
            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int userId))
                    continue;
                Player player = Get(userId);
                all.AddRange(player.Achievements.SelectMany(a => a.Value.Unlocks.Select(u => new UnlockActivity(userId, player.Username, a.Key, a.Value, u))));
            }
            return all.OrderByDescending(a => a.Unlock.Date).Take(count).ToList();
        }

        /// <summary>Who played a game, the last one first (the game's page shows the friends who played it).</summary>
        public List<Activity> PlayersOf(string key) => Recent(int.MaxValue).Where(a => a.Play.Key == key).ToList();

        /// <summary>The games the players played lately, newest first.</summary>
        public List<Activity> Recent(int count)
        {
            if (!Enabled)
                return new();
            var all = new List<Activity>();
            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int userId))
                    continue;
                Player player = Get(userId);
                all.AddRange(player.Plays.Select(p => new Activity(userId, player.Username, p)));
            }
            return all.OrderByDescending(a => a.Play.LastPlayed).Take(count).ToList();
        }
    }
}
