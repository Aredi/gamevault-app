using System;
using System.Collections.Generic;
using System.Linq;

namespace GameVault.Core.Library
{
    /// <summary>
    /// "Continue playing": the installed PC games and the retro games played here, in one row, the last played first.
    /// </summary>
    public static class ContinuePlaying
    {
        /// <param name="pc">The PC games in their order (last launched first), with their last session when known.</param>
        /// <param name="retro">The retro games, with their last session.</param>
        /// <remarks>The PC games keep their order: a retro game goes before the first PC game played before it, a PC game
        /// never played before all of them.</remarks>
        public static List<T> Merge<T>(IReadOnlyList<(T Item, DateTime? Played)> pc, IEnumerable<(T Item, DateTime Played)> retro)
        {
            var queue = new Queue<(T Item, DateTime Played)>(retro.OrderByDescending(r => r.Played.ToUniversalTime()));
            var merged = new List<T>(pc.Count + queue.Count);
            foreach (var (item, played) in pc)
            {
                DateTime at = played?.ToUniversalTime() ?? DateTime.MinValue;
                while (queue.Count > 0 && queue.Peek().Played.ToUniversalTime() > at)
                    merged.Add(queue.Dequeue().Item);
                merged.Add(item);
            }
            merged.AddRange(queue.Select(r => r.Item));
            return merged;
        }
    }
}
