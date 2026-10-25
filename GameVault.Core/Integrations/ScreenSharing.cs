using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GameVault.Core.Integrations
{
    /// <summary>
    /// A room member's screen shared with Sunshine, joined by the others with Moonlight. Their pairing asks for a 4-digit
    /// PIN typed on the host: both PCs work it out from the room's code (its members' only) and the guest's address in
    /// the room, so the host's client types it for its player.
    /// </summary>
    public static class ScreenSharing
    {
        /// <summary>Sunshine's app streaming the whole screen (in its list from the start).</summary>
        public const string DesktopApp = "Desktop";

        public static string Pin(string roomCode, string guestIp)
        {
            byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(roomCode), Encoding.UTF8.GetBytes(guestIp));
            return (BitConverter.ToUInt32(hash, 0) % 10000).ToString("D4");
        }

        /// <summary>The guest's address as Sunshine gives it ("::ffff:10.147.3.2" too), when it is a member's of the room.</summary>
        public static string? MemberAddress(string address, IEnumerable<string> memberIps)
        {
            string ip = IPAddress.TryParse(address, out IPAddress? parsed) && parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4().ToString() : address;
            return memberIps.Contains(ip) ? ip : null;
        }
    }
}
