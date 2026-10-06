using System.IO.Compression;
using System.Text;

namespace GameVault.Core
{
    /// <summary>
    /// Compressed text for the offline cache: base64 of [4 bytes length][gzip]. Same format as before, existing caches stay readable.
    /// </summary>
    public static class StringCompressor
    {
        public static string Compress(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            using var compressed = new MemoryStream();
            compressed.Write(BitConverter.GetBytes(data.Length));
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
                gzip.Write(data);
            return Convert.ToBase64String(compressed.ToArray());
        }

        public static string Decompress(string compressedText)
        {
            byte[] packed = Convert.FromBase64String(compressedText);
            int length = BitConverter.ToInt32(packed, 0);
            byte[] data = new byte[length];
            using var gzip = new GZipStream(new MemoryStream(packed, 4, packed.Length - 4), CompressionMode.Decompress);
            // GZipStream returns what is ready, not always all that was asked for: read until the end
            int read = gzip.ReadAtLeast(data, data.Length, throwOnEndOfStream: false);
            return Encoding.UTF8.GetString(data, 0, read);
        }
    }
}
