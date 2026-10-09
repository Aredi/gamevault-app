using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    public class ImageScenarios
    {
        /// <summary>A JPEG of the given size, written to a temporary file.</summary>
        private static string Jpeg(int width, int height)
        {
            string file = Path.Combine(Path.GetTempPath(), $"cover-{Guid.NewGuid():N}.jpg");
            using var bitmap = new SkiaSharp.SKBitmap(width, height);
            using (var canvas = new SkiaSharp.SKCanvas(bitmap))
                canvas.Clear(new SkiaSharp.SKColor(200, 80, 40));
            using var data = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
            File.WriteAllBytes(file, data.ToArray());
            return file;
        }

        [AvaloniaFact]
        public async Task LargeCovers_AreDecodedSmaller_SmallOnesAsTheyAre()
        {
            await TestSession.GetAsync();
            string large = Jpeg(1200, 1800), small = Jpeg(300, 450);
            try
            {
                // Loading must never fail (the files used to be closed between reading the size and decoding)
                Bitmap decoded = DecodedImages.Load(large, 480);
                Assert.Same(decoded, DecodedImages.TryGet(large, 480));
                Bitmap smallDecoded = DecodedImages.Load(small, 480);
                Bitmap full = DecodedImages.Load(large, null);
                // Pixel sizes are only real with the Skia renderer (SHOWCASE_DIR); headless bitmaps are 1 x 1
                if (ShowcaseScenarios.Enabled)
                {
                    Assert.Equal(new Avalonia.PixelSize(480, 720), decoded.PixelSize);
                    Assert.Equal(new Avalonia.PixelSize(300, 450), smallDecoded.PixelSize);
                    Assert.Equal(new Avalonia.PixelSize(1200, 1800), full.PixelSize);
                }
            }
            finally
            {
                File.Delete(large);
                File.Delete(small);
            }
        }

        /// <summary>
        /// Covers asked for while the library loads (the banner and "Continue playing" come first) used to wait
        /// forever: loading the library emptied the download queue under them.
        /// </summary>
        [AvaloniaFact]
        public async Task CoversAskedBeforeTheLibraryLoads_StillArrive_AndAreDownloadedOnce()
        {
            var session = await TestSession.GetAsync();
            string jpeg = Jpeg(300, 450);
            int mediaId = 88_001;
            session.Server.Media[mediaId] = File.ReadAllBytes(jpeg);
            File.Delete(jpeg);
            var game = new Game { ID = 8801, Title = "Slow Cover", Metadata = new GameMetadata { Title = "Slow Cover", Cover = new Media { ID = mediaId } } };
            session.Server.MediaDelay = TimeSpan.FromMilliseconds(800);
            try
            {
                var banner = new CacheImage { ImageCacheType = ImageCache.GameCover };
                var card = new CacheImage { ImageCacheType = ImageCache.GameCover };
                banner.Data = game;
                card.Data = game;
                await MainWindowViewModel.Instance.Library.LoadLibrary();
                await TestSession.WaitUntil(() => banner.GetImageSource() != null && card.GetImageSource() != null, TimeSpan.FromSeconds(10), "both covers");
                Assert.False(banner.IsShowingReplacement);
                Assert.False(card.IsShowingReplacement);
                Assert.Single(session.Server.Requests, r => r.EndsWith($"/api/media/{mediaId}"));
            }
            finally
            {
                session.Server.MediaDelay = TimeSpan.Zero;
            }
        }

        /// <summary>A cover that arrives late does not replace the cover of the game shown since.</summary>
        [AvaloniaFact]
        public async Task ALateCover_DoesNotReplaceTheNewerOne()
        {
            var session = await TestSession.GetAsync();
            Game Make(int id, int media, int red)
            {
                using var bitmap = new SkiaSharp.SKBitmap(30, 45);
                using (var canvas = new SkiaSharp.SKCanvas(bitmap))
                    canvas.Clear(new SkiaSharp.SKColor((byte)red, 10, 10));
                using var data = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
                session.Server.Media[media] = data.ToArray();
                return new Game { ID = id, Title = $"Game {id}", Metadata = new GameMetadata { Title = $"Game {id}", Cover = new Media { ID = media } } };
            }
            Game slow = Make(8811, 88_011, 250), fast = Make(8812, 88_012, 20);
            var image = new CacheImage { ImageCacheType = ImageCache.GameCover };
            session.Server.MediaDelay = TimeSpan.FromMilliseconds(700);
            image.Data = slow;
            await Task.Delay(100);
            session.Server.MediaDelay = TimeSpan.Zero;
            image.Data = fast;
            await TestSession.WaitUntil(() => image.GetImageSource() != null, TimeSpan.FromSeconds(10), "the cover");
            var shown = image.GetImageSource();
            await Task.Delay(1500);// the slow one has arrived meanwhile
            Assert.Same(shown, image.GetImageSource());
            string cache = Path.Combine(LoginManager.Instance.GetUserProfile().ImageCacheDir, "gbox");
            Assert.Same(DecodedImages.TryGet(Path.Combine(cache, "8812.88012"), 480), image.GetImageSource());
        }
    }
}
