using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class RetroControllerScenarios
    {
        /// <summary>
        /// A Wii game starts with the Wiimote + Nunchuk, the controller chosen for the Wii in Régler mes touches, or the one
        /// chosen there for the game (opened from its page): written in the game's remap file, which RetroArch reads at its start.
        /// </summary>
        [AvaloniaFact]
        public async Task AWiiGame_StartsWithTheConsolesController_OrTheOneChosenForIt()
        {
            await TestSession.GetAsync();
            var consoles = RetroArchManager.Controllers;
            var games = RetroArchManager.GameControllers;
            string remap = RetroArchManager.Layout.GameRemapFile("dolphin", "/nowhere/zelda.rvz");
            var window = new Window { Width = 1300, Height = 900 };
            try
            {
                RetroArchManager.Controllers = new();
                RetroArchManager.GameControllers = new();
                var zelda = new RetroGame { Id = 812, Name = "Zelda Twilight Princess", Platform = new RetroPlatform { Slug = "wii", Name = "Wii" } };

                // Its page: the controls' button, with what it is played with
                var view = new RetroGameViewUserControl(zelda);
                Assert.True(view.ViewModel.HasControls);
                Assert.Contains("Nunchuk", view.ViewModel.ControlsSummary);
                Assert.Equal(769, RetroArchManager.ControllerOf(zelda));

                // Opened from it: the Wii, its controllers, the Nunchuk's plan
                var controls = new RetroControlsUserControl(zelda);
                window.Content = controls;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("wii", controls.CurrentSystem.Slug);
                Assert.True(controls.FindControl<Border>("uiControllerRow")!.IsVisible);
                Assert.Equal("wiiNunchuk", controls.FindControl<PadDiagram>("uiDiagram")!.Kind);
                ToggleButton Chip(int device) => controls.FindControl<ItemsControl>("uiControllers")!.GetVisualDescendants().OfType<ToggleButton>()
                    .First(b => b.DataContext is RetroControlsUserControl.ControllerChip { Device: var d } && d == device);

                // The console's controller, then the game's own
                Chip(513).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(513, RetroArchManager.ControllerOf(zelda));
                var forGame = controls.FindControl<ComboBox>("uiGameControllerChoice")!;
                forGame.SelectedIndex = 4;
                Assert.Equal(1025, RetroArchManager.ControllerOf(zelda));
                Assert.Equal("wiiClassic", controls.FindControl<PadDiagram>("uiDiagram")!.Kind);
                Assert.Contains("ZL", controls.FindControl<ItemsControl>("uiRows")!.Items.Cast<RetroControlsUserControl.ControlRow>().Select(r => r.Name));
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(zelda.Platform.System, "/nowhere/zelda.rvz", "French", RetroArchManager.ControllerOf(zelda)));
                Assert.EndsWith("\ninput_libretro_device_p1 = \"1025\"\n", File.ReadAllText(remap));
                forGame.SelectedIndex = 0;
                Assert.Equal(513, RetroArchManager.ControllerOf(zelda));

                // A GameCube game (Dolphin too) and a NES game: one controller, nothing to choose
                var gameCube = new RetroControlsUserControl(new RetroGame { Platform = new RetroPlatform { Slug = "ngc", Name = "GameCube" } });
                Assert.False(gameCube.FindControl<Border>("uiControllerRow")!.IsVisible);
                Assert.Null(RetroArchManager.ControllerOf(new RetroGame { Platform = new RetroPlatform { Slug = "nes", Name = "NES" } }));
            }
            finally
            {
                window.Close();
                RetroArchManager.Controllers = consoles;
                RetroArchManager.GameControllers = games;
                if (File.Exists(remap))
                    File.Delete(remap);
            }
        }
    }
}
