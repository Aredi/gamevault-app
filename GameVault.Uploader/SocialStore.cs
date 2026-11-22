using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// What the players say to each other about their games: a reaction on a line of the activity, a game recommended
    /// to a friend, a review on a game's page. One JSON file in the profiles' volume (no new volume to mount); the
    /// reactions and recommendations go after <see cref="Kept"/>, the reviews stay.
    /// </summary>
    public sealed partial class SocialStore
    {
        /// <summary>The reactions offered: a short list, the same for everybody.</summary>
        public static readonly IReadOnlyList<string> Emojis = new[] { "GG", "👏", "🔥", "😂", "😮", "❤️" };
        public static readonly TimeSpan Kept = TimeSpan.FromDays(120);
        public const int MaxNote = 300, MaxReview = 800, RecommendationsPerDay = 30, ReactionsPerDay = 200;

        public sealed record Reaction(
            [property: JsonPropertyName("item")] string Item,
            [property: JsonPropertyName("owner")] int Owner,
            [property: JsonPropertyName("userId")] int UserId,
            [property: JsonPropertyName("username")] string Username,
            [property: JsonPropertyName("emoji")] string Emoji,
            [property: JsonPropertyName("at")] DateTimeOffset At);

        public sealed record Recommendation(
            [property: JsonPropertyName("id")] string Id,
            [property: JsonPropertyName("from")] int From,
            [property: JsonPropertyName("fromName")] string FromName,
            [property: JsonPropertyName("to")] int To,
            [property: JsonPropertyName("game")] ChatStore.SharedGame Game,
            [property: JsonPropertyName("note")] string? Note,
            [property: JsonPropertyName("at")] DateTimeOffset At);

        public sealed record Review(
            [property: JsonPropertyName("game")] string Game,
            [property: JsonPropertyName("userId")] int UserId,
            [property: JsonPropertyName("username")] string Username,
            [property: JsonPropertyName("stars")] int Stars,
            [property: JsonPropertyName("text")] string? Text,
            [property: JsonPropertyName("at")] DateTimeOffset At);

        private sealed class Data
        {
            [JsonPropertyName("reactions")] public List<Reaction> Reactions { get; set; } = new();
            [JsonPropertyName("recommendations")] public List<Recommendation> Recommendations { get; set; } = new();
            [JsonPropertyName("reviews")] public List<Review> Reviews { get; set; } = new();
        }

        /// <summary>A line of the activity: "pc:2:793:2026-11-20" (kind, its player, its game, its day), as the clients make it.</summary>
        [GeneratedRegex("^[a-z]{2,8}:[0-9]{1,9}:[a-z0-9:-]{1,60}:[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
        private static partial Regex ItemPattern();
        /// <summary>A game: "pc:793" or "retro:psx:2161".</summary>
        [GeneratedRegex("^(pc:[0-9]{1,10}|retro:[a-z0-9-]{1,40}:[0-9]{1,10})$")]
        private static partial Regex GamePattern();

        public static bool IsItem(string? item) => item != null && ItemPattern().IsMatch(item);
        public static bool IsGame(string? game) => game != null && GamePattern().IsMatch(game);

        private readonly string file;
        private readonly ILogger<SocialStore> logger;
        private readonly object gate = new();
        private Data? data;

        public SocialStore(UploaderOptions options, ILogger<SocialStore> logger)
        {
            string directory = Path.Combine(options.ProfilesDirectory, "social");
            file = Path.Combine(directory, "social.json");
            this.logger = logger;
            Enabled = ProfileStore.IsWritable(directory, out string error);
            if (!Enabled)
                logger.LogWarning("Reactions, recommendations and reviews are off: {Directory} can not be written ({Error}).", directory, error);
        }

        public bool Enabled { get; }
        /// <summary>The tests set the time.</summary>
        public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

        private Data Loaded()
        {
            if (data != null)
                return data;
            try { data = File.Exists(file) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(file)) ?? new() : new(); }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Not thrown away: kept aside for whoever looks, the store starts again
                logger.LogError(ex, "{File} could not be read; kept as {File}.broken", file, file);
                try { File.Copy(file, file + ".broken", overwrite: true); } catch (IOException) { }
                data = new();
            }
            return data;
        }

        private void Save(Data written)
        {
            DateTimeOffset old = Clock() - Kept;
            written.Reactions.RemoveAll(r => r.At < old);
            written.Recommendations.RemoveAll(r => r.At < old);
            string part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(written));
            File.Move(part, file, overwrite: true);
        }

        #region Reactions
        /// <summary>The reactions on these lines of the activity.</summary>
        public Dictionary<string, List<Reaction>> ReactionsOn(IEnumerable<string> items)
        {
            var wanted = items.ToHashSet();
            lock (gate)
                return Loaded().Reactions.Where(r => wanted.Contains(r.Item)).GroupBy(r => r.Item).ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>Puts the player's reaction (one per line, a new one replaces it), or takes it away (null).</summary>
        /// <returns>The reaction put, to tell its owner; null when taken away.</returns>
        /// <returns>
        /// The reaction put (null when taken away); whether it is another than the player's one there (its owner is told
        /// then only); refused past <see cref="ReactionsPerDay"/> a day.
        /// </returns>
        public (Reaction? Put, bool Changed, bool Refused) React(string item, int owner, int userId, string username, string? emoji)
        {
            lock (gate)
            {
                Data all = Loaded();
                DateTimeOffset now = Clock();
                Reaction? before = all.Reactions.FirstOrDefault(r => r.Item == item && r.UserId == userId);
                if (emoji != null && emoji != before?.Emoji && all.Reactions.Count(r => r.UserId == userId && now - r.At < TimeSpan.FromDays(1)) >= ReactionsPerDay)
                    return (null, false, true);
                all.Reactions.RemoveAll(r => r.Item == item && r.UserId == userId);
                Reaction? put = emoji == null ? null : new Reaction(item, owner, userId, username, emoji, now);
                if (put != null)
                    all.Reactions.Add(put);
                Save(all);
                return (put, put != null && put.Emoji != before?.Emoji, false);
            }
        }
        #endregion

        #region Recommendations
        public enum Refusal { None, TooMany }

        public (Recommendation? Made, Refusal Refused) Recommend(int from, string fromName, int to, ChatStore.SharedGame game, string? note)
        {
            lock (gate)
            {
                Data all = Loaded();
                DateTimeOffset now = Clock();
                if (all.Recommendations.Count(r => r.From == from && now - r.At < TimeSpan.FromDays(1)) >= RecommendationsPerDay)
                    return (null, Refusal.TooMany);
                // The same game to the same friend again: the newer one only
                all.Recommendations.RemoveAll(r => r.From == from && r.To == to && r.Game.Key == game.Key);
                var made = new Recommendation(Guid.NewGuid().ToString("N"), from, fromName, to, game, note, now);
                all.Recommendations.Add(made);
                Save(all);
                return (made, Refusal.None);
            }
        }

        /// <summary>The games recommended to this player, the newest first.</summary>
        public List<Recommendation> For(int userId)
        {
            lock (gate)
                return Loaded().Recommendations.Where(r => r.To == userId).OrderByDescending(r => r.At).ToList();
        }

        /// <summary>The player puts it away: only theirs.</summary>
        public bool Dismiss(string id, int userId)
        {
            lock (gate)
            {
                Data all = Loaded();
                if (all.Recommendations.RemoveAll(r => r.Id == id && r.To == userId) == 0)
                    return false;
                Save(all);
                return true;
            }
        }
        #endregion

        #region Reviews
        public sealed record Rating(
            [property: JsonPropertyName("stars")] double Stars,
            [property: JsonPropertyName("count")] int Count);

        /// <summary>Every reviewed game's average stars, for sorting a library by them.</summary>
        public Dictionary<string, Rating> Ratings()
        {
            lock (gate)
                return Loaded().Reviews.GroupBy(r => r.Game)
                    .ToDictionary(g => g.Key, g => new Rating(Math.Round(g.Average(r => r.Stars), 2), g.Count()));
        }

        public List<Review> ReviewsOf(string game)
        {
            lock (gate)
                return Loaded().Reviews.Where(r => r.Game == game).OrderByDescending(r => r.At).ToList();
        }

        /// <summary>The player's review of the game, written again whole.</summary>
        public Review SetReview(string game, int userId, string username, int stars, string? text)
        {
            lock (gate)
            {
                Data all = Loaded();
                all.Reviews.RemoveAll(r => r.Game == game && r.UserId == userId);
                var review = new Review(game, userId, username, stars, text, Clock());
                all.Reviews.Add(review);
                Save(all);
                return review;
            }
        }

        public bool RemoveReview(string game, int userId)
        {
            lock (gate)
            {
                Data all = Loaded();
                if (all.Reviews.RemoveAll(r => r.Game == game && r.UserId == userId) == 0)
                    return false;
                Save(all);
                return true;
            }
        }
        #endregion
    }
}
