using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using gamevault.Helper;

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
    }
}
