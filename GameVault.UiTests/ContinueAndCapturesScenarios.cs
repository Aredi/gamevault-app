using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    public class ContinueAndCapturesScenarios
    {
        /// <summary>CONTINUE_SHOTS=folder: the screens as pictures.</summary>
        private static void Shot(TopLevel window, string name)
        {
            if (Environment.GetEnvironmentVariable("CONTINUE_SHOTS") is not { Length: > 0 } folder)
                return;
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(Path.Combine(folder, name + ".png"));
        }

        private static void Png(string path)
        {
            using var bitmap = new SkiaSharp.SKBitmap(64, 36);
            bitmap.Erase(SkiaSharp.SKColors.Teal);
            using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, png.ToArray());
        }

        private static RetroGame InstalledRetroGame(TestSession session, int id, string name)
        {
            var game = new RetroGame { Id = id, Name = name, FileName = name + ".nes", Platform = new RetroPlatform { Slug = "nes", Name = "NES" } };
            string folder = RetroService.Folder(game, session.LibraryRoot);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".nes"), "nes");
            RetroLibrary.Describe(new[] { game });
            return game;
        }

        [AvaloniaFact]
        public async Task ARetroGamePlayedHere_IsInTheLibrarysContinuePlaying_AndFollowsTheSearch()
        {
            var session = await TestSession.GetAsync();
            SanctuaryService.Current = session.Server.Url;
            RetroGame game = InstalledRetroGame(session, 4242, "Zelda Continue");
            // Installed, never played: not in the row
            RetroGame never = InstalledRetroGame(session, 4243, "Never Played");
            session.Server.RetroMinutes["nes:4242"] = 30;
            var installs = MainWindowViewModel.Instance.Library.GetGameInstalls();
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            try
            {
                await RetroLibrary.LoadAsync();
                RetroGame Shown() => InstallViewModel.Instance.ContinueItems.OfType<RetroGame>().FirstOrDefault(g => g.Id == 4242)!;
                await TestSession.WaitUntil(() => Shown() != null, TimeSpan.FromSeconds(10), "the retro game in Continue playing");
                Assert.DoesNotContain(InstallViewModel.Instance.ContinueItems.OfType<RetroGame>(), g => g.Id == never.Id);
                // Played a moment ago: before the PC games
                Assert.Equal(0, InstallViewModel.Instance.ContinueItems.IndexOf(Shown()));
                Assert.Equal(InstallViewModel.Instance.ContinueItems.Count, InstallViewModel.Instance.VisibleInstalledCount);
                Assert.Contains("NES", Shown().ContinueInfo);

                // Its card is made by its own template: its play button is the retro game's
                Dispatcher.UIThread.RunJobs();
                var scroll = installs.FindControl<ScrollViewer>("uiInstalledGamesScroll")!;
                var row = (ItemsControl)scroll.Content!;
                var retroCard = Assert.IsType<Button>(row.ItemTemplate!.Build(Shown()));
                Assert.Contains(retroCard.GetLogicalDescendants().OfType<Image>(), i => i.Name == null);
                var pcCard = Assert.IsType<Button>(row.ItemTemplate!.Build(new KeyValuePair<gamevault.Models.Game, string>(new gamevault.Models.Game { ID = 1, Title = "PC" }, "/x")));
                Assert.DoesNotContain(pcCard.GetLogicalDescendants().OfType<Image>(), i => i.Name == null);

                Shot(session.Window, "1-continue-playing");
                installs.ApplySearch("nothing like it");
                Dispatcher.UIThread.RunJobs();
                Assert.Null(Shown());
                installs.ApplySearch("zelda cont");
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(Shown());
            }
            finally
            {
                installs.ApplySearch("");
                session.Server.RetroMinutes.TryRemove("nes:4242", out _);
                Directory.Delete(RetroService.Folder(game, session.LibraryRoot), true);
                Directory.Delete(RetroService.Folder(never, session.LibraryRoot), true);
                await RetroLibrary.LoadAsync();
                SanctuaryService.Current = SanctuaryService.Url;
            }
        }

        [AvaloniaFact]
        public async Task AScreenshotTakenWhileARetroGameRuns_IsShownOnItsPage()
        {
            var session = await TestSession.GetAsync();
            RetroGame game = InstalledRetroGame(session, 4250, "Captured Game");
            string shots = Path.Combine(session.LibraryRoot, "shots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(shots);
            var window = new Window { Width = 1400, Height = 900 };
            try
            {
                var page = new RetroGameViewUserControl(game);
                window.Content = page;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var strip = page.FindControl<CapturesStrip>("uiCaptures")!;
                Assert.False(strip.IsVisible);

                // Nothing runs: a picture taken on the desktop is no game's
                string before = Path.Combine(shots, "desktop.png");
                Png(before);
                GameCaptures.Seen(before);

                RetroPlay.Raise(true, game);
                Dispatcher.UIThread.RunJobs();
                string shot = Path.Combine(shots, "Captured Game 2026-11-20.png");
                Png(shot);
                GameCaptures.Seen(shot);
                // A file that is no picture is not a capture
                string other = Path.Combine(shots, "notes.txt");
                File.WriteAllText(other, "x");
                GameCaptures.Seen(other);
                string video = Path.Combine(shots, "Captured Game.mp4");
                File.WriteAllText(video, "mp4");
                GameCaptures.Seen(video);

                await TestSession.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return strip.IsVisible; }, TimeSpan.FromSeconds(10), "the captures shown");
                var items = (IEnumerable<CaptureItem>)strip.FindControl<ItemsControl>("uiItems")!.ItemsSource!;
                Assert.Equal(new[] { video, shot }.OrderBy(p => p), items.Select(i => i.Capture.Path).OrderBy(p => p));
                Assert.True(items.Single(i => i.Capture.Path == video).IsVideo);
                await TestSession.WaitUntil(() => { Dispatcher.UIThread.RunJobs(); return items.Single(i => i.Capture.Path == shot).Thumb != null; }, TimeSpan.FromSeconds(10), "the screenshot's thumbnail");

                strip.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                Shot(window, "2-captures");
                // Another game's page has none
                Assert.Empty(GameCaptures.Of(GameCaptures.PcKey(1)));
            }
            finally
            {
                RetroPlay.Raise(false, game);
                Dispatcher.UIThread.RunJobs();
                window.Close();
                Directory.Delete(shots, true);
                Directory.Delete(RetroService.Folder(game, session.LibraryRoot), true);
            }
        }
    }
}
