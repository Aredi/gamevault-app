using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class QuickMatchTests
    {
        [Fact]
        public void BetterMatches_ScoreHigher()
        {
            int exact = QuickMatch.Score("hades", "Hades");
            int prefix = QuickMatch.Score("hol", "Hollow Knight");
            int word = QuickMatch.Score("knight", "Hollow Knight");
            int inside = QuickMatch.Score("llow", "Hollow Knight");
            int letters = QuickMatch.Score("hk", "Hollow Knight");
            Assert.True(exact > prefix && prefix > word && word > inside && inside > letters && letters > 0,
                $"{exact} {prefix} {word} {inside} {letters}");
        }

        [Theory]
        [InlineData("rdr", "Red Dead Redemption 2")]
        [InlineData("bg3", "Baldur's Gate 3")]
        [InlineData("parametres", "Paramètres")]
        [InlineData("TELECH", "Téléchargements")]
        public void SpreadLetters_AndAccents_Match(string query, string name) => Assert.True(QuickMatch.Score(query, name) > 0);

        [Theory]
        [InlineData("xyz", "Hades")]
        [InlineData("sedah", "Hades")]
        [InlineData("", "Hades")]
        [InlineData("hades", null)]
        public void Others_DoNotMatch(string query, string? name) => Assert.Equal(0, QuickMatch.Score(query, name));
    }
}
