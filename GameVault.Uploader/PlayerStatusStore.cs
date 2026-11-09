using System.Collections.Concurrent;

namespace GameVault.Uploader
{
    /// <summary>
    /// What each player shows of themselves in the chat: away, do not disturb, invisible (online is the default: no
    /// entry), and since when they play the library's game (GameVault does not say it). Said each minute by the clients,
    /// kept in memory, forgotten after 3 minutes of silence.
    /// </summary>
    public sealed class PlayerStatusStore
    {
        public static readonly TimeSpan Fresh = HiddenPlayStore.Fresh;
        public static readonly IReadOnlySet<string> States = new HashSet<string> { "online", "away", "busy", "invisible" };

        public sealed record Status(int UserId, string State, int? GameId, DateTimeOffset? GameSince, DateTimeOffset At);

        private readonly ConcurrentDictionary<int, Status> statuses = new();

        /// <returns>Whether the others see a change (the state, the game): they are told then, not each minute.</returns>
        public bool Set(Status now)
        {
            bool changed = !statuses.TryGetValue(now.UserId, out Status? before) || now.At - before.At >= Fresh
                || before.State != now.State || before.GameId != now.GameId || before.GameSince != now.GameSince;
            statuses[now.UserId] = now;
            return changed;
        }

        public void Forget(int userId) => statuses.TryRemove(userId, out _);

        /// <summary>As the others see it: an invisible player is offline, without their game.</summary>
        public static Status Shown(Status status) =>
            status.State == "invisible" ? status with { State = "offline", GameId = null, GameSince = null } : status;

        public List<Status> Now(DateTimeOffset now) => statuses.Values.Where(s => now - s.At < Fresh).Select(Shown).ToList();
    }
}
