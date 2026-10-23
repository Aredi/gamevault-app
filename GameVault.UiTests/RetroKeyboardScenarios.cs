using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Input;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class RetroKeyboardScenarios
    {
        private static (Window, RetroControlsUserControl) Open(string slug, bool keyboard)
        {
            var controls = new RetroControlsUserControl();
            var window = new Window { Width = 1300, Height = 900, Content = controls };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            void Click(Control c) { c.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            Click(controls.FindControl<Avalonia.Controls.Primitives.ToggleButton>(keyboard ? "uiKeyboardTab" : "uiPadTab")!);
            Click(controls.FindControl<ItemsControl>("uiConsoles")!.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .First(b => b.DataContext is RetroControlsUserControl.ConsoleChip chip && chip.System.Slug == slug));
            return (window, controls);
        }

        private static List<RetroControlsUserControl.ControlRow> Rows(RetroControlsUserControl controls) =>
            controls.FindControl<ItemsControl>("uiRows")!.Items.Cast<RetroControlsUserControl.ControlRow>().ToList();

        /// <summary>
        /// Régler mes touches, the keyboard: a console's button takes the next key pressed (by its place, shown as the
        /// keyboard writes it); written in RetroArch's config before that console's games.
        /// </summary>
        [AvaloniaFact]
        public async Task AKeyPressed_IsTheButtonsKey_ForThatConsolesGames()
        {
            await TestSession.GetAsync();
            var keyboard = RetroArchManager.Keyboard;
            var (window, controls) = Open("snes", keyboard: true);
            try
            {
                RetroArchManager.Keyboard = new();
                controls.Refresh();
                Assert.Equal(("A", "X"), (Rows(controls).Single(r => r.Bind == "a").Name, Rows(controls).Single(r => r.Bind == "a").Input));

                // An AZERTY keyboard's A: RetroArch's "q", shown A
                controls.Give("a", RetroKeys.Of(PhysicalKey.Q, "a")!);
                Assert.Equal(new RetroKeyboard.Key("q", "A"), RetroArchManager.Keyboard["snes"]["a"]);
                Assert.Equal("A", Rows(controls).Single(r => r.Bind == "a").Input);
                // L had it: it takes X
                Assert.Equal("X", Rows(controls).Single(r => r.Bind == "l").Input);

                // F1 stays RetroArch's menu
                controls.Give("b", RetroKeys.Of(PhysicalKey.F1, null)!);
                Assert.Contains("F1", controls.FindControl<TextBlock>("uiHint")!.Text);
                Assert.NotEqual("f1", RetroArchManager.Keyboard["snes"]["b"].Name);

                // Before a SNES game: in the client's config (RetroArch itself is not run here)
                Directory.CreateDirectory(Path.GetDirectoryName(RetroArchManager.Layout.ConfigFile)!);
                File.WriteAllText(RetroArchManager.Layout.ConfigFile, "video_fullscreen = \"true\"\n");
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(RetroSystems.All["snes"], "/nowhere/mario.sfc", "French"));
                string config = File.ReadAllText(RetroArchManager.Layout.ConfigFile);
                Assert.Contains("input_player1_a = \"q\"", config);
                Assert.Contains("input_player1_l = \"x\"", config);
                // No gamepad's button set: RetroArch keeps its own driver
                Assert.DoesNotContain("input_joypad_driver", config);

                // Default keys: nothing written for it
                controls.FindControl<Button>("uiDefaults")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(RetroArchManager.Keyboard.ContainsKey("snes"));
            }
            finally
            {
                window.Close();
                RetroArchManager.Keyboard = keyboard;
                if (File.Exists(RetroArchManager.Layout.ConfigFile))
                    File.Delete(RetroArchManager.Layout.ConfigFile);
            }
        }

        /// <summary>
        /// Régler mes touches, the gamepad: a button pressed (SDL's number, as RetroArch's SDL2 driver reads it) plays the
        /// console's button; that console's games are played with SDL2, every button written.
        /// </summary>
        [AvaloniaFact]
        public async Task AGamepadsButton_PlaysTheConsolesButton_WithRetroArchsSdl2()
        {
            await TestSession.GetAsync();
            var pad = RetroArchManager.Pad;
            var (window, controls) = Open("snes", keyboard: false);
            try
            {
                RetroArchManager.Pad = new();
                controls.Refresh();
                Assert.Equal("B", Rows(controls).Single(r => r.Bind == "a").Input);
                // RetroArch is not installed here: nothing read, nothing downloaded
                Assert.True(controls.FindControl<Button>("uiInstall")!.IsVisible);

                // "Set them all": the first button waits and lights up
                controls.FindControl<IconButton>("uiGuided")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("up", controls.Listening);
                Assert.Equal("up", controls.FindControl<PadDiagram>("uiDiagram")!.Highlight);
                controls.Give("up", RetroPad.Hat(0, "up"));
                Assert.Equal("down", controls.Listening);

                // The SNES's A on the top button (Y): X, which had it, takes A's previous one
                controls.Give("a", RetroPad.Button(3));
                Assert.Equal("3", RetroArchManager.Pad["snes"]["a"].Name);
                Assert.Equal("1", RetroArchManager.Pad["snes"]["x"].Name);
                Assert.Equal("Y", Rows(controls).Single(r => r.Bind == "a").Input);

                Directory.CreateDirectory(Path.GetDirectoryName(RetroArchManager.Layout.ConfigFile)!);
                File.WriteAllText(RetroArchManager.Layout.ConfigFile, "video_fullscreen = \"true\"\n");
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(RetroSystems.All["snes"], "/nowhere/mario.sfc", "French"));
                string config = File.ReadAllText(RetroArchManager.Layout.ConfigFile);
                Assert.Contains("input_joypad_driver = \"sdl2\"", config);
                Assert.Contains("input_autodetect_enable = \"false\"", config);
                Assert.Contains("input_player1_a_btn = \"3\"", config);
                Assert.Contains("input_player1_up_btn = \"h0up\"", config);
                Assert.Contains("input_player1_l2_axis = \"+4\"", config);

                // Another console: RetroArch's own driver
                File.WriteAllText(RetroArchManager.Layout.ConfigFile, "video_fullscreen = \"true\"\n");
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(RetroSystems.All["nes"], "/nowhere/zelda.nes", "French"));
                Assert.DoesNotContain("input_joypad_driver", File.ReadAllText(RetroArchManager.Layout.ConfigFile));
            }
            finally
            {
                window.Close();
                RetroArchManager.Pad = pad;
                if (File.Exists(RetroArchManager.Layout.ConfigFile))
                    File.Delete(RetroArchManager.Layout.ConfigFile);
            }
        }

        /// <summary>The key pressed after the click, in the window: the row clicked is rebuilt, the key still reaches it.</summary>
        [AvaloniaFact]
        public async Task TheKeyPressed_AfterTheClick_IsKept()
        {
            await TestSession.GetAsync();
            var keyboard = RetroArchManager.Keyboard;
            var (window, controls) = Open("nes", keyboard: true);
            try
            {
                RetroArchManager.Keyboard = new();
                controls.Refresh();
                // The player is in this window (the click gives it the focus)
                window.Activate();
                controls.FindControl<Avalonia.Controls.Primitives.ToggleButton>("uiKeyboardTab")!.Focus();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var change = controls.FindControl<ItemsControl>("uiRows")!.GetVisualDescendants().OfType<Button>()
                    .First(b => b.DataContext is RetroControlsUserControl.ControlRow { Bind: "a" });
                change.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("enter", RetroArchManager.Keyboard["nes"]["a"].Name);
                // Start had Enter: it takes X, A's previous key
                Assert.Equal("x", RetroArchManager.Keyboard["nes"]["start"].Name);
                // Not waiting any more: the next key is the window's
                window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
                Assert.Equal("enter", RetroArchManager.Keyboard["nes"]["a"].Name);
            }
            finally
            {
                window.Close();
                RetroArchManager.Keyboard = keyboard;
            }
        }

        /// <summary>A profile moved to another address of its server (game. → gamevault.) keeps its RomM link and its keys.</summary>
        [AvaloniaFact]
        public async Task AProfileOnAnotherAddress_KeepsItsLinks()
        {
            await TestSession.GetAsync();
            UserProfile profile = ProfileManager.CreateUserProfile("game.example.com");
            UserProfile? moved = null;
            try
            {
                Preferences.Set(AppConfigKey.RommToken, "secret", profile.UserConfigFile, true);
                Preferences.Set(AppConfigKey.RetroKeyboard, "{\"nes\":{}}", profile.UserConfigFile);
                moved = ProfileManager.ChangeServer(profile, "https://gamevault.example.com");
                Assert.False(Directory.Exists(profile.RootDir));
                Assert.Equal("secret", Preferences.Get(AppConfigKey.RommToken, moved.UserConfigFile, true));
                Assert.Equal("{\"nes\":{}}", Preferences.Get(AppConfigKey.RetroKeyboard, moved.UserConfigFile));
            }
            finally
            {
                if (moved != null)
                    ProfileManager.DeleteUserProfile(moved);
            }
        }
    }
}
