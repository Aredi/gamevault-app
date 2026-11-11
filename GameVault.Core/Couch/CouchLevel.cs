namespace GameVault.Core.Couch
{
    /// <summary>A player's level, as a console's profile: from the achievements earned, the games finished to 100 % and the time played.</summary>
    public sealed record CouchLevel(int Level, string Tier, int Step, double Progress)
    {
        public const string Bronze = "bronze", Silver = "silver", Gold = "gold", Platinum = "platinum";

        /// <summary>The points: an achievement is one, a game at 100 % fifteen, two hours of play one.</summary>
        public static int Points(int achievements, int mastered, int minutes) =>
            Math.Max(0, achievements) + 15 * Math.Max(0, mastered) + Math.Max(0, minutes) / 120;

        /// <summary>
        /// Each level asks a little more than the one before (5 points, then 10, 15...). Bronze up to level 9, silver to 24,
        /// gold to 49, platinum after; each in three steps (I, II, III).
        /// </summary>
        public static CouchLevel Of(int achievements, int mastered, int minutes)
        {
            int points = Points(achievements, mastered, minutes);
            int level = 1, needed = 5, spent = 0;
            while (points - spent >= needed)
            {
                spent += needed;
                level++;
                needed = 5 * level;
            }
            (string tier, int from, int to) = level switch
            {
                < 10 => (Bronze, 1, 9),
                < 25 => (Silver, 10, 24),
                < 50 => (Gold, 25, 49),
                _ => (Platinum, 50, 100),
            };
            int step = Math.Min(3, 1 + (level - from) * 3 / (to - from + 1));
            return new CouchLevel(level, tier, step, (double)(points - spent) / needed);
        }
    }
}
