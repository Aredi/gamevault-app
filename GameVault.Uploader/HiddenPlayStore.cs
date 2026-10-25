using System.Collections.Concurrent;

namespace GameVault.Uploader
{
    /// <summary>
    /// The games played by the players who hide what they play from the others (Community page): only the
    /// administrators see them. Their client says it each minute while they play; kept in memory, gone after a restart.
    /// </summary>
    public sealed class HiddenPlayStore
    {
        /// <summary>A client gone without saying it stopped (closed, crashed): its game is no longer shown after this.</summary>
        public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(3);

        public sealed record Playing(int UserId, int GameId, DateTimeOffset At);

        private readonly ConcurrentDictionary<int, Playing> playing = new();

        public void Set(int userId, int? gameId, DateTimeOffset now)
        {
            if (gameId is int game)
                playing[userId] = new Playing(userId, game, now);
            else
                playing.TryRemove(userId, out _);
        }

        public List<Playing> Now(DateTimeOffset now) => playing.Values.Where(p => now - p.At < Fresh).ToList();
    }
}
