using GameVault.Core.Input;

namespace GameVault.Core.Tests
{
    public class GamepadMappingTests
    {
        private static readonly DateTime T0 = new(2026, 10, 8, 20, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void A_Button_GivesOneConfirm_PerPress()
        {
            var pad = new GamepadMapping();
            Assert.Equal(new[] { PadAction.Confirm }, pad.Update(new PadState { A = true }, T0));
            Assert.Empty(pad.Update(new PadState { A = true }, T0.AddSeconds(1)));
            Assert.Empty(pad.Update(PadState.Released, T0.AddSeconds(2)));
            Assert.Equal(new[] { PadAction.Confirm }, pad.Update(new PadState { A = true }, T0.AddSeconds(3)));
        }

        [Fact]
        public void HeldDirection_Repeats_AfterAFirstDelay()
        {
            var pad = new GamepadMapping();
            var right = new PadState { Right = true };
            Assert.Equal(new[] { PadAction.Right }, pad.Update(right, T0));
            Assert.Empty(pad.Update(right, T0.AddMilliseconds(300)));
            Assert.Equal(new[] { PadAction.Right }, pad.Update(right, T0.AddMilliseconds(400)));
            Assert.Empty(pad.Update(right, T0.AddMilliseconds(450)));
            Assert.Equal(new[] { PadAction.Right }, pad.Update(right, T0.AddMilliseconds(520)));
            // Another direction starts at once
            Assert.Equal(new[] { PadAction.Down }, pad.Update(new PadState { Down = true }, T0.AddMilliseconds(530)));
        }

        [Theory]
        [InlineData(0.2, 0.3, null)]
        [InlineData(-0.9, 0.2, PadAction.Left)]
        [InlineData(0.3, -0.8, PadAction.Up)]
        [InlineData(0.1, 0.7, PadAction.Down)]
        public void Stick_HasADeadZone_AndItsLargerAxisWins(double x, double y, PadAction? expected) =>
            Assert.Equal(expected, GamepadMapping.Direction(new PadState { StickX = x, StickY = y }));
    }
}
