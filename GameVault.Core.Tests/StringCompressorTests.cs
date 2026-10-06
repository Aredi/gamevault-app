namespace GameVault.Core.Tests
{
    public class StringCompressorTests
    {
        [Fact]
        public void BigTexts_RoundTrip()
        {
            // A game with a long description and many media entries: several hundred KB of JSON
            var random = new Random(42);
            string text = string.Concat(Enumerable.Range(0, 20000).Select(i => $"{{\"id\":{i},\"url\":\"https://example.com/{random.Next()}\",\"title\":\"Élément {i}\"}},"));
            Assert.True(text.Length > 500_000);
            Assert.Equal(text, StringCompressor.Decompress(StringCompressor.Compress(text)));
        }

        [Fact]
        public void TheFormat_IsTheOneOfExistingCaches()
        {
            // Written by the previous implementation (length prefix + gzip, base64)
            string old = Convert.ToBase64String(BitConverter.GetBytes(5).Concat(Gzip("hello")).ToArray());
            Assert.Equal("hello", StringCompressor.Decompress(old));
        }

        private static byte[] Gzip(string text)
        {
            using var output = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionMode.Compress, true))
                gzip.Write(System.Text.Encoding.UTF8.GetBytes(text));
            return output.ToArray();
        }
    }
}
