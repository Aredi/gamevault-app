using GameVault.Core.Downloads;

namespace GameVault.Core.Tests
{
    public class DownloadPartsTests
    {
        private const long MB = 1024 * 1024;

        [Fact]
        public void Plan_CoversTheFileWithoutGapsOrOverlaps()
        {
            var parts = DownloadParts.Plan(1000 * MB + 3, 4);
            Assert.Equal(4, parts.Count);
            Assert.Equal(0, parts[0].Start);
            for (int i = 1; i < parts.Count; i++)
                Assert.Equal(parts[i - 1].End + 1, parts[i].Start);
            Assert.Equal(1000 * MB + 2, parts[^1].End);
            Assert.Equal(1000 * MB + 3, parts.Sum(p => p.Length));
        }

        [Fact]
        public void Plan_SmallFilesUseFewerConnections()
        {
            Assert.Single(DownloadParts.Plan(10 * MB, 4));
            Assert.Equal(2, DownloadParts.Plan(70 * MB, 8).Count);
            Assert.Single(DownloadParts.Plan(5000 * MB, 1));
        }

        [Fact]
        public void Checkpoint_RoundTrips_AndRejectsBrokenOnes()
        {
            var parts = DownloadParts.Plan(200 * MB, 4);
            parts[0].Position = parts[0].Start + 123;
            parts[2].Position = parts[2].End + 1;
            string text = DownloadParts.Serialize(200 * MB, parts);

            var parsed = DownloadParts.Parse(text, out long total);
            Assert.Equal(200 * MB, total);
            Assert.Equal(parts.Select(p => (p.Start, p.End, p.Position)), parsed!.Select(p => (p.Start, p.End, p.Position)));
            Assert.True(parsed[2].IsComplete);
            Assert.Equal(123 + parts[2].Length, DownloadParts.Downloaded(parsed));

            Assert.Null(DownloadParts.Parse("123;456", out _));// the single connection format
            Assert.Null(DownloadParts.Parse("100|0-49@0,60-99@60", out _));// gap
            Assert.Null(DownloadParts.Parse("100|0-49@0", out _));// does not reach the end
            Assert.Null(DownloadParts.Parse("", out _));
        }

        [Fact]
        public void RangeHeader_ContinuesAtThePosition()
        {
            var part = new DownloadPart(100, 199, 150);
            Assert.Equal("bytes=150-199", part.RangeHeader);
            Assert.Equal(50, part.Downloaded);
            Assert.False(part.IsComplete);
        }
    }
}

namespace GameVault.Core.Tests
{
    public class ArchiveErrorsTests
    {
        [Theory]
        [InlineData("ERROR: CRC Failed : data.bin")]
        [InlineData("ERROR: Data Error : game/level1.pak")]
        [InlineData("ERROR: Unexpected end of archive")]
        [InlineData("gzip: stdin: unexpected end of file\ntar: Unexpected EOF in archive")]
        [InlineData("gzip: stdin: invalid compressed data--crc error")]
        public void DamagedArchives_AreRecognized(string output) => Assert.True(ArchiveErrors.IsDamaged(output));

        [Theory]
        [InlineData("ERROR: Wrong password : data.bin")]
        [InlineData("ERROR: No space left on device : data.bin\nERROR: Data Error")]
        [InlineData("")]
        [InlineData(null)]
        public void OtherFailures_AreNot(string? output) => Assert.False(ArchiveErrors.IsDamaged(output));
    }
}
