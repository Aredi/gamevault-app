using GameVault.Core.Retro;
using System.Text.Json;

namespace GameVault.Core.Tests
{
    public class RommProgressTests
    {
        private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

        [Fact]
        public void TheStatusAndRating_AreTheirs_NoneWhenUnknown()
        {
            Assert.Equal(new RommProgress.Mine("finished", 8), RommProgress.MineOf(Json("""{"rom_user":{"status":"finished","rating":8}}""")));
            // RomM's account of a game never touched, and a status of a newer RomM
            Assert.Equal(new RommProgress.Mine("", 0), RommProgress.MineOf(Json("""{"rom_user":{"status":null,"rating":0}}""")));
            Assert.Equal(new RommProgress.Mine("", 10), RommProgress.MineOf(Json("""{"rom_user":{"status":"playing_forever","rating":12}}""")));
            Assert.Equal(new RommProgress.Mine("", 0), RommProgress.MineOf(Json("{}")));
        }

        [Fact]
        public void TheAchievements_AreRommsInTheirOrder_TheEarnedOnesMarked()
        {
            // As RomM 5.4 gives them: an earned one by its badge's name (the progression of another game is not this one's)
            var rom = Json("""
                {"ra_id":5626,"merged_ra_metadata":{"achievements":[
                  {"ra_id":109112,"title":"Mission 2","description":"Clear it","points":5,"badge_id":"379572","badge_url":"https://media.retroachievements.org/Badge/2.png","badge_url_lock":"https://media.retroachievements.org/Badge/2_lock.png","display_order":1},
                  {"ra_id":109111,"title":"Mission 1","description":"Clear it with an S Rank.","points":3,"badge_id":"379571","badge_url":"https://media.retroachievements.org/Badge/1.png","badge_url_lock":null,"display_order":0}]}}
                """);
            var me = Json("""
                {"ra_progression":{"total":2,"results":[
                  {"rom_ra_id":5626,"earned_achievements":[{"id":"379571","date":"2026-10-01 18:22:10"}]},
                  {"rom_ra_id":42,"earned_achievements":[{"id":"379572","date":"2026-10-01"}]}]}}
                """);
            var achievements = RommProgress.AchievementsOf(rom, me);
            Assert.Equal(new[] { "Mission 1", "Mission 2" }, achievements.Select(a => a.Title));
            Assert.True(achievements[0].Earned);
            Assert.Equal(new DateTime(2026, 10, 1, 18, 22, 10, DateTimeKind.Utc), achievements[0].EarnedAt);
            Assert.Equal(DateTimeKind.Utc, achievements[0].EarnedAt!.Value.Kind);
            Assert.False(achievements[1].Earned);
            Assert.Equal(3, achievements[0].Points);
            Assert.Equal("https://media.retroachievements.org/Badge/2_lock.png", achievements[1].LockedBadge);
            // Without RetroAchievements in RomM, or without a progression
            Assert.Empty(RommProgress.AchievementsOf(Json("""{"ra_id":null,"merged_ra_metadata":null}"""), me));
            Assert.All(RommProgress.AchievementsOf(rom, Json("""{"ra_progression":null}""")), a => Assert.False(a.Earned));
        }
    }
}
