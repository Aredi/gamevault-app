using Avalonia.Media.Imaging;
using GameVault.Core;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace gamevault.Helper
{
    /// <summary>
    /// Images decoded at the size they are shown at (a 1200 px wide cover shown at most 260 px wide is decoded at 260 px on a 100 % screen)
    /// and kept in memory while they fit a budget, so scrolling back through the library does not read them again.
    /// </summary>
    internal static class DecodedImages
    {
        private const long BudgetBytes = 96L * 1024 * 1024;
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
        public static Bitmap? TryGet(string path, int? maxWidth) => TryGetKey(Key(path, maxWidth));

        /// <summary>An image downloaded from an address (screenshots), if it is in memory already.</summary>
        public static Bitmap? TryGetUrl(string url, int? maxWidth) => TryGetKey($"{url}|{maxWidth}");

        /// <summary>Decodes a downloaded image at the size it is shown at and keeps it.</summary>
        public static Bitmap LoadUrl(string url, byte[] data, int? maxWidth) => Store($"{url}|{maxWidth}", Decode(data, maxWidth));

        private static Bitmap? TryGetKey(string key)
        {
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
            return Store(key, Decode(File.ReadAllBytes(path), maxWidth));
        }

        private static Bitmap Store(string key, Bitmap bitmap)
        {
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

        private static Bitmap Decode(byte[] data, int? maxWidth)
        {
            if (maxWidth is int width)
            {
                try
                {
                    if (Scaled(data, width) is Bitmap scaled)
                        return scaled;
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            using var stream = new MemoryStream(data, writable: false);
            return new Bitmap(stream);
        }

        /// <summary>
        /// Wider than <paramref name="width"/>: decoded at that width. A JPEG decodes straight at 1/2, 1/4 or 1/8 of
        /// its size (never smaller than asked), so the full image is never in memory; only the rest is scaled.
        /// Not Bitmap.DecodeToWidth: it does not keep the proportions (a 1200 x 1800 cover came out 480 x 480).
        /// </summary>
        private static Bitmap? Scaled(byte[] data, int width)
        {
            // SKCodec closes the stream it is given: it reads its own copy of the bytes
            using var skData = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(skData);
            if (codec == null || codec.Info.Width <= width)
                return null;
            SKSizeI sampled = codec.GetScaledDimensions((float)width / codec.Info.Width);
            var info = new SKImageInfo(sampled.Width, sampled.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var decoded = new SKBitmap(info);
            if (codec.GetPixels(info, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                return null;
            int height = Math.Max(1, (int)Math.Round(codec.Info.Height * (double)width / codec.Info.Width));
            using var scaled = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            decoded.ScalePixels(scaled, SKFilterQuality.High);
            return new Bitmap(Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul, scaled.GetPixels(), new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), scaled.RowBytes);
        }
    }
}
