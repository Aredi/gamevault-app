using System;
using System.IO;
using System.Text;


namespace gamevault.Helper
{
    /// <summary>
    /// GIF detection. Animated GIFs are displayed by Avalonia.Labs.Gif (see CacheImage).
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
                    if (fs.ReadAtLeast(buffer, 3, throwOnEndOfStream: false) < 3)
                        return false;
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
                if (ms.ReadAtLeast(buffer, 3, throwOnEndOfStream: false) < 3)
                    return false;
                return buffer[0] == gifSignature[0] && buffer[1] == gifSignature[1] && (buffer[2] == gifSignature[2] || buffer[2] == '8' || buffer[2] == '7');
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
