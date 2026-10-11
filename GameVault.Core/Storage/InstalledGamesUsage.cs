namespace GameVault.Core.Storage
{
    /// <summary>An installed game, the room it takes and when it was played last.</summary>
    public sealed record InstalledGameUsage(int Id, string Title, string Directory, long Size, int MinutesPlayed, DateTime? LastPlayedUtc, DateTime InstalledUtc)
    {
        /// <summary>Days since the last session, or since the installation for a game never started.</summary>
        public int IdleDays(DateTime nowUtc) => Math.Max(0, (int)(nowUtc - (LastPlayedUtc ?? InstalledUtc)).TotalDays);
    }

    public enum InstalledGamesSort { Size, LastPlayed, Title }

    public static class InstalledGamesUsage
    {
        /// <summary>Not played for three months: offered for uninstallation.</summary>
        public const int IdleAfterDays = 90;

        public static bool IsIdle(InstalledGameUsage game, DateTime nowUtc) => game.IdleDays(nowUtc) >= IdleAfterDays;

        public static List<InstalledGameUsage> Sort(IEnumerable<InstalledGameUsage> games, InstalledGamesSort sort, DateTime nowUtc) => sort switch
        {
            // The longest unplayed first: the ones to remove
            InstalledGamesSort.LastPlayed => games.OrderByDescending(g => g.IdleDays(nowUtc)).ThenByDescending(g => g.Size).ToList(),
            InstalledGamesSort.Title => games.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => games.OrderByDescending(g => g.Size).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };

        /// <summary>Count and size of all the games, and of those not played for <see cref="IdleAfterDays"/> days.</summary>
        public static (int Count, long Size, int IdleCount, long IdleSize) Summarize(IReadOnlyCollection<InstalledGameUsage> games, DateTime nowUtc)
        {
            var idle = games.Where(g => IsIdle(g, nowUtc)).ToList();
            return (games.Count, games.Sum(g => g.Size), idle.Count, idle.Sum(g => g.Size));
        }
    }
}
