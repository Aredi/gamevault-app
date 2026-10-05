using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameVault.Core.Compatibility
{
    /// <summary>
    /// The umu database (Open-Wine-Components/umu-database) maps game titles to umu ids. Started with
    /// GAMEID=&lt;umu id&gt;, umu-run and GE-Proton apply the protonfixes known for that game automatically
    /// (missing runtimes, DLL overrides, ...), like Steam does for its own games.
    /// </summary>
    public static class UmuDatabase
    {
        public const string DefaultGameId = "umu-default";

        public static string LookupUrl(string title) => $"https://umu.openwinecomponents.org/umu_api.php?title={Uri.EscapeDataString(title)}";

        /// <summary>The umu id of <paramref name="title"/>, or null when the database does not know the game.</summary>
        public static async Task<string?> LookupAsync(string title, CancellationToken cancellationToken = default)
        {
            foreach (string candidate in TitleCandidates(title))
            {
                string json = await HttpClients.Shared.GetStringAsync(LookupUrl(candidate), cancellationToken);
                string? id = ParseId(json);
                if (id != null)
                    return id;
            }
            return null;
        }

        /// <summary>
        /// The title as is, without ™/®, and without a subtitle or edition ("Game: Game of the Year Edition" → "Game").
        /// </summary>
        internal static List<string> TitleCandidates(string title)
        {
            var candidates = new List<string>();
            void Add(string value)
            {
                value = Regex.Replace(value, @"\s+", " ").Trim();
                if (value.Length > 0 && !candidates.Contains(value, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(value);
            }
            Add(title);
            string clean = Regex.Replace(title, "[™®©]", "");
            Add(clean);
            Add(Regex.Replace(clean, @"\s*[-–:]\s*[^-–:]*(edition|remastered|definitive|complete|goty)[^-–:]*$", "", RegexOptions.IgnoreCase));
            int colon = clean.IndexOf(':');
            if (colon > 2)
                Add(clean[..colon]);
            return candidates;
        }

        internal static string? ParseId(string json)
        {
            if (JsonNode.Parse(json) is not JsonArray entries)
                return null;
            foreach (JsonNode? entry in entries)
            {
                string? id = entry?["umu_id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
            return null;
        }

        /// <summary>umu ids look like "umu-271590" or "umu-dauntless".</summary>
        public static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, @"^umu-[A-Za-z0-9_.-]+$");
    }
}
