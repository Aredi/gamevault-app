using ImageMagick;
using System;
using System.IO;
using System.Text;


namespace gamevault.Helper
{
    /// <summary>
    /// GIF detection and resizing. Animated GIFs are displayed by Avalonia.Labs.Gif (see CacheImage).
    /// </summary>
    internal class GifHelper
    {
        internal static bool IsGif(string filePath)
        {
            // GIF signature: 47 49 46 38 39 (47 49 46 37 61 for GIF87a)
            byte[] gifSignature = Encoding.UTF8.GetBytes("GIF");
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[3];
                    fs.Read(buffer, 0, 3);
                    return buffer[0] == gifSignature[0] && buffer[1] == gifSignature[1] && (buffer[2] == gifSignature[2] || buffer[2] == '8' || buffer[2] == '7');
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
        internal static bool IsGif(MemoryStream ms)
        {
            byte[] gifSignature = Encoding.UTF8.GetBytes("GIF");
            try
            {
                byte[] buffer = new byte[3];
                ms.Read(buffer, 0, 3);
                return buffer[0] == gifSignature[0] && buffer[1] == gifSignature[1] && (buffer[2] == gifSignature[2] || buffer[2] == '8' || buffer[2] == '7');
            }
            catch (Exception)
            {
                return false;
            }
        }
        internal static void OptimizeGIF(string path, uint maxHeightWidth)
        {
            Tuple<int, int>? dimensions = GetGifDimensions(path);
            if (dimensions != null && (dimensions.Item1 > maxHeightWidth || dimensions.Item2 > maxHeightWidth))
            {
                using (MagickImageCollection collection = new MagickImageCollection(path))
                {
                    MagickGeometry size = new MagickGeometry(maxHeightWidth);
                    size.IgnoreAspectRatio = false;
                    // Coalesce the image
                    collection.Coalesce();
                    foreach (MagickImage image in collection)
                    {
                        image.Resize(size);
                        image.GifDisposeMethod = GifDisposeMethod.Background;
                    }
                    //collection.Optimize();
                    collection.Write(path);
                }
            }
        }
        private static Tuple<int, int>? GetGifDimensions(string filePath)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    // Skip to the start of the logical screen descriptor block
                    fs.Seek(6, SeekOrigin.Begin);

                    byte[] buffer = new byte[4];
                    fs.Read(buffer, 0, 4);

                    // Extract width and height from the buffer
                    int width = BitConverter.ToUInt16(buffer, 0);
                    int height = BitConverter.ToUInt16(buffer, 2);
                    return Tuple.Create(width, height);
                }
            }
            catch (Exception)
            {
                // Handle any exceptions that might occur during file operations
                return null;
            }
        }
    }
}
