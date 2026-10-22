namespace GameVault.Core.Library
{
    /// <summary>A player's time on the PC games and on each retro platform, the most played first.</summary>
    public static class PlaytimeBreakdown
    {
        public sealed record Part(string Name, int Minutes, double Share);

        /// <param name="parts">What was played, by where (several entries of one name add up).</param>
        /// <param name="kept">The ones shown; the rest are added up as <paramref name="others"/>.</param>
        public static List<Part> Compute(IEnumerable<(string Name, int Minutes)> parts, int kept, string others)
        {
            var totals = parts.Where(p => p.Minutes > 0)
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.First().Name, Minutes: g.Sum(p => p.Minutes)))
                .OrderByDescending(p => p.Minutes).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (totals.Count > kept)
                totals = totals.Take(kept - 1).Append((others, totals.Skip(kept - 1).Sum(p => p.Minutes))).ToList();
            int max = totals.Count == 0 ? 0 : totals.Max(p => p.Minutes);
            return totals.Select(p => new Part(p.Name, p.Minutes, max == 0 ? 0 : (double)p.Minutes / max)).ToList();
        }
    }
}
