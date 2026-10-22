using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class PlaytimeBreakdownTests
    {
        [Fact]
        public void ThePlatforms_AreAddedUp_TheMostPlayedFirst_TheRestTogether()
        {
            var parts = PlaytimeBreakdown.Compute(new[] { ("PC", 600), ("SNES", 120), ("snes", 30), ("NES", 60), ("GBA", 20), ("Wii", 10), ("PS2", 0) }, kept: 4, others: "Others");
            Assert.Equal(new[] { ("PC", 600), ("SNES", 150), ("NES", 60), ("Others", 30) }, parts.Select(p => (p.Name, p.Minutes)));
            Assert.Equal(1, parts[0].Share);
            Assert.Equal(0.25, parts[1].Share);
        }

        [Fact]
        public void NothingPlayed_ShowsNothing() => Assert.Empty(PlaytimeBreakdown.Compute(new[] { ("PC", 0) }, 6, "Others"));
    }
}
