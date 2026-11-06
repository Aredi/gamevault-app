using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class EmulatorScenarios
    {
        private const string Catalog = "{\"emulators\":[{\"id\":\"starfox\",\"name\":\"Eglantine Star Fox\",\"console\":\"switch-2\",\"windows\":\"https://x/e.zip\",\"linux\":null,\"mac\":null}],\"games\":{\"12\":\"starfox\"}}";

        private static void Capture(Window window, string name)
        {
            if (Environment.GetEnvironmentVariable("EMULATOR_SHOTS") is not { Length: > 0 } folder)
                return;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.CaptureRenderedFrame()!.Save(Path.Combine(folder, name + ".png"));
        }

        [AvaloniaFact]
        public async Task TheAdminConsole_ShowsTheEmulatorsByGame_InTheServerPart_AndTheGamePageTheChoice()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            session.Server.EmulatorsJson = Catalog;
            Loc.Initialize("fr");
            var window = new Window { Width = 1400, Height = 1000 };
            try
            {
                var console = new AdminConsoleUserControl();
                window.Content = console;
                window.Show();
                await console.LoadEmulatorsAsync();
                console.FindControl<ToggleButton>("uiTileServer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(console.FindControl<Border>("uiEmulatorsCard")!.IsVisible);
                Assert.Single(((System.Collections.IEnumerable)console.FindControl<ItemsControl>("uiEmulators")!.ItemsSource!).Cast<object>());
                Capture(window, "admin-server");

                var game = new RetroGame { Id = 12, Name = "Star Fox", Platform = new RetroPlatform { Slug = "switch-2", Name = "Nintendo Switch 2" } };
                var page = new RetroGameViewUserControl(game);
                window.Content = page;
                await TestSession.WaitUntil(() => page.FindControl<StackPanel>("uiEmulatorAdmin")!.IsVisible, TimeSpan.FromSeconds(10), "the emulator choice");
                var choice = page.FindControl<ComboBox>("uiEmulatorChoice")!;
                Assert.Equal(1, choice.SelectedIndex);
                Assert.Equal("Eglantine Star Fox (Windows)", ((IEnumerable<string>)choice.ItemsSource!).ElementAt(1));
                // Its only build is Windows': on Linux Eglantine plays it, and the page says why
                Assert.Equal(OperatingSystem.IsWindows() ? "Manettes et image : réglages de Eglantine Star Fox" : "Manettes et image : réglages de Eglantine", page.ViewModel.MenuText);
                Assert.Equal(!OperatingSystem.IsWindows(), page.FindControl<TextBlock>("uiEmulatorNote")!.IsVisible);
                Capture(window, "game-page");
            }
            finally
            {
                window.Close();
                Loc.Initialize("en");
                session.Server.EmulatorsJson = "{\"emulators\":[],\"games\":{}}";
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }
    }
}
