using GameVault.Core.Integrations;

namespace GameVault.Core.Tests
{
    public class DiscordActivityTests
    {
        private static readonly DateTime Start = new(2026, 10, 8, 20, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ShowsTheGame_ItsTotalPlayTime_AndTheCover()
        {
            var activity = DiscordActivity.Create("Hades", 2050, Start, Start.AddMinutes(25), "https://images.igdb.com/igdb/image/upload/t_cover_big/co39vc.jpg", m => $"{m / 60} h played");
            Assert.Equal("Hades", activity.Details);
            Assert.Equal("34 h played", activity.State);// 2050 + 25 minutes
            Assert.Equal(Start, activity.SessionStartUtc);
            Assert.StartsWith("https://images.igdb.com/", activity.LargeImage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("/media/covers/12.jpg")]
        [InlineData("http://images.igdb.com/cover.jpg")]
        [InlineData("https://192.168.1.25:7476/api/media/12")]
        [InlineData("https://localhost/api/media/12")]
        public void PrivateOrMissingCovers_UseTheLogo(string? source) =>
            Assert.Equal(DiscordActivity.LogoAsset, DiscordActivity.Create("Hades", 0, Start, Start, source, m => "").LargeImage);

        [Fact]
        public void Texts_FitDiscordLimits()
        {
            var activity = DiscordActivity.Create(new string('x', 300), 0, Start, Start, null, m => "");
            Assert.Equal(DiscordActivity.MaxText, activity.Details.Length);
            Assert.Null(activity.State);// never played: no time shown
            Assert.Equal(2, DiscordActivity.Fit("A").Length);
        }
    }
}
