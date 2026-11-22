using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class SwitchModsScenarios
    {
        /// <summary>MODS_SHOTS=folder (with SHOWCASE_DIR set, for real frames): the screens as pictures.</summary>
        private static string? Shots => Environment.GetEnvironmentVariable("MODS_SHOTS") is { Length: > 0 } folder ? folder : null;

        private static async Task Shot(TopLevel window, string name)
        {
            if (Shots is not string folder)
                return;
            // The pictures come from the network
            for (int i = 0; i < 30; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(100);
            }
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(Path.Combine(folder, name + ".png"));
        }

        [AvaloniaFact]
        public async Task ASwitchGamesMod_IsFoundOnGameBanana_InstalledForItsVersion_AndTurnedOff()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            GameBanana.Api = session.Server.Url + "/gb";
            EdenLayout layout = StandaloneEmulator.Eden.Layout;
            Directory.CreateDirectory(Path.GetDirectoryName(layout.Executable)!);
            if (!File.Exists(layout.Executable))
                File.WriteAllText(layout.Executable, "");
            File.WriteAllText(layout.VersionFile, layout.Version);
            var page = new RetroUserControl();
            await page.LoadPlatformsAsync();
            await page.SelectPlatformAsync(page.ViewModel.Platforms.Single(p => p.Slug == "switch"));
            await TestSession.WaitUntil(() => page.Shown.Any(g => g.Name == "1-2-Switch"), TimeSpan.FromSeconds(10), "the Switch games");
            RetroGame game = page.Shown.Single(g => g.Name == "1-2-Switch");
            // Installed here, with its update: its id and its version are in the update's name
            string folder = RetroService.Folder(game, session.LibraryRoot);
            bool wasInstalled = game.Installed;
            string update = Path.Combine(folder, "update", "1-2-Switch v1.1.0[0100000000010800][v65536].nsp");
            Directory.CreateDirectory(Path.GetDirectoryName(update)!);
            if (!wasInstalled)
                File.WriteAllText(Path.Combine(folder, "1-2-Switch.xci"), "xci");
            File.WriteAllText(update, "nsp");
            game.Refresh();
            string? ids = null;
            var panel = new SwitchModsPanel();
            var window = new Window { Width = 1000, Height = 760, Content = panel };
            try
            {
                window.Show();
                panel.Game = game;
                Assert.True(panel.IsVisible);
                // From the update's name (another test's install may have put one with its own id first)
                string id = SwitchModsService.TitleIdOf(game)!;
                Assert.Matches("^0100[0-9A-F]{9}000$", id);
                ids = id;
                Assert.Equal("1.1.0", SwitchModsService.VersionOf(game));
                Assert.True(panel.FindControl<TextBlock>("uiEmpty")!.IsVisible);

                var popup = new ModsPopup(game);
                window.Content = popup;
                Dispatcher.UIThread.RunJobs();
                session.Server.GameBananaShowcase = Shots != null;
                await popup.ListAsync(reset: true);
                var list = popup.FindControl<ListBox>("uiList")!;
                // Not the adult one
                GameBananaMod mod = Assert.Single((IEnumerable<GameBananaMod>)list.ItemsSource!, m => m.Id == 7);
                Assert.DoesNotContain((IEnumerable<GameBananaMod>)list.ItemsSource!, m => m.HasContentRatings);
                list.SelectedItem = mod;
                await popup.ShowAsync(mod);
                var files = popup.FindControl<ComboBox>("uiFiles")!;
                // Not the one the antivirus refused; the one for the game's version chosen, though older
                Assert.Equal(new[] { 70, 71 }, ((IEnumerable<GameBananaFile>)files.ItemsSource!).Select(f => f.Id));
                Assert.Equal(71, ((GameBananaFile)files.SelectedItem!).Id);
                Assert.Contains("1.1.0", popup.FindControl<TextBlock>("uiFileNote")!.Text);
                // The file for the game's 1.0.0: not installed on its 1.1.0, it would not start
                files.SelectedItem = ((IEnumerable<GameBananaFile>)files.ItemsSource!).Single(f => f.Id == 70);
                Assert.False(popup.FindControl<Button>("uiInstall")!.IsEnabled);
                Assert.Contains("1.0.0", popup.FindControl<TextBlock>("uiFileNote")!.Text);
                files.SelectedItem = ((IEnumerable<GameBananaFile>)files.ItemsSource!).Single(f => f.Id == 71);
                Assert.True(popup.FindControl<Button>("uiInstall")!.IsEnabled);
                await Shot(window, "1-find");

                await popup.InstallAsync();
                Assert.True(session.Server.GameBananaDownloads.TryDequeue(out string? downloaded));
                Assert.Equal("/gb/dl/71", downloaded);
                string on = Path.Combine(layout.UserDirectory, "load", id, "Cool Mod");
                Assert.Equal("cool", File.ReadAllText(Path.Combine(on, "romfs", "cool.txt")));
                Assert.True(File.Exists(Path.Combine(on, "exefs", "main.npdm")));
                Assert.False(Directory.Exists(Path.Combine(layout.UserDirectory, "load-tmp")) && Directory.EnumerateFileSystemEntries(Path.Combine(layout.UserDirectory, "load-tmp")).Any());
                Assert.True(((IEnumerable<GameBananaMod>)list.ItemsSource!).Single(m => m.Id == 7).Installed);
                if (Shots != null)
                {
                    list.SelectedItem = ((IEnumerable<GameBananaMod>)list.ItemsSource!).Single(m => m.Id == 7);
                    await TestSession.WaitUntil(() => popup.FindControl<Button>("uiInstall")!.IsEnabled, TimeSpan.FromSeconds(5), "the mod shown again");
                    popup.FindControl<TextBlock>("uiStatus")!.Text = gamevault.Localization.Loc.T("Installed: it is on the next time the game starts.");
                    await Shot(window, "2-installed");
                }

                // On the game's page: on, then off for its next start, then taken away
                window.Content = panel;
                Dispatcher.UIThread.RunJobs();
                ModRow row = Assert.Single((IEnumerable<ModRow>)panel.FindControl<ItemsControl>("uiMods")!.ItemsSource!);
                Assert.True(row.Enabled);
                Assert.True(panel.FindControl<Button>("uiAllOff")!.IsVisible);
                if (Shots != null)
                {
                    // A second mod, turned off, as the page shows them
                    Directory.CreateDirectory(Path.Combine(layout.UserDirectory, "load-off", id, "Serfrosts Defogger", "romfs"));
                    panel.Show();
                    await Shot(window, "3-page");
                    Directory.Delete(Path.Combine(layout.UserDirectory, "load-off", id, "Serfrosts Defogger"), true);
                    panel.Show();
                }
                SwitchModsService.SetEnabled(game, row.Mod.Folder, false);
                Dispatcher.UIThread.RunJobs();
                Assert.False(Directory.Exists(on));
                Assert.False(Assert.Single((IEnumerable<ModRow>)panel.FindControl<ItemsControl>("uiMods")!.ItemsSource!).Enabled);
                // Nothing changes while the game runs
                game.Busy = true;
                Dispatcher.UIThread.RunJobs();
                Assert.False(panel.FindControl<ItemsControl>("uiMods")!.IsEnabled);
                game.Busy = false;
                SwitchModsService.Remove(game, row.Mod.Folder);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(SwitchModsService.Installed(game));
            }
            finally
            {
                session.Server.GameBananaShowcase = false;
                game.Busy = false;
                window.Content = null;
                window.Close();
                File.Delete(update);
                if (!wasInstalled && Directory.Exists(folder))
                    Directory.Delete(folder, true);
                if (ids != null)
                    foreach (string dir in new[] { "load", "load-off" }.Select(d => Path.Combine(layout.UserDirectory, d, ids)))
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, true);
                game.Refresh();
                GameBanana.Api = "https://gamebanana.com/apiv11";
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }
    }
}
