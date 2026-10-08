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
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (maxWidth is int width)
            {
                int sourceWidth = 0;
                try
                {
                    using var codec = SKCodec.Create(file);
                    sourceWidth = codec?.Info.Width ?? 0;
                }
                catch (Exception ex) { Log.Ignored(ex); }
                file.Position = 0;
                if (sourceWidth > width)
                    return Bitmap.DecodeToWidth(file, width, BitmapInterpolationMode.HighQuality);
            }
            return new Bitmap(file);
        }
    }
}
