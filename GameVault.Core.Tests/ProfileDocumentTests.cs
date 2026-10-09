using GameVault.Core.Library;

namespace GameVault.Core.Tests
{
    public class ProfileDocumentTests
    {
        [Fact]
        public void NothingOrSomethingUnreadable_IsTheDefaultProfile()
        {
            foreach (string? text in new[] { null, "", "not json", "[1]", "{\"modules\": 5}" })
            {
                var profile = ProfileDocument.Parse(text);
                Assert.Equal(new[] { ProfileModuleType.Stats, ProfileModuleType.Recent }, profile.Modules.Select(m => m.Type));
            }
        }

        [Fact]
        public void AProfile_SurvivesTheTripToTheService()
        {
            var profile = new ProfileDocument
            {
                Tagline = "  Speedrunner du dimanche  ",
                Accent = "#e2a33b",
                Modules =
                {
                    new ProfileModule { Type = ProfileModuleType.Favorite, Games = { 12 } },
                    new ProfileModule { Type = ProfileModuleType.Showcase, Title = "Mes indispensables", Games = { 4, 8, 15 } },
                    new ProfileModule { Type = ProfileModuleType.Text, Title = "À propos", Text = "Je joue surtout le soir." },
                },
            };
            var read = ProfileDocument.Parse(profile.ToJson());
            Assert.Equal("Speedrunner du dimanche", read.Tagline);
            Assert.Equal("#E2A33B", read.Accent);
            Assert.Equal(new[] { "favorite", "showcase", "text" }, read.Modules.Select(m => m.Type));
            Assert.Equal(new[] { 4, 8, 15 }, read.Modules[1].Games);
            Assert.Equal("Je joue surtout le soir.", read.Modules[2].Text);
        }

        [Fact]
        public void WhatIsReadFromTheService_IsKeptWithinItsLimits()
        {
            string json = "{\"tagline\":\"" + new string('x', 300) + "\",\"accent\":\"red; drop\",\"modules\":["
                + "{\"type\":\"hack\"},"
                + "{\"type\":\"favorite\",\"games\":[3,4,5]},"
                + "{\"type\":\"favorite\",\"games\":[6]},"
                + "{\"type\":\"showcase\",\"games\":[1,1,2,-4,0,3,4,5,6,7,8,9,10]},"
                + "{\"type\":\"stats\",\"text\":\"ignored\"},"
                + "{\"type\":\"text\",\"text\":\"" + new string('y', 5000) + "\"}"
                + "]}";
            var profile = ProfileDocument.Parse(json);
            Assert.Equal(ProfileDocument.MaxTagline, profile.Tagline!.Length);
            Assert.Null(profile.Accent);
            Assert.Equal(new[] { "favorite", "showcase", "stats", "text" }, profile.Modules.Select(m => m.Type));
            Assert.Equal(new[] { 3 }, profile.Modules[0].Games);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, profile.Modules[1].Games);
            Assert.Null(profile.Modules[2].Text);
            Assert.Equal(ProfileDocument.MaxText, profile.Modules[3].Text!.Length);
        }

        [Fact]
        public void Badges_ComeWithLevels_AndTheNextGoal()
        {
            var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
            var stats = new ProfileStats(TotalMinutes: 130 * 60, GamesPlayed: 12, Completed: 0, PlayedThisWeek: 2);
            var badges = ProfileBadges.Compute(stats, now.AddDays(-400), longestMinutes: 60 * 60, now).ToDictionary(b => b.Key);
            Assert.Equal(3, badges["hours"].Level);// 10, 50, 100
            Assert.Equal(250, badges["hours"].NextGoal);
            Assert.Equal(2, badges["played"].Level);
            Assert.Equal(3, badges["member"].Level);
            Assert.Equal(2, badges["marathon"].Level);
            Assert.Equal(1, badges["week"].Level);
            Assert.False(badges.ContainsKey("completed"));
            Assert.Empty(ProfileBadges.Compute(new ProfileStats(0, 0, 0, 0), null, 0, now));
        }
    }
}
