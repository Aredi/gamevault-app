namespace GameVault.Core.Library
{
    /// <summary>A game that could be proposed for tonight, with what the player did with it.</summary>
    public sealed record TonightCandidate(int Id, bool Installed, int MinutesPlayed, DateTime? LastPlayedUtc, string? State, int? AverageMinutes, IReadOnlyList<string> Genres);

    /// <param name="InstalledOnly">Only games ready to start.</param>
    /// <param name="NeverPlayed">Only games never started.</param>
    /// <param name="NotFinished">Without the games completed or given up for good.</param>
    /// <param name="Short">Only games finished in less than <see cref="TonightPicker.ShortMinutes"/> on average.</param>
    /// <param name="Genre">Only this genre (null: all).</param>
    public sealed record TonightFilter(bool InstalledOnly, bool NeverPlayed, bool NotFinished, bool Short, string? Genre);

    /// <summary>
    /// "What should I play tonight?": a random game among those matching the filters, the forgotten ones more likely
    /// (never started, or not played for months) than those played yesterday or already completed.
    /// </summary>
    public static class TonightPicker
    {
        public const int ShortMinutes = 10 * 60;

        public static bool IsFinished(string? state) =>
            string.Equals(state, "COMPLETED", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "ABORTED_PERMANENT", StringComparison.OrdinalIgnoreCase);

        public static bool IsNeverPlayed(TonightCandidate game) => game.MinutesPlayed <= 0 && game.LastPlayedUtc == null;

        public static List<TonightCandidate> Filter(IEnumerable<TonightCandidate> games, TonightFilter filter) => games.Where(g =>
            (!filter.InstalledOnly || g.Installed)
            && (!filter.NeverPlayed || IsNeverPlayed(g))
            && (!filter.NotFinished || !IsFinished(g.State))
            && (!filter.Short || g.AverageMinutes is > 0 and <= ShortMinutes)
            && (filter.Genre == null || g.Genres.Contains(filter.Genre, StringComparer.OrdinalIgnoreCase))).ToList();

        /// <summary>How likely a game is to be picked.</summary>
        public static double Weight(TonightCandidate game, DateTime nowUtc)
        {
            if (IsFinished(game.State))
                return 0.25;
            // Never started first; one set aside for months comes close (at most 3)
            if (IsNeverPlayed(game))
                return 3.5;
            if (game.LastPlayedUtc is not DateTime last)
                return 1.5;
            double days = (nowUtc - last).TotalDays;
            // Played last night: probably not what is looked for; set aside for months: worth a reminder
            return days < 2 ? 0.4 : 1 + Math.Min(days / 90, 2);
        }

        /// <summary>A random game of the pool, avoiding those just proposed while others remain.</summary>
        public static TonightCandidate? Pick(IReadOnlyList<TonightCandidate> pool, Random random, IReadOnlyCollection<int> avoid, DateTime nowUtc)
        {
            if (pool.Count == 0)
                return null;
            var choices = pool.Where(g => !avoid.Contains(g.Id)).ToList();
            if (choices.Count == 0)
                choices = pool.ToList();
            double total = choices.Sum(g => Weight(g, nowUtc));
            double roll = random.NextDouble() * total;
            foreach (TonightCandidate game in choices)
            {
                roll -= Weight(game, nowUtc);
                if (roll <= 0)
                    return game;
            }
            return choices[^1];
        }
    }
}
