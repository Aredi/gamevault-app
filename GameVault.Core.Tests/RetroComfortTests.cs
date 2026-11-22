using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroComfortTests
    {
        private static Dictionary<string, string> Of(string core, bool resume, bool runAhead) => RetroComfort.Config(core, resume, runAhead).ToDictionary(o => o.Key, o => o.Value);

        [Fact]
        public void ALightConsole_ResumesAndRunsAhead_WhenTheyAreOn()
        {
            var snes = Of("snes9x", true, true);
            Assert.Equal("true", snes["savestate_auto_save"]);
            Assert.Equal("true", snes["savestate_auto_load"]);
            Assert.Equal("true", snes["run_ahead_enabled"]);
            Assert.Equal("1", snes["run_ahead_frames"]);
            Assert.Equal("true", snes["run_ahead_secondary_instance"]);
        }

        [Fact]
        public void TheSettingsOff_OrAHeavyConsole_WriteThemOff()
        {
            var off = Of("snes9x", false, false);
            Assert.Equal("false", off["savestate_auto_save"]);
            Assert.Equal("false", off["savestate_auto_load"]);
            Assert.Equal("false", off["run_ahead_enabled"]);
            // A PS1 resumes (small states) but is too heavy to run twice
            var psx = Of("swanstation", true, true);
            Assert.Equal("true", psx["savestate_auto_load"]);
            Assert.Equal("false", psx["run_ahead_enabled"]);
            // A PS2's or a GameCube's states are tens of MB, sent with the cloud saves: neither
            foreach (string core in new[] { "pcsx2", "dolphin" })
            {
                var heavy = Of(core, true, true);
                Assert.Equal("false", heavy["savestate_auto_save"]);
                Assert.Equal("false", heavy["run_ahead_enabled"]);
            }
        }
    }
}
