using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using gamevault.Helper;
using gamevault.UserControls;
using gamevault.UserControls.SettingsComponents;

namespace GameVault.UiTests
{
    public class RetroControllerScenarios
    {
        /// <summary>
        /// A Wii game starts with the Wiimote + Nunchuk, the controller of Settings > Retro, or the one chosen on its page:
        /// written in the game's remap file, which RetroArch reads at its start.
        /// </summary>
        [AvaloniaFact]
        public async Task AWiiGame_StartsWithTheControllerOfTheSettings_OrTheOneChosenOnItsPage()
        {
            await TestSession.GetAsync();
            var consoles = RetroArchManager.Controllers;
            var games = RetroArchManager.GameControllers;
            string remap = RetroArchManager.Layout.GameRemapFile("dolphin", "/nowhere/zelda.rvz");
            try
            {
                RetroArchManager.Controllers = new();
                RetroArchManager.GameControllers = new();
                var zelda = new RetroGame { Id = 812, Name = "Zelda Twilight Princess", Platform = new RetroPlatform { Slug = "wii", Name = "Wii" } };

                // Settings > Retro: a row per console with several controllers, the default chosen
                var settings = new RetroSettingsUserControl();
                await settings.RefreshAsync();
                var rows = settings.FindControl<ItemsControl>("uiControllers")!.Items.Cast<RetroSettingsUserControl.ControllerRow>().ToList();
                Assert.Equal(new[] { "wii", "psx" }, rows.Select(r => r.Slug));
                Assert.All(rows, r => Assert.Equal(0, r.Index));

                // Its page: "Automatic" (the Nunchuk), then the Wii's controllers
                var view = new RetroGameViewUserControl(zelda);
                Assert.True(view.ViewModel.HasControllerChoice);
                Assert.Equal(6, view.ViewModel.ControllerOptions.Count);
                Assert.Contains("Nunchuk", view.ViewModel.ControllerOptions[0]);
                Assert.Equal(769, RetroArchManager.ControllerOf(zelda));

                // The console's own, then the game's: written for RetroArch before it starts (it is not run here)
                RetroArchManager.Controllers = new() { ["wii"] = 513 };
                Assert.Equal(513, RetroArchManager.ControllerOf(zelda));
                view.ViewModel.ControllerIndex = 4;
                Assert.Equal(1025, RetroArchManager.ControllerOf(zelda));
                Assert.Equal(4, new RetroGameViewUserControl(zelda).ViewModel.ControllerIndex);
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(zelda.Platform.System, "/nowhere/zelda.rvz", "French", RetroArchManager.ControllerOf(zelda)));
                Assert.EndsWith("\ninput_libretro_device_p1 = \"1025\"\n", File.ReadAllText(remap));
                view.ViewModel.ControllerIndex = 0;
                Assert.Equal(513, RetroArchManager.ControllerOf(zelda));

                // A GameCube game (Dolphin too) and a NES game: one controller, nothing to choose
                Assert.False(new RetroGameViewUserControl(new RetroGame { Platform = new RetroPlatform { Slug = "ngc", Name = "GameCube" } }).ViewModel.HasControllerChoice);
                Assert.Null(RetroArchManager.ControllerOf(new RetroGame { Platform = new RetroPlatform { Slug = "nes", Name = "NES" } }));
            }
            finally
            {
                RetroArchManager.Controllers = consoles;
                RetroArchManager.GameControllers = games;
                if (File.Exists(remap))
                    File.Delete(remap);
            }
        }
    }
}
