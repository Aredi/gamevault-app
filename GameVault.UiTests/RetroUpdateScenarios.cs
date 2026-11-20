using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.UserControls;

namespace GameVault.UiTests
{
    public class RetroUpdateScenarios
    {
        /// <summary>RETRO_UPDATE_SHOTS=folder: the page before and after, as pictures.</summary>
        private static void Capture(Window window, string name)
        {
            if (Environment.GetEnvironmentVariable("RETRO_UPDATE_SHOTS") is not { Length: > 0 } folder)
                return;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.CaptureRenderedFrame()!.Save(Path.Combine(folder, name + ".png"));
        }

        [AvaloniaFact]
        public async Task AnUpdateAddedToRomm_AfterTheInstall_IsOffered_AndComesIntoTheGamesFolder()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            session.Server.SwitchUpdate = true;
            Loc.Initialize("fr");
            var game = new RetroGame { Id = 70, Name = "1-2-Switch", FileName = "1-2-Switch.xci", Multi = true, Platform = new RetroPlatform { Slug = "switch", Name = "Nintendo Switch" } };
            // Installed before the update was in RomM
            string folder = RetroService.Folder(game, session.LibraryRoot);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "1-2-Switch.xci"), "xci");
            var window = new Window { Width = 1400, Height = 900 };
            try
            {
                var page = new RetroGameViewUserControl(game);
                window.Content = page;
                window.Show();
                await TestSession.WaitUntil(() => page.ViewModel.HasUpdates, TimeSpan.FromSeconds(10), "the update offered");
                Assert.Equal("1-2-Switch [01000320000CC800][v393216].nsp", Path.GetFileName(Assert.Single(page.ViewModel.Updates).Path));
                Capture(window, "1-update-available");

                Button button = page.FindControl<Button>("uiUpdate")!;
                Assert.True(button.IsVisible);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string update = Path.Combine(folder, "update", "1-2-Switch [01000320000CC800][v393216].nsp");
                await TestSession.WaitUntil(() => !page.ViewModel.HasUpdates && !game.Busy, TimeSpan.FromSeconds(10), "the update downloaded");
                Assert.Equal(FakeGameVaultServer.SwitchUpdateFile, File.ReadAllBytes(update));
                Assert.False(Directory.Exists(folder + ".update"));
                Assert.Equal("1-2-Switch.xci", Path.GetFileName(game.RomFile));
                Assert.Equal("Mise à jour installée : elle s'applique au lancement du jeu.", game.Status);
                Capture(window, "2-update-installed");
            }
            finally
            {
                window.Close();
                Loc.Initialize("en");
                session.Server.SwitchUpdate = false;
                SanctuaryService.Current = SanctuaryService.Url;
                Directory.Delete(folder, true);
            }
        }
    }
}
