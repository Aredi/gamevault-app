using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GameVault.Core.Retro;
using gamevault.Helper;
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
                Assert.Equal(("A", "X (QWERTY)"), (rows.Single(r => r.Button.Bind == "a").Name, rows.Single(r => r.Button.Bind == "a").KeyText));

                // An AZERTY keyboard's A: RetroArch's "q", shown A
                settings.Give("a", RetroKeys.Of(PhysicalKey.Q, "a"));
                Assert.Equal(new RetroKeyboard.Key("q", "A"), RetroArchManager.Keyboard["snes"]["a"]);
                rows = settings.FindControl<ItemsControl>("uiKeys")!.Items.Cast<RetroSettingsUserControl.KeyRow>().ToList();
                Assert.Equal("A", rows.Single(r => r.Button.Bind == "a").KeyText);
                // L had it: it takes X
                Assert.Equal("X (QWERTY)", rows.Single(r => r.Button.Bind == "l").KeyText);

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
    }
}
