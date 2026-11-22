using GameVault.Core.Couch;

namespace GameVault.Core.Tests
{
    public class QuietStartTests
    {
        /// <summary>A WebM as YouTube's: a segment and clusters of unknown size, 20 ms frames of the sizes given, a cluster every second.</summary>
        private static byte[] Webm(IEnumerable<int> sizes)
        {
            var bytes = new List<byte> { 0x1A, 0x45, 0xDF, 0xA3, 0x80 };
            bytes.AddRange(new byte[] { 0x18, 0x53, 0x80, 0x67, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
            // Info: TimecodeScale 1 000 000 (milliseconds)
            bytes.AddRange(new byte[] { 0x15, 0x49, 0xA9, 0x66, 0x87, 0x2A, 0xD7, 0xB1, 0x83, 0x0F, 0x42, 0x40 });
            int frame = 0;
            foreach (int size in sizes)
            {
                if (frame % 50 == 0)
                {
                    int cluster = frame * 20;
                    bytes.AddRange(new byte[] { 0x1F, 0x43, 0xB6, 0x75, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
                    bytes.AddRange(new byte[] { 0xE7, 0x82, (byte)(cluster >> 8), (byte)cluster });
                }
                int relative = frame % 50 * 20, length = 4 + size;
                bytes.AddRange(new byte[] { 0xA3, (byte)(0x40 | length >> 8), (byte)length, 0x81, (byte)(relative >> 8), (byte)relative, 0x80 });
                bytes.AddRange(new byte[size]);
                frame++;
            }
            return bytes.ToArray();
        }

        [Fact]
        public void ASilentBeginning_IsSkipped_ToALittleBeforeTheSound()
        {
            byte[] webm = Webm(Enumerable.Repeat(3, 150).Concat(Enumerable.Repeat(300, 400)));
            var frames = QuietStart.Frames(webm);
            Assert.Equal(550, frames.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(3000), frames[150].At);
            Assert.Equal(300, frames[150].Bytes);
            Assert.Equal(TimeSpan.FromMilliseconds(2700), QuietStart.Of(webm));
        }

        [Fact]
        public void ASoundHeardAtOnce_OrAShortSilence_OrAClick_StartsAtTheBeginning()
        {
            Assert.Null(QuietStart.Of(Webm(Enumerable.Repeat(300, 400))));
            // Half a second: the theme's own
            Assert.Null(QuietStart.Of(Webm(Enumerable.Repeat(3, 25).Concat(Enumerable.Repeat(300, 400)))));
            // A click in the silence is not the sound
            Assert.Equal(TimeSpan.FromMilliseconds(3700), QuietStart.Of(Webm(Enumerable.Repeat(3, 100).Append(400).Concat(Enumerable.Repeat(3, 99)).Concat(Enumerable.Repeat(300, 400)))));
            // Not a WebM, or too little of it
            Assert.Null(QuietStart.Of(new byte[] { 1, 2, 3 }));
            Assert.Null(QuietStart.Of(ReadOnlySpan<byte>.Empty));
        }
    }
}
