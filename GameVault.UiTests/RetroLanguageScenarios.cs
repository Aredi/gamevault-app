using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.UserControls;
using gamevault.UserControls.SettingsComponents;

namespace GameVault.UiTests
{
    public class RetroLanguageScenarios
    {
        /// <summary>
        /// A 3DS game starts in the language of Settings > Retro (the client's by default), or the one chosen on its page
        /// among its own: written in the core's options before RetroArch starts.
        /// </summary>
        [AvaloniaFact]
        public async Task AGame_StartsInTheLanguageOfTheSettings_OrTheOneChosenOnItsPage()
        {
            await TestSession.GetAsync();
            string setting = RetroArchManager.LanguageSetting;
            var games = RetroArchManager.GameLanguages;
            string options = RetroArchManager.Layout.CoreOptionsFile("azahar");
            try
            {
                RetroArchManager.LanguageSetting = "";
                var echoes = new RetroGame
                {
                    Id = 559, Name = "Fire Emblem Echoes", FileName = "Fire Emblem Echoes (Europe) (En,Fr,De,Es,It,Nl).3ds",
                    Platform = new RetroPlatform { Slug = "3ds", Name = "Nintendo 3DS" },
                    Languages = new[] { "English", "French", "German", "Spanish", "Italian", "Dutch" },
                };

                // Settings > Retro: the client's language first, chosen
                var settings = new RetroSettingsUserControl();
                await settings.RefreshAsync();
                var setting3ds = settings.FindControl<ComboBox>("uiLanguage")!;
                Assert.Equal(0, setting3ds.SelectedIndex);
                Assert.Equal(7, setting3ds.ItemCount);

                // Its page: its languages in the header, "Automatic" and the ones it has that the emulators offer (not Dutch)
                var view = new RetroGameViewUserControl(echoes);
                Assert.Equal("EN · FR · DE · ES · IT · NL", view.ViewModel.LanguageCodes);
                Assert.True(view.ViewModel.HasConsoleLanguage);
                Assert.Equal(6, view.ViewModel.LanguageOptions.Count);
                Assert.Equal(0, view.ViewModel.LanguageIndex);

                // Automatic: the setting's, written for the core (RetroArch itself is not run here)
                RetroArchManager.LanguageSetting = "French";
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(echoes.Platform.System, "/nowhere/echoes.3ds", RetroArchManager.LanguageOf(echoes)));
                Assert.Contains("citra_language_value = \"French\"", File.ReadAllText(options));

                // Chosen on its page: kept for it, and the one it starts in
                view.ViewModel.LanguageIndex = view.ViewModel.LanguageOptions.Count - 1;
                Assert.Equal("Italian", RetroArchManager.LanguageOf(echoes));
                Assert.Equal(view.ViewModel.LanguageOptions.Count - 1, new RetroGameViewUserControl(echoes).ViewModel.LanguageIndex);
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(echoes.Platform.System, "/nowhere/echoes.3ds", RetroArchManager.LanguageOf(echoes)));
                Assert.Contains("citra_language_value = \"Italian\"", File.ReadAllText(options));
                view.ViewModel.LanguageIndex = 0;
                Assert.Equal("French", RetroArchManager.LanguageOf(echoes));

                // A NES game: no language to choose
                Assert.False(new RetroGameViewUserControl(new RetroGame { Platform = new RetroPlatform { Slug = "nes", Name = "NES" } }).ViewModel.HasConsoleLanguage);
            }
            finally
            {
                RetroArchManager.LanguageSetting = setting;
                RetroArchManager.GameLanguages = games;
                if (File.Exists(options))
                    File.Delete(options);
            }
        }
    }
}
