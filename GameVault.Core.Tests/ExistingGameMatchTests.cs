using GameVault.Core.Publishing;

namespace GameVault.Core.Tests
{
    public class ExistingGameMatchTests
    {
        private static readonly ServerGameTitle Duckov = new("Escape from Duckov", null, null);

        [Fact]
        public void TheExactTitle_AddsAVersion_ADifferentCaseCreatesASecondGame()
        {
            Assert.True(ExistingGameMatch.MergesWith("Escape from Duckov", null, Duckov));
            Assert.True(ExistingGameMatch.MergesWith("  Escape  from Duckov ", null, Duckov));
            Assert.False(ExistingGameMatch.MergesWith("Escape from duckov", null, Duckov));
            Assert.True(ExistingGameMatch.SpelledDifferently("Escape from duckov", Duckov));
            Assert.True(ExistingGameMatch.SpelledDifferently("Escape From Duckov", Duckov));
            Assert.False(ExistingGameMatch.SpelledDifferently("Escape from Duckov", Duckov));
        }

        [Fact]
        public void YearTags_MustAgree()
        {
            var prey2017 = new ServerGameTitle("Prey", "v1.0", 2017);
            Assert.True(ExistingGameMatch.MergesWith("Prey", 2017, prey2017));
            Assert.False(ExistingGameMatch.MergesWith("Prey", 2006, prey2017));
            Assert.True(ExistingGameMatch.MergesWith("Prey", null, prey2017));
        }

        [Fact]
        public void TheServerTitle_IsTheBuiltFileNameWithoutItsTags()
        {
            Assert.Equal("Hollow Knight [GOTY]", ExistingGameMatch.ServerTitle("Hollow Knight (GOTY)"));
            Assert.Equal("Nier Automata", ExistingGameMatch.ServerTitle("Nier: Automata"));
            Assert.Equal("v2.0", ExistingGameMatch.VersionTag("2.0"));
            Assert.Equal("v2.0", ExistingGameMatch.VersionTag("V2.0"));
            Assert.Null(ExistingGameMatch.VersionTag(" "));
        }

        [Fact]
        public void CloseTitles_AreOffered()
        {
            Assert.True(ExistingGameMatch.IsCandidate("escape from duckov", Duckov));
            Assert.True(ExistingGameMatch.IsCandidate("Escape from", Duckov));
            Assert.True(ExistingGameMatch.IsCandidate("Escape from Duckof", Duckov));
            Assert.False(ExistingGameMatch.IsCandidate("Hades", Duckov));
            Assert.False(ExistingGameMatch.IsCandidate("E", Duckov));
        }
    }
}
