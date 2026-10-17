using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// "Install on my PC", asked from the web page (a phone...). Each SanctuaryVault client of a player says every
    /// minute that it is open, so the page lets the player choose the PC. The client takes the games asked for it and
    /// says what became of them (started, or why not): a request stays on the page with its outcome.
    /// One JSON file per player, written whole.
    /// </summary>
    public sealed partial class RemoteInstallStore
    {
        public const int MaxPerUser = 20;
        /// <summary>A PC that said nothing for longer is shown as closed.</summary>
        public static readonly TimeSpan OnlineFor = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan ForgetDevicesAfter = TimeSpan.FromDays(30);
        private static readonly TimeSpan KeepOutcomesFor = TimeSpan.FromDays(3);

        public const string Pending = "pending", Started = "started", Failed = "failed";

        private readonly string directory;
        private readonly object writing = new();

        public RemoteInstallStore(UploaderOptions options, ILogger<RemoteInstallStore> logger)
        {
            directory = options.RemoteInstallsDirectory;
            Enabled = ProfileStore.IsWritable(directory, out string error);
            if (!Enabled)
                logger.LogWarning("Remote installs are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", directory, error);
        }

        public bool Enabled { get; }

        public sealed record Device(string Id, string Name, DateTime LastSeen)
        {
            public bool Online => DateTime.UtcNow - LastSeen < OnlineFor;
        }

        public sealed record Entry(string Id, int GameId, string Title, string DeviceId, string DeviceName, string Status, string? Message, DateTime RequestedAt, DateTime UpdatedAt);

        private sealed class State
        {
            public List<Device> Devices { get; set; } = new();
            public List<Entry> Installs { get; set; } = new();
        }

        [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
        private static partial Regex DeviceIdPattern();

        public static bool IsValidDeviceId(string? id) => id != null && DeviceIdPattern().IsMatch(id);

        private string FileOf(int userId) => Path.Combine(directory, $"{userId}.json");

        private State Read(int userId)
        {
            string file = FileOf(userId);
            if (!Enabled || !File.Exists(file))
                return new State();
            string json = File.ReadAllText(file);
            // The first version kept a bare list, taken by any PC: started again from nothing
            if (json.TrimStart().StartsWith('['))
                return new State();
            State state = JsonSerializer.Deserialize<State>(json) ?? new State();
            state.Devices.RemoveAll(d => DateTime.UtcNow - d.LastSeen > ForgetDevicesAfter);
            state.Installs.RemoveAll(e => e.Status != Pending && DateTime.UtcNow - e.UpdatedAt > KeepOutcomesFor);
            return state;
        }

        private void Write(int userId, State state)
        {
            // Written aside then moved: never half written
            string file = FileOf(userId), part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(state));
            File.Move(part, file, overwrite: true);
        }

        /// <summary>A client of the player says it is open.</summary>
        public void Seen(int userId, string deviceId, string name)
        {
            if (!Enabled)
                return;
            lock (writing)
            {
                State state = Read(userId);
                state.Devices.RemoveAll(d => d.Id == deviceId);
                state.Devices.Add(new Device(deviceId, name, DateTime.UtcNow));
                Write(userId, state);
            }
        }

        /// <summary>The player's PCs, the open ones first.</summary>
        public List<Device> Devices(int userId)
        {
            lock (writing)
                return Read(userId).Devices.OrderByDescending(d => d.Online).ThenByDescending(d => d.LastSeen).ToList();
        }

        /// <summary>Every request of the player, newest first; with a device, only the ones that PC still has to take.</summary>
        public List<Entry> List(int userId, string? deviceId = null)
        {
            lock (writing)
            {
                IEnumerable<Entry> entries = Read(userId).Installs;
                if (deviceId != null)
                    entries = entries.Where(e => e.DeviceId == deviceId && e.Status == Pending);
                return entries.OrderByDescending(e => e.RequestedAt).ToList();
            }
        }

        public enum AddResult { Added, AlreadyAsked, Full, UnknownDevice, Disabled }

        public AddResult Add(int userId, int gameId, string title, string deviceId)
        {
            if (!Enabled)
                return AddResult.Disabled;
            lock (writing)
            {
                State state = Read(userId);
                Device? device = state.Devices.FirstOrDefault(d => d.Id == deviceId);
                if (device == null)
                    return AddResult.UnknownDevice;
                if (state.Installs.Any(e => e.GameId == gameId && e.DeviceId == deviceId && e.Status == Pending))
                    return AddResult.AlreadyAsked;
                if (state.Installs.Count(e => e.Status == Pending) >= MaxPerUser)
                    return AddResult.Full;
                // Asked again after an outcome: the old line goes
                state.Installs.RemoveAll(e => e.GameId == gameId && e.DeviceId == deviceId);
                DateTime now = DateTime.UtcNow;
                state.Installs.Add(new Entry(Guid.NewGuid().ToString("N"), gameId, title, deviceId, device.Name, Pending, null, now, now));
                Write(userId, state);
                return AddResult.Added;
            }
        }

        /// <summary>The PC says what became of a request; false when there is no such request.</summary>
        public bool Report(int userId, string id, string status, string? message)
        {
            if (!Enabled)
                return false;
            lock (writing)
            {
                State state = Read(userId);
                int index = state.Installs.FindIndex(e => e.Id == id);
                if (index < 0)
                    return false;
                state.Installs[index] = state.Installs[index] with { Status = status, Message = message, UpdatedAt = DateTime.UtcNow };
                Write(userId, state);
                return true;
            }
        }

        public void Remove(int userId, string id)
        {
            if (!Enabled)
                return;
            lock (writing)
            {
                State state = Read(userId);
                if (state.Installs.RemoveAll(e => e.Id == id) > 0)
                    Write(userId, state);
            }
        }
    }
}
