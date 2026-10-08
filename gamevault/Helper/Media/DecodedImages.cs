using Avalonia.Media.Imaging;
using GameVault.Core;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace gamevault.Helper
{
    /// <summary>
    /// Images decoded at the size they are shown at (a 1200 px wide cover shown 170 px wide is decoded at 480 px)
    /// and kept in memory while they fit a budget, so scrolling back through the library does not read them again.
    /// </summary>
    internal static class DecodedImages
    {
        private const long BudgetBytes = 160L * 1024 * 1024;
        private static readonly object gate = new();
        private static readonly LinkedList<(string Key, Bitmap Bitmap, long Bytes)> order = new();
        private static readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Bitmap, long Bytes)>> byKey = new();
        private static long usedBytes;

        private static string Key(string path, int? maxWidth)
        {
            try { return $"{path}|{maxWidth}|{File.GetLastWriteTimeUtc(path).Ticks}"; }
            catch { return $"{path}|{maxWidth}"; }
        }

        /// <summary>The image if it is in memory already (scrolling back), without reading the file.</summary>
        public static Bitmap? TryGet(string path, int? maxWidth)
        {
            string key = Key(path, maxWidth);
            lock (gate)
            {
                if (!byKey.TryGetValue(key, out var node))
                    return null;
                order.Remove(node);
                order.AddFirst(node);
                return node.Value.Bitmap;
            }
        }

        /// <param name="maxWidth">Decoded at most this wide (never enlarged); null: the image as it is.</param>
        public static Bitmap Load(string path, int? maxWidth)
        {
            string key = Key(path, maxWidth);
            lock (gate)
            {
                if (byKey.TryGetValue(key, out var node))
                {
                    order.Remove(node);
                    order.AddFirst(node);
                    return node.Value.Bitmap;
                }
            }
            Bitmap bitmap = Decode(path, maxWidth);
            long bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
            lock (gate)
            {
                if (byKey.TryGetValue(key, out var existing))
                    return existing.Value.Bitmap;
                byKey[key] = order.AddFirst((key, bitmap, bytes));
                usedBytes += bytes;
                // The bitmaps dropped here are freed once no image shows them any more
                while (usedBytes > BudgetBytes && order.Last != null && order.Count > 1)
                {
                    var last = order.Last.Value;
                    order.RemoveLast();
                    byKey.Remove(last.Key);
                    usedBytes -= last.Bytes;
                }
            }
            return bitmap;
        }

        private static Bitmap Decode(string path, int? maxWidth)
        {
            // Read once: SKCodec closes the stream it is given, so it gets its own copy of the bytes
            byte[] data = File.ReadAllBytes(path);
            if (maxWidth is int width)
            {
                int sourceWidth = 0;
                try
                {
                    using var skData = SKData.CreateCopy(data);
                    using var codec = SKCodec.Create(skData);
                    sourceWidth = codec?.Info.Width ?? 0;
                }
                catch (Exception ex) { Log.Ignored(ex); }
                if (sourceWidth > width)
                {
                    // Not Bitmap.DecodeToWidth: it does not keep the proportions (a 1200 x 1800 cover came out 480 x 480)
                    using var full = new MemoryStream(data, writable: false);
                    using var original = new Bitmap(full);
                    int height = Math.Max(1, (int)Math.Round(original.PixelSize.Height * (double)width / original.PixelSize.Width));
                    return original.CreateScaledBitmap(new Avalonia.PixelSize(width, height), BitmapInterpolationMode.HighQuality);
                }
            }
            using var stream = new MemoryStream(data, writable: false);
            return new Bitmap(stream);
        }
    }
}
