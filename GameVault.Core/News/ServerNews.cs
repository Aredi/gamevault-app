using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameVault.Core.News
{
    public enum NewsKind { Added, Updated }

    /// <summary>What the news need to know about a game of the server.</summary>
    public sealed record NewsGame(int Id, DateTime? CreatedAt, string? FilePath, string? Size, string? Version);

    /// <summary>A line of the news: a game added to the server, or a new build of a game.</summary>
    public sealed record NewsEntry(
        [property: JsonPropertyName("kind")] NewsKind Kind,
        [property: JsonPropertyName("game")] int GameId,
        [property: JsonPropertyName("date")] DateTime DateUtc,
        [property: JsonPropertyName("version")] string? Version);

    /// <summary>The files of the games seen last time, and the updates found so far (kept in the profile).</summary>
    public sealed class NewsSnapshot
    {
        [JsonPropertyName("files")]
        public Dictionary<int, string> Files { get; set; } = new();
        [JsonPropertyName("updates")]
        public List<NewsEntry> Updates { get; set; } = new();

        public static NewsSnapshot Load(string file)
        {
            try
            {
                if (File.Exists(file))
                    return JsonSerializer.Deserialize<NewsSnapshot>(File.ReadAllText(file)) ?? new NewsSnapshot();
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return new NewsSnapshot();
        }

        public void Save(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, file, overwrite: true);
        }
    }

    /// <summary>
    /// The server's news, made from its games: the ones added recently (their creation date) and the new builds of
    /// existing ones (found by comparing each game's file with the one seen the time before).
    /// </summary>
    public static class ServerNews
    {
        public static readonly TimeSpan Window = TimeSpan.FromDays(60);
        private const int MaxUpdates = 200;

        /// <summary>The file of a game as it matters for updates: path and size (a replaced file keeps its path).</summary>
        private static string Fingerprint(NewsGame game) => $"{game.FilePath}|{game.Size}";

        /// <summary>Compares the recently changed games with the snapshot: their new builds become updates.</summary>
        public static void Track(NewsSnapshot snapshot, IEnumerable<NewsGame> games, DateTime nowUtc)
        {
            foreach (NewsGame game in games)
            {
                if (string.IsNullOrEmpty(game.FilePath))
                    continue;
                string fingerprint = Fingerprint(game);
                if (snapshot.Files.TryGetValue(game.Id, out string? previous) && previous != fingerprint)
                    snapshot.Updates.Add(new NewsEntry(NewsKind.Updated, game.Id, nowUtc, game.Version));
                snapshot.Files[game.Id] = fingerprint;
            }
            snapshot.Updates = snapshot.Updates
                .Where(u => nowUtc - u.DateUtc <= Window)
                .OrderByDescending(u => u.DateUtc)
                .Take(MaxUpdates)
                .ToList();
        }

        /// <summary>The news, newest first: games added within the window and the updates found.</summary>
        public static List<NewsEntry> Feed(IEnumerable<NewsGame> recentlyAdded, NewsSnapshot snapshot, DateTime nowUtc)
        {
            var added = recentlyAdded
                .Where(g => g.CreatedAt != null && nowUtc - g.CreatedAt.Value.ToUniversalTime() <= Window)
                .Select(g => new NewsEntry(NewsKind.Added, g.Id, g.CreatedAt!.Value.ToUniversalTime(), g.Version))
                .ToList();
            var addedIds = added.ToDictionary(a => a.GameId, a => a.DateUtc);
            // A game added and "updated" the same day is just new
            var updates = snapshot.Updates.Where(u => !(addedIds.TryGetValue(u.GameId, out DateTime date) && (u.DateUtc - date).Duration() < TimeSpan.FromDays(1)));
            return added.Concat(updates).OrderByDescending(e => e.DateUtc).ToList();
        }

        /// <summary>News newer than when the user last opened them.</summary>
        public static int Unread(IEnumerable<NewsEntry> feed, DateTime? lastSeenUtc) =>
            feed.Count(e => lastSeenUtc == null || e.DateUtc > lastSeenUtc.Value);

        public enum Period { ThisWeek, LastWeek, ThisMonth, Earlier }

        /// <summary>Which group a date falls in (the week starts on Monday).</summary>
        public static Period PeriodOf(DateTime dateLocal, DateTime nowLocal)
        {
            DateTime weekStart = nowLocal.Date.AddDays(-(((int)nowLocal.DayOfWeek + 6) % 7));
            if (dateLocal >= weekStart)
                return Period.ThisWeek;
            if (dateLocal >= weekStart.AddDays(-7))
                return Period.LastWeek;
            if (nowLocal - dateLocal <= TimeSpan.FromDays(31))
                return Period.ThisMonth;
            return Period.Earlier;
        }
    }
}
