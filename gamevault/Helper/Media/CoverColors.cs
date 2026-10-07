using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GameVault.Core;
using GameVault.Core.Media;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace gamevault.Helper
{
    /// <summary>The accent color of each cover image, computed once from a thumbnail and kept for the session.</summary>
    internal static class CoverColors
    {
        private static readonly ConcurrentDictionary<string, Color?> cache = new();

        /// <param name="key">Identifies the image (its cache file), so each one is only analysed once.</param>
        public static Color? Get(Bitmap bitmap, string key)
        {
            if (cache.TryGetValue(key, out Color? known))
                return known;
            Color? result = null;
            try
            {
                const int width = 24, height = 36;
                using Bitmap small = bitmap.CreateScaledBitmap(new PixelSize(width, height), BitmapInterpolationMode.LowQuality);
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                IntPtr buffer = Marshal.AllocHGlobal(pixels.Length);
                try
                {
                    small.CopyPixels(new PixelRect(0, 0, width, height), buffer, pixels.Length, stride);
                    Marshal.Copy(buffer, pixels, 0, pixels.Length);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
                if (small.Format == PixelFormat.Rgba8888)
                {
                    for (int i = 0; i + 3 < pixels.Length; i += 4)
                        (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                }
                Rgb? dominant = CoverColor.Dominant(pixels);
                if (dominant != null)
                {
                    Rgb accent = CoverColor.Accent(dominant.Value);
                    result = Color.FromRgb(accent.R, accent.G, accent.B);
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            cache[key] = result;
            return result;
        }

        /// <summary>The color of the current theme, used for games without a colored cover.</summary>
        public static Color ThemeAccent =>
            Application.Current?.TryGetResource("GameVault.Colors.Accent", null, out object? value) == true && value is Color color
                ? color
                : Color.Parse("#4F46AF");
    }
}
