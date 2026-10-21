using System.Text.Json;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// What the player's RomM keeps of them for a game: their status and rating (RomM's "rom_user"), and the game's
    /// RetroAchievements with the ones they earned (RomM's RetroAchievements metadata, and the player's progression).
    /// </summary>
    public static class RommProgress
    {
        /// <summary>RomM's statuses of a game, in the order the page lists them ("" is none).</summary>
        public static readonly IReadOnlyList<string> Statuses = new[] { "", "incomplete", "finished", "completed_100", "retired", "never_playing" };

        public sealed record Mine(string Status, int Rating);

        public sealed record Achievement(string Id, string Title, string Description, int Points, string? Badge, string? LockedBadge, bool Earned);

        private static string? Text(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static long? Number(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
        private static JsonElement Child(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) ? v : default;

        /// <param name="rom">RomM's details of the rom (GET /api/roms/{id}).</param>
        public static Mine MineOf(JsonElement rom)
        {
            JsonElement user = Child(rom, "rom_user");
            string status = Text(user, "status") ?? "";
            return new Mine(Statuses.Contains(status) ? status : "", (int)Math.Clamp(Number(user, "rating") ?? 0, 0, 10));
        }

        /// <param name="rom">RomM's details of the rom.</param>
        /// <param name="me">The player in RomM (GET /api/users/me): their RetroAchievements progression.</param>
        /// <returns>The game's achievements in RetroAchievements' order, the earned ones marked; none when RomM has none.</returns>
        public static List<Achievement> AchievementsOf(JsonElement rom, JsonElement me)
        {
            long? game = Number(rom, "ra_id");
            var earned = new HashSet<string>();
            JsonElement results = Child(Child(me, "ra_progression"), "results");
            if (game != null && results.ValueKind == JsonValueKind.Array)
                foreach (JsonElement progress in results.EnumerateArray().Where(p => Number(p, "rom_ra_id") == game))
                    if (Child(progress, "earned_achievements") is { ValueKind: JsonValueKind.Array } list)
                        earned.UnionWith(list.EnumerateArray().Select(a => Child(a, "id")).Select(id => id.ValueKind == JsonValueKind.Number ? id.GetInt64().ToString() : id.ToString()));
            JsonElement achievements = Child(Child(rom, "merged_ra_metadata"), "achievements");
            if (achievements.ValueKind != JsonValueKind.Array)
                return new();
            return achievements.EnumerateArray()
                .OrderBy(a => Number(a, "display_order") ?? 0)
                .Select(a =>
                {
                    string id = Number(a, "ra_id")?.ToString() ?? "";
                    // RomM keeps an earned one by its badge's name ("379571"), not its id
                    bool done = Text(a, "badge_id") is string badge && earned.Contains(badge);
                    return new Achievement(id, Text(a, "title") ?? "", Text(a, "description") ?? "", (int)(Number(a, "points") ?? 0),
                        Text(a, "badge_url"), Text(a, "badge_url_lock"), done);
                }).ToList();
        }
    }
}
