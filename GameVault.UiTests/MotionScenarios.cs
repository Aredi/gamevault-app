using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>The page's arrival and its depth: played once, then everything is where it belongs, fully shown.</summary>
    public class MotionScenarios
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [AvaloniaFact]
        public async Task AGamePage_ComesIn_ThenStaysFullyShown_AndItsArtScrollsSlower()
        {
            var session = await TestSession.GetAsync();
            Game game = session.Server.AddGame(9901, "Motion Game", GameType.WINDOWS_PORTABLE, Array.Empty<byte>(), "Motion Game (W_P).zip");
            var page = new GameViewUserControl(game);
            MainWindowViewModel.Instance.SetActiveControl(page);
            Control header = page.FindControl<Control>("uiHeaderText")!, body = page.FindControl<Control>("uiBody")!, cover = page.FindControl<Control>("uiCoverFrame")!;

            // Coming in: not shown at once (headless, the frames come when asked for)
            await TestSession.WaitUntil(() => page.IsLoaded, Timeout, "the page");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(body.Opacity < 1, "the page's body comes in after the header");

            // Then in place and fully shown: an animation never leaves a part of the page faded or moved
            await TestSession.WaitUntil(() =>
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                return new[] { header, body, cover }.All(c => c.Opacity == 1 && c.RenderTransform is null);
            }, Timeout, "the page fully shown");

            // Scrolled: the art follows at less than the page's speed
            var scroll = page.FindControl<ScrollViewer>("uiPageScroll")!;
            scroll.Offset = new Vector(0, 300);
            Dispatcher.UIThread.RunJobs();
            var art = page.FindControl<Control>("uiHeroArt")!;
            double depth = Assert.IsType<TranslateTransform>(art.RenderTransform).Y;
            Assert.InRange(depth, 1, scroll.Offset.Y - 1);
        }
    
        [AvaloniaFact]
        public async Task APopup_OpensSoftly_ThenIsFullyShown()
        {
            var session = await TestSession.GetAsync();
            var layer = session.Window.View.FindControl<Control>("uiPopupLayer")!;
            var frame = session.Window.View.FindControl<Control>("uiPopupFrame")!;
            MainWindowViewModel.Instance.ClosePopup();
            // What they are at rest (a style may give the panel its own transform): given back after
            var layerAtRest = layer.RenderTransform;
            var frameAtRest = frame.RenderTransform;
            MainWindowViewModel.Instance.OpenPopup(new Border { Width = 300, Height = 200 });
            try
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Assert.True(layer.Opacity < 1, "the page darkens");
                await TestSession.WaitUntil(() =>
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    return layer.Opacity == 1 && layer.RenderTransform == layerAtRest && frame.RenderTransform == frameAtRest;
                }, Timeout, "the popup fully shown");
            }
            finally { MainWindowViewModel.Instance.ClosePopup(); }
        }

        /// <summary>With SHOWCASE_DIR (Skia): the downloads page with nothing in it, as a player first sees it.</summary>
        [AvaloniaFact]
        public async Task TheEmptyDownloads_SayWhatComesThere_AndLeadToTheLibrary()
        {
            if (!ShowcaseScenarios.Enabled)
                return;
            var session = await TestSession.GetAsync();
            gamevault.Localization.Loc.Initialize("fr");
            if (DownloadsViewModel.Instance.DownloadedGames.Count > 0 || DownloadsViewModel.Instance.RetroDownloads.Count > 0)
                return;// other tests left some: not the empty page
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Downloads);
            for (int i = 0; i < 20; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(50);
            }
            session.Window.CaptureRenderedFrame()!.Save(Path.Combine(ShowcaseScenarios.Folder!, "downloads-empty.png"));
        }
    }
}
