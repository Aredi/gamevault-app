using System.Collections.Concurrent;

namespace GameVault.Uploader
{
    /// <summary>
    /// The retro games played now: GameVault only knows its own games, so the clients say here what they play in
    /// RetroArch, Eden, ... (each minute while it runs). Kept in memory, gone after a restart or 3 minutes of silence.
    /// </summary>
    public sealed class RetroPresenceStore
    {
        public static readonly TimeSpan Fresh = HiddenPlayStore.Fresh;

        public sealed record Playing(int UserId, string Platform, int RomId, string Name, string PlatformName, DateTimeOffset At);

        private readonly ConcurrentDictionary<int, Playing> playing = new();

        /// <returns>Whether it changed (a new game, or it stopped): the others are told then, not each minute.</returns>
        public bool Set(int userId, Playing? now)
        {
            if (now == null)
                return playing.TryRemove(userId, out _);
            bool changed = !playing.TryGetValue(userId, out Playing? before) || before.Platform != now.Platform || before.RomId != now.RomId;
            playing[userId] = now;
            return changed;
        }

        public List<Playing> Now(DateTimeOffset now) => playing.Values.Where(p => now - p.At < Fresh).ToList();
    }
}
