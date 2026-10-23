using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Input;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls.SettingsComponents;

namespace GameVault.UiTests
{
    public class RetroKeyboardScenarios
    {
        /// <summary>
        /// Settings > Retro: a console's button, clicked, takes the next key pressed (by its place, shown as the keyboard
        /// writes it); written in RetroArch's config before that console's games.
        /// </summary>
        [AvaloniaFact]
        public async Task AKeyPressed_IsTheButtonsKey_ForThatConsolesGames()
        {
            await TestSession.GetAsync();
            var keyboard = RetroArchManager.Keyboard;
            try
            {
                RetroArchManager.Keyboard = new();
                var settings = new RetroSettingsUserControl();
                await settings.RefreshAsync();
                var console = settings.FindControl<ComboBox>("uiKeyboardConsole")!;
                console.SelectedIndex = Array.FindIndex(RetroSettingsUserControl.KeyboardSystems, s => s.Slug == "snes");
                var rows = settings.FindControl<ItemsControl>("uiKeys")!.Items.Cast<RetroSettingsUserControl.KeyRow>().ToList();
                Assert.Equal(("A", "X"), (rows.Single(r => r.Button.Bind == "a").Name, rows.Single(r => r.Button.Bind == "a").KeyText));

                // An AZERTY keyboard's A: RetroArch's "q", shown A
                settings.Give("a", RetroKeys.Of(PhysicalKey.Q, "a"));
                Assert.Equal(new RetroKeyboard.Key("q", "A"), RetroArchManager.Keyboard["snes"]["a"]);
                rows = settings.FindControl<ItemsControl>("uiKeys")!.Items.Cast<RetroSettingsUserControl.KeyRow>().ToList();
                Assert.Equal("A", rows.Single(r => r.Button.Bind == "a").KeyText);
                // L had it: it takes X
                Assert.Equal("X", rows.Single(r => r.Button.Bind == "l").KeyText);

                // F1 stays RetroArch's menu
                settings.Give("b", RetroKeys.Of(PhysicalKey.F1, null));
                Assert.True(settings.FindControl<TextBlock>("uiKeyboardMessage")!.IsVisible);
                Assert.False(RetroArchManager.Keyboard["snes"].ContainsKey("b") && RetroArchManager.Keyboard["snes"]["b"].Name == "f1");

                // Before a SNES game: in the client's config (RetroArch itself is not run here)
                Directory.CreateDirectory(Path.GetDirectoryName(RetroArchManager.Layout.ConfigFile)!);
                File.WriteAllText(RetroArchManager.Layout.ConfigFile, "video_fullscreen = \"true\"\n");
                Assert.ThrowsAny<Exception>(() => RetroArchManager.Launch(RetroSystems.All["snes"], "/nowhere/mario.sfc", "French"));
                string config = File.ReadAllText(RetroArchManager.Layout.ConfigFile);
                Assert.Contains("input_player1_a = \"q\"", config);
                Assert.Contains("input_player1_l = \"x\"", config);

                // Default keys: nothing written for it
                settings.FindControl<Avalonia.Controls.Control>("uiKeyboardDefaults")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(RetroArchManager.Keyboard.ContainsKey("snes"));
            }
            finally
            {
                RetroArchManager.Keyboard = keyboard;
                if (File.Exists(RetroArchManager.Layout.ConfigFile))
                    File.Delete(RetroArchManager.Layout.ConfigFile);
            }
        }

        /// <summary>The key pressed after the click, in the window: the button clicked is rebuilt, the key still reaches it.</summary>
        [AvaloniaFact]
        public async Task TheKeyPressed_AfterTheClick_IsKept()
        {
            await TestSession.GetAsync();
            var keyboard = RetroArchManager.Keyboard;
            var window = new Window { Width = 900, Height = 900 };
            try
            {
                RetroArchManager.Keyboard = new();
                var settings = new RetroSettingsUserControl();
                window.Content = settings;
                window.Show();
                await settings.RefreshAsync();
                var console = settings.FindControl<ComboBox>("uiKeyboardConsole")!;
                console.SelectedIndex = Array.FindIndex(RetroSettingsUserControl.KeyboardSystems, s => s.Slug == "nes");
                // The player is in this window (the click gives it the focus)
                window.Activate();
                console.Focus();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var a = settings.FindControl<ItemsControl>("uiKeys")!.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                    .First(b => b.DataContext is RetroSettingsUserControl.KeyRow { Button.Bind: "a" });
                a.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
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
