namespace GameVault.Core.Library
{
    public enum PlayStatusFilter
    {
        All,
        Played,
        NeverPlayed,
    }

    /// <summary>One play record of the current user (from /api/progresses).</summary>
    public record PlayRecord(int GameId, DateTime? LastPlayedAt, int MinutesPlayed);

    /// <summary>
    /// Builds the parts of the library query the server cannot do by itself: collections, played / never played
    /// and sorting by the user's own play data (the server sorts games only by their own columns).
    /// </summary>
    public static class LibraryQuery
    {
        /// <summary>
        /// The "filter.id" condition for a collection and a play status, or "" when nothing restricts the ids.
        /// An empty selection becomes an impossible id so the library correctly shows no games.
        /// </summary>
        public static string IdFilter(IReadOnlyCollection<int>? collectionIds, PlayStatusFilter playStatus, IReadOnlyCollection<int> playedIds)
        {
            HashSet<int>? only = collectionIds == null ? null : new HashSet<int>(collectionIds);
            HashSet<int>? exclude = null;
            if (playStatus == PlayStatusFilter.Played)
            {
                only = only == null ? new HashSet<int>(playedIds) : new HashSet<int>(only.Intersect(playedIds));
            }
            else if (playStatus == PlayStatusFilter.NeverPlayed)
            {
                if (only != null)
                    only.ExceptWith(playedIds);
                else if (playedIds.Count > 0)
                    exclude = new HashSet<int>(playedIds);
            }

            if (only != null)
                return only.Count == 0 ? "&filter.id=$eq:-1" : $"&filter.id=$in:{string.Join(',', only.OrderBy(i => i))}";
            if (exclude != null)
                return $"&filter.id=$not:$in:{string.Join(',', exclude.OrderBy(i => i))}";
            return "";
        }

        /// <summary>Game ids ordered by last play (or play time), most recent / longest first unless ascending.</summary>
        public static List<int> OrderByPlay(IEnumerable<PlayRecord> records, bool byPlaytime, bool ascending)
        {
            var distinct = records
                .GroupBy(r => r.GameId)
                .Select(g => new PlayRecord(g.Key, g.Max(r => r.LastPlayedAt), g.Sum(r => r.MinutesPlayed)));
            var ordered = byPlaytime
                ? distinct.OrderByDescending(r => r.MinutesPlayed).ThenByDescending(r => r.LastPlayedAt)
                : distinct.OrderByDescending(r => r.LastPlayedAt ?? DateTime.MinValue).ThenByDescending(r => r.MinutesPlayed);
            var ids = ordered.Select(r => r.GameId).ToList();
            if (ascending)
                ids.Reverse();
            return ids;
        }
    }
}
