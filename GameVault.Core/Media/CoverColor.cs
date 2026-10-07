namespace GameVault.Core.Media
{
    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        /// <summary>Relative luminance (WCAG), 0 = black, 1 = white.</summary>
        public double Luminance
        {
            get
            {
                static double Channel(byte c)
                {
                    double v = c / 255.0;
                    return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
            }
        }

        /// <summary>WCAG contrast ratio with white text.</summary>
        public double ContrastWithWhite => 1.05 / (Luminance + 0.05);
    }

    /// <summary>
    /// The color of a game: the most present vivid hue of its cover. The library tints buttons, glows and
    /// backgrounds with it, so each game page takes the look of its own artwork.
    /// </summary>
    public static class CoverColor
    {
        private const int HueBuckets = 24;

        /// <summary>
        /// The dominant vivid color of an image given as BGRA pixels (a small thumbnail is enough), or null when the
        /// image has no real color (black and white covers, placeholders).
        /// </summary>
        public static Rgb? Dominant(ReadOnlySpan<byte> bgra)
        {
            var score = new double[HueBuckets];
            var red = new double[HueBuckets];
            var green = new double[HueBuckets];
            var blue = new double[HueBuckets];
            double total = 0;
            int pixels = 0;
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
                if (a < 128)
                    continue;
                pixels++;
                ToHsv(r, g, b, out double hue, out double saturation, out double value);
                // Near black and washed out pixels say nothing about the artwork's color
                if (value < 0.15 || saturation < 0.18)
                    continue;
                double weight = saturation * saturation * Math.Min(1, value * 1.4);
                int bucket = (int)(hue / 360 * HueBuckets) % HueBuckets;
                score[bucket] += weight;
                red[bucket] += r * weight;
                green[bucket] += g * weight;
                blue[bucket] += b * weight;
                total += weight;
            }
            // Less than a few percent of colored pixels: a grey cover
            if (pixels == 0 || total < pixels * 0.02)
                return null;

            // Neighbouring buckets count too, so a hue split across a bucket edge is not lost
            int best = 0;
            double bestScore = -1;
            for (int i = 0; i < HueBuckets; i++)
            {
                double s = score[i] + 0.5 * (score[(i + 1) % HueBuckets] + score[(i + HueBuckets - 1) % HueBuckets]);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = i;
                }
            }
            double sum = score[best];
            return new Rgb((byte)Math.Round(red[best] / sum), (byte)Math.Round(green[best] / sum), (byte)Math.Round(blue[best] / sum));
        }

        /// <summary>
        /// The same hue made usable as a button background: saturated enough to read as the game's color, dark enough
        /// for white text on it.
        /// </summary>
        public static Rgb Accent(Rgb color)
        {
            ToHsl(color.R, color.G, color.B, out double hue, out double saturation, out double lightness);
            saturation = Math.Clamp(saturation, 0.45, 0.85);
            lightness = Math.Clamp(lightness, 0.36, 0.55);
            Rgb accent = FromHsl(hue, saturation, lightness);
            // Yellows and greens are light at any lightness: darken until white text stays readable
            while (accent.ContrastWithWhite < 3.2 && lightness > 0.2)
            {
                lightness -= 0.02;
                accent = FromHsl(hue, saturation, lightness);
            }
            return accent;
        }

        public static void ToHsv(byte r, byte g, byte b, out double hue, out double saturation, out double value)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
            double delta = max - min;
            value = max;
            saturation = max == 0 ? 0 : delta / max;
            hue = Hue(rf, gf, bf, max, delta);
        }

        public static void ToHsl(byte r, byte g, byte b, out double hue, out double saturation, out double lightness)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
            double delta = max - min;
            lightness = (max + min) / 2;
            saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
            hue = Hue(rf, gf, bf, max, delta);
        }

        public static Rgb FromHsl(double hue, double saturation, double lightness)
        {
            double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
            double m = lightness - c / 2;
            (double r, double g, double b) = (hue % 360) switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x),
            };
            static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
            return new Rgb(Byte(r + m), Byte(g + m), Byte(b + m));
        }

        private static double Hue(double r, double g, double b, double max, double delta)
        {
            if (delta == 0)
                return 0;
            double hue = max == r ? 60 * ((g - b) / delta % 6)
                : max == g ? 60 * ((b - r) / delta + 2)
                : 60 * ((r - g) / delta + 4);
            return hue < 0 ? hue + 360 : hue;
        }
    }
}
