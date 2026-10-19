using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    
        /// <summary>Searching while far down the page: the page goes back to the top of the results.</summary>
        [AvaloniaFact]
        public async Task ASearch_FarDownThePage_ShowsTheTopOfTheResults()
        {
            var session = await TestSession.GetAsync();
            for (int i = 0; i < 40; i++)
                session.Server.AddGame(1080 + i, $"Search Scroll Game {i:00}", GameType.LINUX_PORTABLE, Array.Empty<byte>(), $"Search Scroll Game {i:00} (L_P).tar.gz");
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            var library = MainWindowViewModel.Instance.Library;
            await library.LoadLibrary();
            var scroll = library.FindControl<ScrollViewer>("uiMainScrollBar")!;
            await TestSession.WaitUntil(() => scroll.Extent.Height > scroll.Viewport.Height + 1500, TimeSpan.FromSeconds(10), "a long library");
            scroll.Offset = new Avalonia.Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
            Dispatcher.UIThread.RunJobs();

            library.FindControl<TextBox>("uiSearch")!.Text = "Search Scroll Game 07";
            await TestSession.WaitUntil(() => library.Model.GameCards.Count == 1, TimeSpan.FromSeconds(10), "the search result");
            Dispatcher.UIThread.RunJobs();
            var results = library.FindControl<StackPanel>("uiAllGames")!;
            var top = results.TranslatePoint(new Avalonia.Point(0, 0), scroll)!.Value;
            Assert.InRange(top.Y, 0, scroll.Viewport.Height - 100);

            library.FindControl<TextBox>("uiSearch")!.Text = "";
            await TestSession.WaitUntil(() => library.Model.GameCards.Count > 1, TimeSpan.FromSeconds(10), "the whole library again");
        }

        /// <summary>
        /// Cards are created ahead while the page is idle, so scrolling down does not create any; their hover
        /// actions only exist once the pointer comes over them.
        /// </summary>
        [AvaloniaFact]
        public async Task TheCards_AreReadyBeforeScrolling_AndGetTheirActionsOnHover()
        {
            var session = await TestSession.GetAsync();
            for (int i = 0; i < 60; i++)
                session.Server.AddGame(1180 + i, $"Ready Card Game {i:00}", GameType.LINUX_PORTABLE, Array.Empty<byte>(), $"Ready Card Game {i:00} (L_P).tar.gz");
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            var library = MainWindowViewModel.Instance.Library;
            await library.LoadLibrary();
            var grid = library.FindControl<ItemsRepeater>("uiServerGamesItemsControl")!;
            var scroll = library.FindControl<ScrollViewer>("uiMainScrollBar")!;
            await TestSession.WaitUntil(() => grid.Children.Count > 20 && grid.Children.Count(c => c.DataContext is Game) < grid.Children.Count, TimeSpan.FromSeconds(10), "cards made ahead");
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            int prepared = grid.Children.Count;

            for (int y = 0; y < 1500; y += 100)
            {
                scroll.Offset = new Avalonia.Vector(0, y);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(prepared, grid.Children.Count);

            var card = grid.Children.First(c => c.DataContext is Game && c.TranslatePoint(new Avalonia.Point(0, 0), scroll) is { } p && p.Y > 0 && p.Y < scroll.Viewport.Height - 300);
            Assert.DoesNotContain(card.GetVisualDescendants(), v => v is Panel p && p.Classes.Contains("hoverOnly"));
            var center = card.TranslatePoint(new Avalonia.Point(card.Bounds.Width / 2, 60), session.Window)!.Value;
            session.Window.MouseMove(center);
            Dispatcher.UIThread.RunJobs();
            var actions = card.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("hoverOnly"));
            Assert.Contains(actions.GetVisualDescendants(), v => v is Avalonia.Controls.Primitives.ToggleButton t && t.Classes.Contains("bookmark") && t.IsChecked == false);
            Assert.Contains(actions.GetVisualDescendants(), v => v is Button b && b.Width == 46 && b.Background is Avalonia.Media.ISolidColorBrush);
            session.Window.MouseMove(new Avalonia.Point(1, 1));
        }
    }
}
