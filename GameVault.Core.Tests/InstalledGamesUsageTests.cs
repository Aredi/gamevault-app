using GameVault.Core.Storage;

namespace GameVault.Core.Tests
{
    public class InstalledGamesUsageTests
    {
        private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        private const long GB = 1024L * 1024 * 1024;

        private static readonly InstalledGameUsage Hades = new(1, "Hades", "/g/1", 15 * GB, 2050, Now.AddDays(-1), Now.AddDays(-300));
        private static readonly InstalledGameUsage Witcher = new(2, "The Witcher 3", "/g/2", 60 * GB, 900, Now.AddDays(-200), Now.AddDays(-400));
        private static readonly InstalledGameUsage NeverStarted = new(3, "Celeste", "/g/3", 2 * GB, 0, null, Now.AddDays(-120));
        private static readonly InstalledGameUsage JustInstalled = new(4, "Balatro", "/g/4", 1 * GB, 0, null, Now.AddDays(-2));

        [Fact]
        public void GamesNotPlayedForThreeMonths_AreIdle_CountingFromTheInstallationWhenNeverStarted()
        {
            Assert.False(InstalledGamesUsage.IsIdle(Hades, Now));
            Assert.True(InstalledGamesUsage.IsIdle(Witcher, Now));
            Assert.True(InstalledGamesUsage.IsIdle(NeverStarted, Now));
            Assert.False(InstalledGamesUsage.IsIdle(JustInstalled, Now));
            Assert.Equal(120, NeverStarted.IdleDays(Now));
        }

        [Fact]
        public void TheSummary_SaysWhatCanBeFreed()
        {
            var summary = InstalledGamesUsage.Summarize(new[] { Hades, Witcher, NeverStarted, JustInstalled }, Now);
            Assert.Equal(4, summary.Count);
            Assert.Equal(78 * GB, summary.Size);
            Assert.Equal(2, summary.IdleCount);
            Assert.Equal(62 * GB, summary.IdleSize);
        }

        [Fact]
        public void Sorting_BySize_ByIdleTime_ByTitle()
        {
            var all = new[] { Hades, Witcher, NeverStarted, JustInstalled };
            Assert.Equal(new[] { 2, 1, 3, 4 }, InstalledGamesUsage.Sort(all, InstalledGamesSort.Size, Now).Select(g => g.Id));
            Assert.Equal(new[] { 2, 3, 4, 1 }, InstalledGamesUsage.Sort(all, InstalledGamesSort.LastPlayed, Now).Select(g => g.Id));
            Assert.Equal(new[] { 4, 3, 1, 2 }, InstalledGamesUsage.Sort(all, InstalledGamesSort.Title, Now).Select(g => g.Id));
        }
    }
}
