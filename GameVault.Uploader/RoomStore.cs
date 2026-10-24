using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// LAN rooms: players on the same ZeroTier network play the PC games made for a LAN together. A room is one network
    /// of the controller; joining lets the player's PC in with an address of the room's subnet. Also keeps which games
    /// the administrators marked as playable in LAN. One JSON file, written whole.
    /// </summary>
    public sealed partial class RoomStore
    {
        public const int MaxMembers = 8;
        public const int MaxRoomsPerOwner = 3;
        private const string CodeLetters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        private readonly string file;
        /// <summary>Held across the controller's calls: a room and its network change together.</summary>
        private readonly SemaphoreSlim writing = new(1, 1);

        public RoomStore(UploaderOptions options, ILogger<RoomStore> logger)
        {
            Enabled = ProfileStore.IsWritable(options.RoomsDirectory, out string error);
            if (!Enabled)
                logger.LogWarning("LAN rooms are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", options.RoomsDirectory, error);
            file = Path.Combine(options.RoomsDirectory, "rooms.json");
        }

        public bool Enabled { get; }

        public sealed record Member(int UserId, string UserName, string NodeId, string Ip, DateTime JoinedAt);

        public sealed record Room(string Id, string Code, string Name, int OwnerId, string OwnerName, int? GameId, string? GameTitle, bool Public, int Subnet, List<Member> Members, DateTime CreatedAt);

        private sealed class State
        {
            public List<Room> Rooms { get; set; } = new();
            public List<int> LanGames { get; set; } = new();
        }

        [GeneratedRegex("^[0-9a-f]{10}$")]
        private static partial Regex NodeIdPattern();

        /// <summary>A ZeroTier address: 10 hexadecimal digits.</summary>
        public static bool IsValidNodeId(string? id) => id != null && NodeIdPattern().IsMatch(id);

        /// <summary>"k7q4mx", "K7Q-4MX": the same code.</summary>
        public static string NormalizedCode(string? code) => new string((code ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

        private State Read() => File.Exists(file) ? JsonSerializer.Deserialize<State>(File.ReadAllText(file)) ?? new State() : new State();

        private void Write(State state)
        {
            string part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(state));
            File.Move(part, file, overwrite: true);
        }

        private async Task<T> WithStateAsync<T>(Func<State, Task<T>> action)
        {
            await writing.WaitAsync();
            try { return await action(Read()); }
            finally { writing.Release(); }
        }

        /// <summary>The public rooms and the player's own.</summary>
        public Task<List<Room>> VisibleAsync(int userId) =>
            WithStateAsync(state => Task.FromResult(state.Rooms.Where(r => r.Public || r.Members.Any(m => m.UserId == userId)).OrderByDescending(r => r.Members.Any(m => m.UserId == userId)).ThenByDescending(r => r.CreatedAt).ToList()));

        public enum Outcome { Done, NotFound, Full, TooMany, NotOwner }

        public Task<(Outcome, Room?)> CreateAsync(ZeroTierController controller, int userId, string userName, string nodeId, string name, int? gameId, string? gameTitle, bool isPublic, CancellationToken ct) =>
            WithStateAsync<(Outcome, Room?)>(async state =>
            {
                if (state.Rooms.Count(r => r.OwnerId == userId) >= MaxRoomsPerOwner)
                    return (Outcome.TooMany, null);
                // Each room its own subnet: a PC in two rooms keeps both
                int subnet = Enumerable.Range(1, 254).Except(state.Rooms.Select(r => r.Subnet)).FirstOrDefault();
                if (subnet == 0)
                    return (Outcome.TooMany, null);
                string network = await controller.CreateNetworkAsync(name, subnet, ct);
                string ip = $"10.147.{subnet}.1";
                await controller.AuthorizeAsync(network, nodeId, ip, ct);
                string code;
                do code = new string(Enumerable.Range(0, 6).Select(_ => CodeLetters[RandomNumberGenerator.GetInt32(CodeLetters.Length)]).ToArray());
                while (state.Rooms.Any(r => r.Code == code));
                var room = new Room(network, code, name, userId, userName, gameId, gameTitle, isPublic, subnet, new List<Member> { new(userId, userName, nodeId, ip, DateTime.UtcNow) }, DateTime.UtcNow);
                state.Rooms.Add(room);
                Write(state);
                return (Outcome.Done, room);
            });

        /// <summary>Joins by the room's id (a public room, or one the player is in) or by its code (any room).</summary>
        public Task<(Outcome, Room?)> JoinAsync(ZeroTierController controller, int userId, string userName, string nodeId, string? roomId, string? code, CancellationToken ct) =>
            WithStateAsync<(Outcome, Room?)>(async state =>
            {
                string wanted = NormalizedCode(code);
                Room? room = state.Rooms.FirstOrDefault(r => wanted.Length > 0 ? r.Code == wanted : r.Id == roomId && (r.Public || r.Members.Any(m => m.UserId == userId)));
                if (room == null)
                    return (Outcome.NotFound, null);
                Member? before = room.Members.FirstOrDefault(m => m.UserId == userId);
                if (before?.NodeId == nodeId)
                    return (Outcome.Done, room);
                if (before == null && room.Members.Count >= MaxMembers)
                    return (Outcome.Full, null);
                // From another PC: the player's address goes to the new one
                if (before != null)
                    await controller.RemoveAsync(room.Id, before.NodeId, ct);
                string ip = before?.Ip ?? Enumerable.Range(1, 254).Select(n => $"10.147.{room.Subnet}.{n}").Except(room.Members.Select(m => m.Ip)).First();
                await controller.AuthorizeAsync(room.Id, nodeId, ip, ct);
                room.Members.RemoveAll(m => m.UserId == userId);
                room.Members.Add(new Member(userId, userName, nodeId, ip, DateTime.UtcNow));
                Write(state);
                return (Outcome.Done, room);
            });

        /// <summary>The player leaves; the room passes to the next member, and goes with its network when nobody is left.</summary>
        // shortcut: a player who never leaves keeps their place, fine for rooms among friends; forget idle members when rooms fill up
        public Task<Outcome> LeaveAsync(ZeroTierController controller, int userId, string roomId, CancellationToken ct) =>
            WithStateAsync(async state =>
            {
                Room? room = state.Rooms.FirstOrDefault(r => r.Id == roomId);
                Member? member = room?.Members.FirstOrDefault(m => m.UserId == userId);
                if (room == null || member == null)
                    return Outcome.NotFound;
                room.Members.Remove(member);
                if (room.Members.Count == 0)
                {
                    await controller.DeleteNetworkAsync(room.Id, ct);
                    state.Rooms.Remove(room);
                }
                else
                {
                    await controller.RemoveAsync(room.Id, member.NodeId, ct);
                    if (room.OwnerId == userId)
                        state.Rooms[state.Rooms.IndexOf(room)] = room with { OwnerId = room.Members[0].UserId, OwnerName = room.Members[0].UserName };
                }
                Write(state);
                return Outcome.Done;
            });

        public Task<Outcome> SetGameAsync(int userId, string roomId, int? gameId, string? gameTitle) =>
            WithStateAsync(state =>
            {
                int index = state.Rooms.FindIndex(r => r.Id == roomId);
                if (index < 0)
                    return Task.FromResult(Outcome.NotFound);
                if (state.Rooms[index].OwnerId != userId)
                    return Task.FromResult(Outcome.NotOwner);
                state.Rooms[index] = state.Rooms[index] with { GameId = gameId, GameTitle = gameTitle };
                Write(state);
                return Task.FromResult(Outcome.Done);
            });

        public Task<List<int>> LanGamesAsync() => WithStateAsync(state => Task.FromResult(state.LanGames.ToList()));

        public Task SetLanGameAsync(int gameId, bool lan) =>
            WithStateAsync(state =>
            {
                state.LanGames.Remove(gameId);
                if (lan)
                    state.LanGames.Add(gameId);
                Write(state);
                return Task.FromResult(true);
            });
    }
}
