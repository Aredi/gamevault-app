using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using SkiaSharp;

namespace GameVault.UiTests
{
    public class CoverSlideshowScenarios
    {
        private static byte[] Png(SKColor color)
        {
            using var bitmap = new SKBitmap(64, 36);
            bitmap.Erase(color);
            using var image = SKImage.FromBitmap(bitmap);
            return image.Encode(SKEncodedImageFormat.Png, 90).ToArray();
        }

        [AvaloniaFact]
        public async Task AHoveredCard_ShowsItsGamesScreenshots_OneAfterTheOther_AndNothingOnceLeft()
        {
            var session = await TestSession.GetAsync();
            session.Server.Media[9101] = Png(SKColors.Red);
            session.Server.Media[9102] = Png(SKColors.Blue);
            var game = new Game
            {
                ID = 9100,
                Title = "Slides",
                Metadata = new GameMetadata { Screenshots = new[] { $"{session.Server.Url}/api/media/9101", $"{session.Server.Url}/api/media/9102" } },
            };
            var slideshow = new CoverSlideshow();
            var window = new Window { Width = 300, Height = 400, Content = new Button { Content = slideshow, DataContext = game } };
            try
            {
                window.Show();
                Assert.Equal(2, CoverSlideshow.Sources(game).Count);
                Assert.False(slideshow.IsShowing);

                slideshow.Start();
                // Not before the delay: passing over the cards loads nothing
                Dispatcher.UIThread.RunJobs();
                Assert.False(slideshow.IsShowing);
                await TestSession.WaitUntil(() => slideshow.IsShowing, TimeSpan.FromSeconds(10), "the first screenshot");
                var first = slideshow.Shown;
                await TestSession.WaitUntil(() => slideshow.Shown != null && slideshow.Shown != first, TimeSpan.FromSeconds(10), "the second screenshot");

                slideshow.Stop();
                Assert.False(slideshow.IsShowing);

                // A game without screenshots keeps its cover
                Assert.Empty(CoverSlideshow.Sources(new Game { ID = 9103, Title = "Bare" }));
                Assert.Empty(CoverSlideshow.Sources(new RetroGame { Id = 9104, Name = "Bare" }));
            }
            finally
            {
                window.Close();
            }
        }
    }
}
