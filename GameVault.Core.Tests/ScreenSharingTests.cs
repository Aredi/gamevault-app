using GameVault.Core.Integrations;

namespace GameVault.Core.Tests
{
    public class ScreenSharingTests
    {
        [Fact]
        public void TheHostAndTheGuest_FindTheSamePin_ForThisRoomOnly()
        {
            string pin = ScreenSharing.Pin("K7Q4MX");
            Assert.Matches("^[0-9]{4}$", pin);
            Assert.Equal(pin, ScreenSharing.Pin("K7Q4MX"));
            // Another room: another PIN (these ones differ)
            Assert.NotEqual(pin, ScreenSharing.Pin("AB2CD3"));
        }

        [Fact]
        public void APairing_IsTheRooms_FromAMembersAddressOnly()
        {
            var members = new[] { "10.147.3.1", "10.147.3.2" };
            Assert.Equal("10.147.3.2", ScreenSharing.MemberAddress("10.147.3.2", members));
            Assert.Equal("10.147.3.2", ScreenSharing.MemberAddress("::ffff:10.147.3.2", members));
            Assert.Null(ScreenSharing.MemberAddress("192.168.1.20", members));
            Assert.Null(ScreenSharing.MemberAddress("", members));
        }
    }
}
