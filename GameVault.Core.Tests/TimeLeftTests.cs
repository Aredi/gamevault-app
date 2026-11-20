using GameVault.Core.Downloads;

namespace GameVault.Core.Tests
{
    public class TimeLeftTests
    {
        [Fact]
        public void ASpeedNearZero_ShowsNoTime_InsteadOfOverflowing()
        {
            Assert.Null(TimeLeft.Of(20_000_000_000, 1e-12));
            Assert.Null(TimeLeft.Of(20_000_000_000, double.Epsilon));
            Assert.Null(TimeLeft.Of(1000, 0));
            Assert.Null(TimeLeft.Of(1000, double.NaN));
        }

        [Fact]
        public void ATime_CountsItsHours_PastADay()
        {
            Assert.Equal(TimeSpan.FromSeconds(100), TimeLeft.Of(100_000, 1000));
            Assert.Equal(TimeSpan.FromHours(26), TimeLeft.Of(26 * 3600, 1));
            Assert.Equal(TimeSpan.Zero, TimeLeft.Of(-5, 1000));
        }
    }
}
