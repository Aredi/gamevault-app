using GameVault.Core.Couch;
using Xunit;

namespace GameVault.Core.Tests
{
    /// <summary>The living-room mode's customization: its values kept, its packs of sounds, its music themes.</summary>
    public class CouchCustomTests
    {
        [Fact]
        public void EachCustomization_IsKept_AndAValueItDoesNotHave_IsTheUsualOne()
        {
            string file = Path.Combine(Path.GetTempPath(), "gv-couch-custom-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var settings = new CouchSettings();
                // The usual one of each is the first of its values
                foreach (var (name, values) in CouchSettings.Choices)
                    Assert.Equal(values[0], settings.Get(name));

                settings.Layout = "showcase";
                settings.Background = "synthwave";
                settings.CoverSize = "large";
                settings.SoundPack = "retro";
                settings.SoundVolume = 50;
                settings.Hints = false;
                settings.Save(file);
                CouchSettings read = CouchSettings.Load(file);
                Assert.Equal(("showcase", "synthwave", "large", "retro", 50, false), (read.Layout, read.Background, read.CoverSize, read.SoundPack, read.SoundVolume, read.Hints));

                File.WriteAllText(file, "{\"Layout\":\"spiral\",\"Font\":null,\"SoundVolume\":300,\"MusicVolume\":-4}");
                read = CouchSettings.Load(file);
                Assert.Equal(("signature", "sora", 100, 75), (read.Layout, read.Font, read.SoundVolume, read.MusicVolume));
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void EachPackOfSounds_SoundsItsOwnWay_QuietAndWithoutClicks()
        {
            foreach (string pack in CouchSettings.Choices[nameof(CouchSettings.SoundPack)])
                foreach (CouchSound sound in Enum.GetValues<CouchSound>())
                {
                    short[] samples = CouchSounds.Samples(sound, pack);
                    Assert.InRange(samples.Max(s => Math.Abs((int)s)), short.MaxValue / 20, short.MaxValue / 4);
                    Assert.InRange(Math.Abs((int)samples[0]), 0, 200);
                    Assert.InRange(Math.Abs((int)samples[^1]), 0, 200);
                }
            Assert.NotEqual(CouchSounds.Samples(CouchSound.Accept, "soft"), CouchSounds.Samples(CouchSound.Accept, "retro"));
            Assert.NotEqual(CouchSounds.Samples(CouchSound.Accept, "soft"), CouchSounds.Samples(CouchSound.Accept, "crystal"));
            // Half as loud at 50 %
            int full = CouchSounds.Samples(CouchSound.Move).Max(s => Math.Abs((int)s));
            int half = CouchSounds.Samples(CouchSound.Move, volume: 50).Max(s => Math.Abs((int)s));
            Assert.InRange(half, full / 2 - 2, full / 2 + 2);
        }

        [Fact]
        public void TheNightMusic_IsAnotherLoop_OfTheSameLength()
        {
            short[] calm = CouchMusic.Samples(), night = CouchMusic.Samples("night");
            Assert.Equal(calm.Length, night.Length);
            Assert.NotEqual(calm, night);
            Assert.InRange(night.Max(s => Math.Abs((int)s)), short.MaxValue / 8, short.MaxValue / 5);
        }

        [Fact]
        public void APlayersLevel_GrowsWithTheAchievementsTheGamesFinishedAndTheTime()
        {
            Assert.Equal(new CouchLevel(1, CouchLevel.Bronze, 1, 0), CouchLevel.Of(0, 0, 0));
            // 5 points for level 2, 10 more for level 3
            Assert.Equal(2, CouchLevel.Of(5, 0, 0).Level);
            Assert.Equal(2, CouchLevel.Of(14, 0, 0).Level);
            Assert.Equal(0.9, CouchLevel.Of(14, 0, 0).Progress, 3);
            Assert.Equal(3, CouchLevel.Of(15, 0, 0).Level);
            // A game at 100 % is 15 points, two hours one point
            Assert.Equal(CouchLevel.Points(15, 0, 0), CouchLevel.Points(0, 1, 0));
            Assert.Equal(1, CouchLevel.Points(0, 0, 120));
            // The tiers and their steps
            CouchLevel silver = CouchLevel.Of(300, 0, 0);
            Assert.Equal((11, CouchLevel.Silver, 1), (silver.Level, silver.Tier, silver.Step));
            Assert.Equal(CouchLevel.Gold, CouchLevel.Of(2000, 0, 0).Tier);
            Assert.Equal(CouchLevel.Platinum, CouchLevel.Of(8000, 0, 0).Tier);
            Assert.Equal(3, CouchLevel.Of(200, 0, 0).Step);
            Assert.InRange(CouchLevel.Of(-5, -1, -100).Level, 1, 1);
        }

        [Fact]
        public void BackToTheUsualSettings_KeepsTheApplications()
        {
            var settings = new CouchSettings { Layout = "panel", Background = "night", SoundVolume = 25 };
            settings.Apps.Add(new CouchApp("Kodi", "/usr/bin/kodi"));
            CouchSettings usual = settings.Reset();
            Assert.Equal(("signature", "game", 100), (usual.Layout, usual.Background, usual.SoundVolume));
            Assert.Single(usual.Apps);
        }
    }
}
