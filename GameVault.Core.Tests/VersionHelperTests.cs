using GameVault.Core;

namespace GameVault.Core.Tests
{
    public class VersionHelperTests
    {
        [Theory]
        [InlineData("1.10.0", "1.9.12")]   // the old "remove the dots" comparison got this wrong (1100 < 1912)
        [InlineData("1.17.2.0", "1.17.1.0")]
        [InlineData("v2.0.0", "1.99.99")]
        [InlineData("15.0.1", "15.0.0.0")]
        [InlineData("2.0", "1.9.9.9")]
        public void IsNewer_DetectsNewerVersions(string candidate, string current)
        {
            Assert.True(VersionHelper.IsNewer(candidate, current));
        }

        [Theory]
        [InlineData("1.9.12", "1.10.0")]
        [InlineData("1.17.2", "1.17.2.0")]
        [InlineData("1.17.2.0", "1.17.2")]
        [InlineData("garbage", "1.0.0")]
        [InlineData("1.0.0", "")]
        [InlineData(null, "1.0.0")]
        public void IsNewer_RejectsOlderEqualOrInvalid(string? candidate, string current)
        {
            Assert.False(VersionHelper.IsNewer(candidate, current));
        }

        [Fact]
        public void Parse_StripsPrefixAndSuffix()
        {
            Assert.Equal(new Version(15, 0, 0, 0), VersionHelper.Parse("v15.0.0-beta.2"));
            Assert.Equal(new Version(3, 0, 0, 0), VersionHelper.Parse("3"));
        }
    }
}
