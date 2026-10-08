using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class ProfileStatsTests
    {
        [Fact]
        public void Figures_CountPlayTime_PlayedCompletedAndThisWeek()
        {
            var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            var stats = ProfileStats.Compute(new (int, DateTime?, string?)[]
            {
                (600, now.AddDays(-1), "PLAYING"),
                (1200, now.AddDays(-30), "COMPLETED"),
                (0, null, "UNPLAYED"),
                (0, now.AddDays(-6.9), "PLAYING"),// started, not counted yet
                (-5, null, null),
            }, now);
            Assert.Equal(new ProfileStats(1800, 3, 1, 2), stats);
        }
    }
}
