using GameVault.Core.Downloads;

namespace GameVault.Core.Tests
{
    public class TimeLeftTests
    {
        [Fact]
        public void ASpeedNearZero_ShowsNoTime_InsteadOfOverflowing()
        {
            Assert.Equal("--:--:--", TimeLeft.Text(20_000_000_000, 1e-12));
            Assert.Equal("--:--:--", TimeLeft.Text(20_000_000_000, double.Epsilon));
            Assert.Equal("--:--:--", TimeLeft.Text(1000, 0));
            Assert.Equal("--:--:--", TimeLeft.Text(1000, double.NaN));
        }

        [Fact]
        public void ATime_CountsItsHours_PastADay()
        {
            Assert.Equal("00:01:40", TimeLeft.Text(100_000, 1000));
            Assert.Equal("26:00:00", TimeLeft.Text(26 * 3600, 1));
            Assert.Equal("00:00:00", TimeLeft.Text(-5, 1000));
        }
    }
}
