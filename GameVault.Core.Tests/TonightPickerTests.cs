using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class TonightPickerTests
    {
        private static readonly DateTime Now = new(2026, 10, 11, 20, 0, 0, DateTimeKind.Utc);
        private static readonly string[] Roguelike = { "Roguelike", "Action" };

        private static readonly TonightCandidate Hades = new(1, true, 2000, Now.AddDays(-1), "PLAYING", 1300, Roguelike);
        private static readonly TonightCandidate Celeste = new(2, true, 0, null, null, 500, new[] { "Platformer" });
        private static readonly TonightCandidate Witcher = new(3, false, 900, Now.AddDays(-200), "PLAYING", 3000, new[] { "RPG" });
        private static readonly TonightCandidate Portal = new(4, true, 600, Now.AddDays(-400), "COMPLETED", 480, new[] { "Puzzle" });
        private static readonly TonightCandidate[] All = { Hades, Celeste, Witcher, Portal };

        [Fact]
        public void Filters_Combine()
        {
            Assert.Equal(new[] { 1, 2, 4 }, TonightPicker.Filter(All, new TonightFilter(true, false, false, false, null)).Select(g => g.Id));
            Assert.Equal(new[] { 2 }, TonightPicker.Filter(All, new TonightFilter(false, true, false, false, null)).Select(g => g.Id));
            Assert.Equal(new[] { 1, 2, 3 }, TonightPicker.Filter(All, new TonightFilter(false, false, true, false, null)).Select(g => g.Id));
            Assert.Equal(new[] { 2, 4 }, TonightPicker.Filter(All, new TonightFilter(false, false, false, true, null)).Select(g => g.Id));
            Assert.Equal(new[] { 1 }, TonightPicker.Filter(All, new TonightFilter(false, false, false, false, "roguelike")).Select(g => g.Id));
            Assert.Equal(new[] { 2 }, TonightPicker.Filter(All, new TonightFilter(true, false, true, true, null)).Select(g => g.Id));
        }

        [Fact]
        public void ForgottenGames_AreMoreLikely_ThanLastNightsOrCompletedOnes()
        {
            Assert.True(TonightPicker.Weight(Celeste, Now) > TonightPicker.Weight(Witcher, Now));
            Assert.True(TonightPicker.Weight(Witcher, Now) > TonightPicker.Weight(Hades, Now));
            Assert.True(TonightPicker.Weight(Hades, Now) > TonightPicker.Weight(Portal, Now));

            var counts = new Dictionary<int, int>();
            var random = new Random(42);
            for (int i = 0; i < 4000; i++)
            {
                int id = TonightPicker.Pick(All, random, Array.Empty<int>(), Now)!.Id;
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            Assert.True(counts[2] > counts[3] && counts[3] > counts[1] && counts[1] > counts[4], string.Join(", ", counts));
        }

        [Fact]
        public void AnotherIdea_AvoidsTheGamesJustProposed()
        {
            var random = new Random(1);
            for (int i = 0; i < 50; i++)
                Assert.Equal(3, TonightPicker.Pick(All, random, new[] { 1, 2, 4 }, Now)!.Id);
            // Everything was proposed: starts again rather than nothing
            Assert.NotNull(TonightPicker.Pick(All, random, new[] { 1, 2, 3, 4 }, Now));
            Assert.Null(TonightPicker.Pick(Array.Empty<TonightCandidate>(), random, Array.Empty<int>(), Now));
        }
    }
}
