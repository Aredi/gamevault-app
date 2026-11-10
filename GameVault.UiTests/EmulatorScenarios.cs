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

        /// <summary>A recomp's build: its files in the folder of its release, its program a script that writes what it was started with.</summary>
        private static byte[] RecompBuild(string version)
        {
            using var bytes = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var script = new StreamWriter(zip.CreateEntry("zelda-recomp/bin/run.sh").Open()))
                    script.Write("#!/bin/sh\nprintf '%s\\n' \"$@\" > started.txt\n");
                using (var data = new StreamWriter(zip.CreateEntry("zelda-recomp/version.txt").Open()))
                    data.Write(version);
            }
            return bytes.ToArray();
        }

        [AvaloniaFact]
        public async Task ARecomp_IsInstalledAndStartedWithTheGamesFiles_AndKeepsItsSaves()
        {
            if (OperatingSystem.IsWindows())
                return;
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            session.Server.Media[9951] = RecompBuild("1");
            session.Server.Media[9952] = RecompBuild("2");
            string Recomps(int build, string? arguments) => System.Text.Json.JsonSerializer.Serialize(new
            {
                emulators = new[] { new { id = "zelda-recomp-test", name = "Zelda Recompiled", console = "nes", kind = "recomp", windows = (string?)null, linux = $"{session.Server.Url}/api/media/{build}", mac = (string?)null, executable = "bin/run.sh", arguments } },
                games = new Dictionary<string, string> { ["40"] = "zelda-recomp-test" },
            });
            var game = new RetroGame { Id = 40, Name = "Zelda", FileName = "Zelda (E).nes", Platform = new RetroPlatform { Slug = "nes", Name = "NES" } };
            string roms = Path.Combine(Path.GetTempPath(), "gv-recomp-rom-" + Guid.NewGuid().ToString("N"));
            string rom = Path.Combine(roms, "Zelda (E).nes");
            Directory.CreateDirectory(roms);
            File.WriteAllText(rom, "rom");
            TimeSpan poll = RecompProgram.Poll;
            RecompProgram.Poll = TimeSpan.FromMilliseconds(200);
            string? installed = null;
            var window = new Window { Width = 1400, Height = 1000 };
            try
            {
                // The game's page: the recomp plays it, an administrator sees the choice (a NES game has no fork)
                session.Server.EmulatorsJson = Recomps(9951, "--rom {rom} --fast");
                var page = new RetroGameViewUserControl(game);
                window.Content = page;
                window.Show();
                await TestSession.WaitUntil(() => page.FindControl<StackPanel>("uiEmulatorAdmin")!.IsVisible, TimeSpan.FromSeconds(10), "the emulator choice");
                var choice = page.FindControl<ComboBox>("uiEmulatorChoice")!;
                Assert.Equal(new[] { Loc.F("{0} (console)", "RetroArch"), "Zelda Recompiled (Linux)" }, (IEnumerable<string>)choice.ItemsSource!);
                Assert.Equal(1, choice.SelectedIndex);
                Assert.Equal(Loc.F("Controls and picture: {0} settings", "Zelda Recompiled"), page.ViewModel.MenuText);
                Assert.False(page.FindControl<TextBlock>("uiEmulatorNote")!.IsVisible);
                // Not an emulator: the console's stays what the rest of the client knows
                Assert.Null(StandaloneEmulator.Of(game));

                RecompProgram recomp = GameEmulators.RecompOf(game)!;
                installed = recomp.Layout.Directory;
                game.Busy = true;
                await RetroPlay.PlayRecompAsync(game, recomp, rom);
                Assert.Equal(Loc.F("Playing with {0}: close its windows to quit", "Zelda Recompiled"), game.Status);
                await TestSession.WaitUntil(() => !game.Busy, TimeSpan.FromSeconds(20), "the recomp to close");
                // Installed without the folder of its release, started beside its program with the game's file
                Assert.Equal("1", File.ReadAllText(Path.Combine(installed, "version.txt")));
                Assert.Equal(new[] { "--rom", rom, "--fast" }, File.ReadAllLines(Path.Combine(installed, "bin", "started.txt")));
                Assert.Equal("", game.Status);

                // A new link: the new build over the old one, what the recomp wrote stays. Without arguments, the game's path is told
                Directory.CreateDirectory(Path.Combine(installed, "save"));
                File.WriteAllText(Path.Combine(installed, "save", "slot1.sav"), "mine");
                session.Server.EmulatorsJson = Recomps(9952, null);
                await GameEmulators.LoadAsync();
                recomp = GameEmulators.RecompOf(game)!;
                game.Busy = true;
                await RetroPlay.PlayRecompAsync(game, recomp, rom);
                Assert.Equal(Loc.F("Playing with {0}. The game's files are in {1}: this path is copied, paste it when {0} asks for the game.", "Zelda Recompiled", rom), game.Status);
                await TestSession.WaitUntil(() => !game.Busy, TimeSpan.FromSeconds(20), "the recomp to close");
                Assert.Equal("2", File.ReadAllText(Path.Combine(installed, "version.txt")));
                Assert.Equal("mine", File.ReadAllText(Path.Combine(installed, "save", "slot1.sav")));
                Assert.Equal("", File.ReadAllText(Path.Combine(installed, "bin", "started.txt")).Trim());
                // A dump of several files is shown by its folder
                File.WriteAllText(Path.Combine(roms, "update.bin"), "more");
                Assert.Equal(roms, RetroPlay.GameFilesOf(rom, roms));

                // The admin console keeps what makes it a recomp
                var console = new AdminConsoleUserControl();
                window.Content = console;
                await console.LoadEmulatorsAsync();
                var row = Assert.IsType<AdminConsoleUserControl.EmulatorRow>(Assert.Single(((System.Collections.IEnumerable)console.FindControl<ItemsControl>("uiEmulators")!.ItemsSource!).Cast<object>()));
                Assert.True(row.IsRecomp);
                Assert.Equal("NES", row.Consoles[row.ConsoleIndex]);
                row.Arguments = "{folder}";
                GameEmulators.Emulator sent = row.ToEmulator();
                Assert.Equal(("recomp", "nes", "bin/run.sh", "{folder}"), (sent.Kind, sent.Console, sent.Executable, sent.Arguments));
                console.FindControl<ToggleButton>("uiTileServer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Capture(window, "admin-recomp");
            }
            finally
            {
                window.Close();
                RecompProgram.Poll = poll;
                session.Server.EmulatorsJson = "{\"emulators\":[],\"games\":{}}";
                await GameEmulators.LoadAsync();
                SanctuaryService.Current = SanctuaryService.Url;
                Directory.Delete(roms, true);
                if (installed != null && Directory.Exists(installed))
                    Directory.Delete(installed, true);
            }
        }
    }
}
