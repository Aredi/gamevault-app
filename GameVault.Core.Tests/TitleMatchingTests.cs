using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class TitleMatchingTests
    {
        [Theory]
        [InlineData("/files/Hades (v1.38) (W_P).zip", "Hades")]
        [InlineData("Baldur's Gate 3 (2023) [GOG] (W_S).7z", "Baldur's Gate 3")]
        [InlineData("Celeste (L_P).tar.gz", "Celeste")]
        [InlineData("Disco Elysium - The Final Cut.iso", "Disco Elysium - The Final Cut")]
        [InlineData("Hollow_Knight_v1.5.zip", "Hollow Knight")]
        [InlineData("Hollow Knight v1.5.4.zip", "Hollow Knight")]
        [InlineData("Baldur's Gate 3.zip", "Baldur's Gate 3")]
        [InlineData("Sea.of.Stars.Dreamers.Edition-GOG.zip", "Sea of Stars Dreamers Edition")]
        [InlineData("Ori (and the Will) of the Wisps.zip", "Ori (and the Will) of the Wisps")]
        public void SearchTerm_DropsTheExtension_AndTrailingTags(string file, string expected) =>
            Assert.Equal(expected, TitleMatching.SearchTerm(file));

        [Fact]
        public void YearAndPinnedId_AreReadFromTags()
        {
            Assert.Equal(2018, TitleMatching.Year("Celeste (2018) (W_P).zip"));
            Assert.Null(TitleMatching.Year("Celeste (W_P).zip"));
            Assert.Equal("1020", TitleMatching.PinnedId("Grand Theft Auto V (igdb-1020).zip", "igdb"));
            Assert.Null(TitleMatching.PinnedId("Grand Theft Auto V.zip", "igdb"));
        }

        [Theory]
        [InlineData("The Witcher 3: Wild Hunt", "witcher 3 wild hunt")]
        [InlineData("Legend of Zelda, The - Ocarina of Time", "legend of zelda ocarina of time")]
        [InlineData("Pokémon Écarlate", "pokemon ecarlate")]
        [InlineData("  ELDEN RING  ", "elden ring")]
        public void Normalize_LikeRomm(string name, string expected) => Assert.Equal(expected, TitleMatching.Normalize(name));

        [Fact]
        public void JaroWinkler_KnownValues()
        {
            Assert.Equal(1, TitleMatching.JaroWinkler("hades", "hades"));
            Assert.Equal(0.961, TitleMatching.JaroWinkler("martha", "marhta"), 3);
            Assert.Equal(0.813, TitleMatching.JaroWinkler("dixon", "dicksonx"), 3);
            Assert.Equal(0, TitleMatching.JaroWinkler("", "x"));
        }

        [Fact]
        public void Best_PicksTheRightGame_AmongProviderResults()
        {
            var results = new[]
            {
                new TitleCandidate("The Witcher 2: Assassins of Kings", new DateTime(2011, 5, 17)),
                new TitleCandidate("The Witcher 3: Wild Hunt", new DateTime(2015, 5, 19)),
                new TitleCandidate("The Witcher 3: Wild Hunt - Blood and Wine", new DateTime(2016, 5, 31)),
            };
            var match = TitleMatching.Best("/files/The Witcher 3 - Wild Hunt (2015) (W_S).zip", results);
            Assert.NotNull(match);
            Assert.Equal(1, match.Index);
            Assert.True(match.IsConfident);
        }

        [Fact]
        public void APrefixOnly_OrAWeakMatch_IsNotTakenAutomatically()
        {
            var plus = TitleMatching.Best("Metal Gear Solid Portable Ops Plus.iso", new[] { new TitleCandidate("Metal Gear Solid: Portable Ops", null) });
            Assert.NotNull(plus);
            Assert.False(plus.IsConfident);
            Assert.Null(TitleMatching.Best("Hades.zip", new[] { new TitleCandidate("Stardew Valley", null) }));
        }

        [Fact]
        public void TheYear_Decides_BetweenTwoGamesWithTheSameTitle()
        {
            var results = new[] { new TitleCandidate("Prey", new DateTime(2006, 7, 11)), new TitleCandidate("Prey", new DateTime(2017, 5, 5)) };
            Assert.Equal(1, TitleMatching.Best("Prey (2017) (W_P).zip", results)!.Index);
            Assert.Equal(0, TitleMatching.Best("Prey (2006) (W_P).zip", results)!.Index);
        }
    }
}
