using gamevault.Localization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    internal class BitmapHelper
    {
        /// <summary>
        /// Loads a local file or avares:// asset fully into memory (the file is not kept open).
        /// </summary>
        public static Bitmap GetBitmapImage(string uri)
        {
            if (uri.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using Stream asset = AssetLoader.Open(new Uri(uri));
                return new Bitmap(asset);
            }
            using FileStream file = new FileStream(uri, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new Bitmap(file);
        }

        public static async Task<Bitmap?> GetBitmapImageAsync(string uri)
        {
            using var response = await GameVault.Core.HttpClients.Shared.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
                return null;
            using var stream = new MemoryStream();
            await response.Content.CopyToAsync(stream);
            stream.Position = 0;
            return new Bitmap(stream);
        }

        public static Task<Bitmap> GetBitmapImageAsync(MemoryStream stream)
        {
            stream.Position = 0;
            return Task.FromResult(new Bitmap(stream));
        }

        public static async Task<MemoryStream> UrlToMemoryStream(string url)
        {
            MemoryStream stream = new MemoryStream();
            using (HttpResponseMessage response = await GameVault.Core.HttpClients.Shared.GetAsync(url))
            {
                if (response.IsSuccessStatusCode)
                {
                    await response.Content.CopyToAsync(stream);
                    stream.Position = 0;
                }
            }
            return stream;
        }

        public static MemoryStream UriToMemoryStream(string url)
        {
            MemoryStream ms = new MemoryStream();
            using (FileStream file = new FileStream(url, FileMode.Open, FileAccess.Read))
                file.CopyTo(ms);

            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// Encodes an in-memory image as JPEG (for uploads).
        /// </summary>
        public static MemoryStream BitmapSourceToMemoryStream(IImage src)
        {
            if (src is not Bitmap bitmap)
                throw new ArgumentException(Loc.T("Only bitmaps can be uploaded"), nameof(src));
            using var png = new MemoryStream();
            bitmap.Save(png);
            png.Position = 0;
            using SKBitmap image = SKBitmap.Decode(png) ?? throw new ArgumentException(Loc.T("The image could not be read"), nameof(src));
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            var jpeg = new MemoryStream();
            data.SaveTo(jpeg);
            jpeg.Position = 0;
            return jpeg;
        }

        /// <summary>The largest size with the image's aspect ratio that fits into <paramref name="maxWidth"/> × <paramref name="maxHeight"/>.</summary>
        public static SKSizeI FitInto(int width, int height, int maxWidth, int maxHeight)
        {
            double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
            return new SKSizeI(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        }
    }
}
