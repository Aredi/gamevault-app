using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class ContinuePlayingTests
    {
        private static readonly DateTime Now = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void RetroGames_GoBetweenThePcGames_ByTheirLastSession()
        {
            var pc = new (string, DateTime?)[] { ("Hades", Now.AddHours(-1)), ("Celeste", Now.AddDays(-3)), ("Never played", null) };
            var retro = new (string, DateTime)[] { ("Zelda", Now.AddDays(-1)), ("Mario", Now.AddMinutes(-5)), ("Sonic", Now.AddDays(-30)) };
            Assert.Equal(new[] { "Mario", "Hades", "Zelda", "Celeste", "Sonic", "Never played" }, ContinuePlaying.Merge(pc, retro));
        }

        [Fact]
        public void ThePcOrder_IsKept_EvenWhenItsTimesDisagree()
        {
            // Launched last here (the local order), the server's last session older: the order of this PC wins
            var pc = new (string, DateTime?)[] { ("Just launched", Now.AddDays(-2)), ("Older", Now.AddHours(-1)) };
            Assert.Equal(new[] { "Just launched", "Older" }, ContinuePlaying.Merge(pc, Array.Empty<(string, DateTime)>()));
            Assert.Equal(new[] { "Zelda" }, ContinuePlaying.Merge(Array.Empty<(string, DateTime?)>(), new[] { ("Zelda", Now) }));
        }
    }
}
