namespace GameVault.Core.Library
{
    /// <summary>The figures at the top of a profile.</summary>
    public record ProfileStats(int TotalMinutes, int GamesPlayed, int Completed, int PlayedThisWeek)
    {
        /// <param name="progresses">Each game of the user: minutes played, last session, state ("COMPLETED", ...).</param>
        public static ProfileStats Compute(IEnumerable<(int Minutes, DateTime? LastPlayedAt, string? State)> progresses, DateTime nowUtc)
        {
            int total = 0, played = 0, completed = 0, week = 0;
            foreach (var (minutes, last, state) in progresses)
            {
                total += Math.Max(0, minutes);
                if (minutes > 0 || last != null)
                    played++;
                if (string.Equals(state, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                    completed++;
                if (last != null && nowUtc - last.Value.ToUniversalTime() <= TimeSpan.FromDays(7))
                    week++;
            }
            return new ProfileStats(total, played, completed, week);
        }
    }
}
