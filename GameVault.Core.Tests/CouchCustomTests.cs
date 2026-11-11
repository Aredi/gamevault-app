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
    }
}
