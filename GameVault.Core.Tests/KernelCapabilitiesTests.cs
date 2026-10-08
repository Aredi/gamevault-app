using GameVault.Core.Input;

namespace GameVault.Core.Tests
{
    public class KernelCapabilitiesTests
    {
        [Theory]
        // Xbox pad (xpad): BTN_SOUTH and the other gamepad buttons, BTN_MODE
        [InlineData("7cdb000000000000 0 0 0 0", true)]
        // KVM emulated pointer (mouse buttons only) and a motherboard LED controller, both seen as js devices
        [InlineData("1f0000 0 0 0 0", false)]
        [InlineData("c000 0 0 40000001000000 1200000000 c00000000000000 100000800000000 40000010cc00 10168000000000 0", false)]
        [InlineData("", false)]
        [InlineData("0", false)]
        public void OnlyRealGamepads_Count(string bitmap, bool gamepad) => Assert.Equal(gamepad, KernelCapabilities.IsGamepad(bitmap));

        [Fact]
        public void Bits_AreCountedFromTheLastWord()
        {
            Assert.True(KernelCapabilities.HasBit("1 0", 64));
            Assert.True(KernelCapabilities.HasBit("0 8", 3));
            Assert.False(KernelCapabilities.HasBit("0 8", 64));
        }
    }
}
