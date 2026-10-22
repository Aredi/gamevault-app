using GameVault.Core.Retro;

namespace GameVault.Core.Tests
{
    public class RetroLanguageTests
    {
        [Fact]
        public void TheCores_TakeTheLanguage_InTheirOwnValues()
        {
            Assert.Equal(("citra_language_value", "French"), RetroLanguage.CoreOption("azahar", "French"));
            Assert.Equal(("melonds_firmware_language", "fr"), RetroLanguage.CoreOption("melondsds", "French"));
            Assert.Equal(("melonds_firmware_language", "ja"), RetroLanguage.CoreOption("melondsds", "Japanese"));
            Assert.Equal(("dolphin_language", "3"), RetroLanguage.CoreOption("dolphin", "French"));
            Assert.Equal(("dolphin_language", "1"), RetroLanguage.CoreOption("dolphin", "English"));
            Assert.Null(RetroLanguage.CoreOption("mesen", "French"));
            Assert.Null(RetroLanguage.CoreOption("pcsx2", "French"));
        }

        [Fact]
        public void Rpcs3AndEden_TakeItInTheirSettings_TheRestKept()
        {
            var rpcs3 = new Rpcs3Layout("/rpcs3", windows: false);
            string config = "Video:\n  Resolution Scale: 200\nSystem:\n  License Area: SCEE\n  Language: English (US)\n";
            Assert.Equal(config.Replace("English (US)", "French"), rpcs3.Language(config, "French"));
            Assert.Equal("Video:\n  Resolution Scale: 100\nSystem:\n  Language: English (US)\n", rpcs3.Language("Video:\n  Resolution Scale: 100\n", "English"));

            var eden = new EdenLayout("/eden", windows: false);
            string merged = eden.Language("[Renderer]\nbackend=1\n[System]\nlanguage_index\\default=true\nlanguage_index=1\n", "Spanish");
            Assert.Equal("[Renderer]\nbackend=1\n[System]\nlanguage_index\\default=false\nlanguage_index=5\n", merged);
        }

        [Fact]
        public void AGame_StartsInTheOneChosenForIt_ElseTheSettingsWhenItHasIt_ElseItsOwn()
        {
            string[] europe = { "English", "French", "German" };
            Assert.Equal("Japanese", RetroLanguage.Pick("Japanese", "French", europe));
            Assert.Equal("French", RetroLanguage.Pick(null, "French", europe));
            // RomM does not know its languages: the setting's
            Assert.Equal("French", RetroLanguage.Pick(null, "French", Array.Empty<string>()));
            // A Japanese game: Japanese, not a French console it does not know
            Assert.Equal("Japanese", RetroLanguage.Pick(null, "French", new[] { "Japanese" }));
            Assert.Equal("French", RetroLanguage.Pick(null, "French", new[] { "Chinese" }));
        }

        [Fact]
        public void TheGamesChoices_KeepOnlyKnownLanguages()
        {
            Assert.Equal(new Dictionary<int, string> { [12] = "Japanese", [559] = "French" }, RetroLanguage.Parse("559=French;12=Japanese;7=Klingon;x=French;broken"));
            Assert.Equal("12=Japanese;559=French", RetroLanguage.Format(new Dictionary<int, string> { [559] = "French", [12] = "Japanese" }));
        }
    }
}
