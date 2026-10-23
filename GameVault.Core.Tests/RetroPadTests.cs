using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroPadTests
    {
        [Fact]
        public void TheDefaults_AreRetroArchsSdl2Profiles()
        {
            // RetroArch's SDL2 profiles (Xbox One, PS4...): the RetroPad's B is the bottom button, L2 the left trigger
            Assert.Equal(("0", "A"), (RetroPad.Defaults["b"].Name, RetroPad.Defaults["b"].Label));
            Assert.Equal("+4", RetroPad.Defaults["l2"].Name);
            Assert.Equal("-1", RetroPad.Defaults["l_y_minus"].Name);
            Assert.Equal("Left Stick ↑", RetroPad.Axis(1, false).Label);
            Assert.Equal("Button 17", RetroPad.Button(17).Label);
            Assert.Equal("Axis 2+", RetroPad.Axis(2, true, standard: false).Label);
            Assert.Equal(("h0left", "Hat 0 ←"), (RetroPad.Hat(0, "left").Name, RetroPad.Hat(0, "left").Label));
        }

        [Fact]
        public void AButton_GoesToOneConsolesButton_TheOtherTakesThePreviousOne()
        {
            var chosen = RetroPad.Assign("snes", null, null, "a", RetroPad.Button(3));
            Assert.Equal("3", chosen["a"].Name);
            Assert.Equal("1", chosen["x"].Name);
            // A trigger for the GameCube's L... the A button the other way
            var gc = RetroPad.Assign("ngc", null, null, "a", RetroPad.Axis(4, true));
            Assert.Equal("1", gc["l2"].Name);
        }

        [Fact]
        public void TheConfig_IsSdl2AndEveryButton_OnlyForTheConsolesSet()
        {
            Assert.Empty(RetroPad.Config("nes", null, null));
            var chosen = RetroPad.Assign("nes", null, null, "a", RetroPad.Button(2));
            var config = RetroPad.Config("nes", null, chosen).ToDictionary(c => c.Key, c => c.Value);
            Assert.Equal("sdl2", config["input_joypad_driver"]);
            // The profile is not read: a button left empty would be played by it
            Assert.Equal("false", config["input_autodetect_enable"]);
            Assert.Equal(("2", "nul"), (config["input_player1_a_btn"], config["input_player1_a_axis"]));
            // The RetroPad's Y (not the NES's) had the button: none, so it plays one button only
            Assert.Equal(("nul", "nul"), (config["input_player1_y_btn"], config["input_player1_y_axis"]));
            // The others as in the profiles, the sticks too; Select and Start always (Select+Start quits)
            Assert.Equal(("nul", "+4"), (config["input_player1_l2_btn"], config["input_player1_l2_axis"]));
            Assert.Equal("6", config["input_player1_start_btn"]);
        }

        [Fact]
        public void TheChoices_AreKeptAsText()
        {
            var choices = new Dictionary<string, Dictionary<string, RetroPad.Input>> { ["snes"] = new() { ["a"] = RetroPad.Axis(5, true) }, ["nes"] = new() };
            string text = RetroPad.Format(choices);
            // The console without a choice is not kept
            Assert.Equal(new[] { "snes" }, RetroPad.Parse(text).Keys);
            Assert.Equal(RetroPad.Axis(5, true), RetroPad.Parse(text)["snes"]["a"]);
            Assert.True(RetroPad.Parse(text)["snes"]["a"].IsAxis);
            Assert.Empty(RetroPad.Parse("{"));
        }
    }
}
