using GameVault.Core.Media;

namespace GameVault.Core.Tests
{
    public class CoverColorTests
    {
        /// <summary>BGRA pixels: <paramref name="count"/> times the given color.</summary>
        private static IEnumerable<byte> Pixels(byte r, byte g, byte b, int count, byte a = 255) =>
            Enumerable.Range(0, count).SelectMany(_ => new[] { b, g, r, a });

        private static double HueOf(Rgb color)
        {
            CoverColor.ToHsv(color.R, color.G, color.B, out double hue, out _, out _);
            return hue;
        }

        [Fact]
        public void TheMostPresentVividHue_Wins()
        {
            // Mostly dark artwork, a large orange sky and a small blue logo
            byte[] image = Pixels(10, 10, 14, 500).Concat(Pixels(230, 120, 40, 300)).Concat(Pixels(40, 90, 230, 60)).ToArray();
            Rgb? color = CoverColor.Dominant(image);
            Assert.NotNull(color);
            Assert.InRange(HueOf(color.Value), 20, 35);
        }

        [Fact]
        public void GreyCovers_AndTransparentPixels_HaveNoColor()
        {
            byte[] grey = Pixels(20, 20, 20, 200).Concat(Pixels(128, 128, 128, 200)).Concat(Pixels(250, 250, 250, 200)).ToArray();
            Assert.Null(CoverColor.Dominant(grey));
            Assert.Null(CoverColor.Dominant(Pixels(255, 0, 0, 100, a: 0).ToArray()));
            Assert.Null(CoverColor.Dominant(Array.Empty<byte>()));
        }

        [Theory]
        [InlineData(255, 230, 40)]   // yellow
        [InlineData(120, 255, 120)]  // light green
        [InlineData(20, 10, 60)]     // very dark blue
        [InlineData(255, 200, 220)]  // pale pink
        public void Accent_KeepsTheHue_AndWhiteTextReadable(byte r, byte g, byte b)
        {
            var source = new Rgb(r, g, b);
            Rgb accent = CoverColor.Accent(source);
            Assert.True(accent.ContrastWithWhite >= 3.2, $"contrast {accent.ContrastWithWhite:0.00} for {accent}");
            double difference = Math.Abs(HueOf(accent) - HueOf(source));
            Assert.True(Math.Min(difference, 360 - difference) < 8, $"hue moved by {difference}");
            // Still a color, not grey or black
            CoverColor.ToHsl(accent.R, accent.G, accent.B, out _, out double saturation, out double lightness);
            Assert.True(saturation >= 0.4 && lightness >= 0.18, accent.ToString());
        }

        [Fact]
        public void Hsl_RoundTrips()
        {
            var color = new Rgb(79, 70, 175);
            CoverColor.ToHsl(color.R, color.G, color.B, out double h, out double s, out double l);
            Assert.Equal(color, CoverColor.FromHsl(h, s, l));
        }
    }
}
