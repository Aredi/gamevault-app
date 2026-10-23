using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroKeyboardTests
    {
        [Fact]
        public void EachConsole_HasItsButtons_TheWiisByItsController()
        {
            Assert.Equal(new[] { "up", "down", "left", "right", "a", "b", "select", "start" }, RetroKeyboard.Of("nes").Select(b => b.Bind));
            // SwanStation: the RetroPad's B is the Cross; FinalBurn Neo: its B is the Neo Geo's A
            Assert.Equal("Cross", RetroKeyboard.Of("psx").Single(b => b.Bind == "b").Name);
            Assert.Equal("A", RetroKeyboard.Of("neogeoaes").Single(b => b.Bind == "b").Name);
            Assert.Equal("C", RetroKeyboard.Of("wii").Single(b => b.Bind == "x").Name);
            Assert.Equal("1", RetroKeyboard.Of("wii", 1).Single(b => b.Bind == "x").Name);
            Assert.Equal("ZL", RetroKeyboard.Of("wii", 1025).Single(b => b.Bind == "l").Name);
            Assert.Contains(RetroKeyboard.Of("ngc"), b => b.Bind == "l_y_minus" && b.Name == "Control Stick ↑");
            // Not RetroArch's: their own windows
            Assert.Empty(RetroKeyboard.Of("ps3"));
            Assert.Empty(RetroKeyboard.Of("switch"));
        }

        [Fact]
        public void AKey_GoesToOneButton_TheOtherTakesThePreviousOne()
        {
            var w = new RetroKeyboard.Key("w", "Z");
            // The SNES's A on W (RetroArch's R): R gets X, A's previous key
            var chosen = RetroKeyboard.Assign("snes", null, null, "a", w)!;
            Assert.Equal(w, chosen["a"]);
            Assert.Equal("x", chosen["r"].Name);
            // A stick direction has no key by default: the button it takes one from has none then
            var gc = RetroKeyboard.Assign("ngc", null, null, "l_y_minus", new("up", "↑"))!;
            Assert.Equal(RetroKeyboard.None, gc["up"]);
            // RetroArch's menu, quitting and save states stay
            Assert.Null(RetroKeyboard.Assign("snes", null, chosen, "b", new("f1", "F1")));
        }

        [Fact]
        public void TheConfig_HasTheConsolesKeys_OnlyWhenChosen()
        {
            Assert.Empty(RetroKeyboard.Config("nes", null, null));
            var chosen = RetroKeyboard.Assign("nes", null, null, "a", new("p", "P"))!;
            var config = RetroKeyboard.Config("nes", null, chosen).ToDictionary(c => c.Key, c => c.Value);
            Assert.Equal("p", config["input_player1_a"]);
            Assert.Equal("z", config["input_player1_b"]);
            // The RetroPad's buttons the NES has not: no key; RetroArch's pause on P: off
            Assert.Equal("nul", config["input_player1_x"]);
            Assert.Equal("nul", config["input_pause_toggle"]);
            Assert.False(config.ContainsKey("input_reset"));
        }

        [Fact]
        public void TheChoices_AreKeptAsText()
        {
            var choices = new Dictionary<string, Dictionary<string, RetroKeyboard.Key>> { ["snes"] = new() { ["a"] = new("q", "A") }, ["nes"] = new() };
            string text = RetroKeyboard.Format(choices);
            Assert.Equal("{\"snes\":{\"a\":{\"Name\":\"q\",\"Label\":\"A\"}}}", text);
            Assert.Equal("q", RetroKeyboard.Parse(text)["snes"]["a"].Name);
            Assert.Empty(RetroKeyboard.Parse("broken"));
        }
    }
}
