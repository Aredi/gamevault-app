using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    public sealed class RoomMember
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }
        [JsonPropertyName("userName")]
        public string UserName { get; set; } = "";
        [JsonPropertyName("nodeId")]
        public string NodeId { get; set; } = "";
        [JsonPropertyName("ip")]
        public string Ip { get; set; } = "";
    }

    /// <summary>A LAN room as the SanctuaryVault service gives it: its ZeroTier network is its id.</summary>
    public sealed class RoomItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
        [JsonPropertyName("code")]
        public string? Code { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("ownerId")]
        public int OwnerId { get; set; }
        [JsonPropertyName("ownerName")]
        public string OwnerName { get; set; } = "";
        [JsonPropertyName("gameId")]
        public int? GameId { get; set; }
        [JsonPropertyName("gameTitle")]
        public string? GameTitle { get; set; }
        [JsonPropertyName("public")]
        public bool Public { get; set; }
        [JsonPropertyName("maxMembers")]
        public int MaxMembers { get; set; }
        [JsonPropertyName("member")]
        public bool Member { get; set; }
        [JsonPropertyName("members")]
        public List<RoomMember> Members { get; set; } = new();

        /// <summary>"K7Q-4MX": read aloud more easily.</summary>
        public string ShownCode => Code is { Length: 6 } c ? $"{c[..3]}-{c[3..]}" : Code ?? "";
        public string Seats => $"{Members.Count} / {MaxMembers}";
        public string Subtitle => Loc.F("Made by {0}", OwnerName) + " · " + (GameTitle ?? Loc.T("no game chosen"));
    }

    /// <summary>The LAN rooms, through the SanctuaryVault service (which lets the PCs in their ZeroTier networks).</summary>
    internal static class RoomsService
    {
        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>The games marked playable in LAN, read once a while for the game pages.</summary>
        private static (HashSet<int> Games, DateTime At)? lanGames;

        private static async Task<string> SendAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, $"{Service}/{path}")
            {
                Content = body == null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            string text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                throw new NotSupportedException(Loc.T("LAN rooms are not available on this server."));
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ErrorOf(text) ?? Loc.T("The SanctuaryVault service could not be reached."));
            return text;
        }

        public static async Task<List<RoomItem>> ListAsync() => JsonSerializer.Deserialize<List<RoomItem>>(await SendAsync(HttpMethod.Get, "rooms")) ?? new();

        public static async Task<RoomItem> CreateAsync(string name, int? gameId, string? gameTitle, bool isPublic, string nodeId) =>
            JsonSerializer.Deserialize<RoomItem>(await SendAsync(HttpMethod.Post, "rooms", new { name, gameId, gameTitle, @public = isPublic, nodeId }))!;

        public static async Task<RoomItem> JoinAsync(string? roomId, string? code, string nodeId) =>
            JsonSerializer.Deserialize<RoomItem>(await SendAsync(HttpMethod.Post, "rooms/join", new { roomId, code, nodeId }))!;

        public static Task LeaveAsync(string roomId) => SendAsync(HttpMethod.Post, $"rooms/{roomId}/leave");

        public static Task SetGameAsync(string roomId, int? gameId, string? gameTitle) => SendAsync(HttpMethod.Put, $"rooms/{roomId}/game", new { gameId, gameTitle });

        public static async Task<HashSet<int>> LanGamesAsync()
        {
            if (lanGames is { } known && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(5))
                return known.Games;
            var games = (JsonSerializer.Deserialize<List<int>>(await SendAsync(HttpMethod.Get, "lan-games")) ?? new()).ToHashSet();
            lanGames = (games, DateTime.UtcNow);
            return games;
        }

        public static async Task SetLanGameAsync(int gameId, bool lan)
        {
            await SendAsync(HttpMethod.Put, $"lan-games/{gameId}", new { lan });
            lanGames = null;
        }

        private static string? ErrorOf(string body)
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(body);
                return json.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() : null;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// Into a room for good: this PC's ZeroTier address goes to the service, which lets it in, then the PC joins the
        /// network and (Windows) puts it first and private once it is up.
        /// </summary>
        public static async Task<RoomItem> EnterAsync(Func<string, Task<RoomItem>> ask, Action<string> status)
        {
            var (address, _) = await ZeroTierAgent.StatusAsync();
            RoomItem room = await ask(address);
            status(Loc.T("Joining the room's network..."));
            await ZeroTierAgent.JoinAsync(room.Id);
            for (int i = 0; i < 30 && (await ZeroTierAgent.NetworksAsync()).GetValueOrDefault(room.Id) != "OK"; i++)
                await Task.Delay(1000);
            if ((await ZeroTierAgent.NetworksAsync()).GetValueOrDefault(room.Id) != "OK")
                throw new InvalidOperationException(Loc.T("The room's network did not answer: check that ZeroTier is running, then try again."));
            if (Platform.PlatformInfo.IsWindows)
            {
                status(Loc.T("Giving the room's network the priority: accept Windows' prompt..."));
                await ZeroTierAgent.PrioritizeAsync();
            }
            return room;
        }
    }
}
