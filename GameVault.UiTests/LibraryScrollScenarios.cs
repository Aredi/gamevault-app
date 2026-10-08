using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using gamevault.Models;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    public class LibraryScrollScenarios
    {
        /// <summary>The banner changes game every few seconds: the page must stay where the user scrolled to.</summary>
        [AvaloniaFact]
        public async Task TheBannerChangingGame_DoesNotMoveThePage()
        {
            var session = await TestSession.GetAsync();
            for (int i = 0; i < 40; i++)
                session.Server.AddGame(980 + i, $"Scroll Test Game {i:00}", GameType.LINUX_PORTABLE, Array.Empty<byte>(), $"Scroll Test Game {i:00} (L_P).tar.gz");
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            var library = MainWindowViewModel.Instance.Library;
            await library.LoadLibrary();
            var model = library.Model;
            // Two games in the banner
            if (model.HeroItems.Count < 2)
            {
                model.HeroItems.Clear();
                model.HeroItems.Add(new HeroItem { Game = model.GameCards[0], Eyebrow = "A" });
                model.HeroItems.Add(new HeroItem { Game = model.GameCards[1], Eyebrow = "B" });
                model.HeroItemsChanged();
            }
            Dispatcher.UIThread.RunJobs();

            var scroll = library.FindControl<ScrollViewer>("uiMainScrollBar")!;
            await TestSession.WaitUntil(() => scroll.Extent.Height > scroll.Viewport.Height + 900, TimeSpan.FromSeconds(10), "a library taller than the window");
            scroll.Offset = new Avalonia.Vector(0, 900);
            Dispatcher.UIThread.RunJobs();

            for (int i = 0; i < 4; i++)
            {
                model.HeroIndex = (model.HeroIndex + 1) % model.HeroItems.Count;
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(900, scroll.Offset.Y, 1);
            }
        }
    }
}
