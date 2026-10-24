using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// Whether a PC game plays in LAN, from its PCGamingWiki page (its "lan play" fields). The wiki's database queries
    /// need a permission: the page's text is read instead.
    /// </summary>
    public sealed partial class PcGamingWiki(HttpClient wiki, ILogger<PcGamingWiki> logger)
    {
        /// <summary>Lan: "true" or "limited" on the wiki; Players: its count, when given.</summary>
        public sealed record Detected(bool Lan, int? Players);

        [GeneratedRegex(@"^\|lan play\s*=\s*(\w*)", RegexOptions.Multiline)]
        private static partial Regex LanPlay();

        [GeneratedRegex(@"^\|lan play players\s*=\s*(\d+)", RegexOptions.Multiline)]
        private static partial Regex LanPlayers();

        /// <summary>
        /// A game's page, by its title (the wiki's redirects followed), else the wiki's search when it finds the same title
        /// but for its punctuation and case ("Ghost of Tsushima: Director's Cut", "CloverPit"). No page: not in LAN.
        /// Null when the wiki did not answer.
        /// </summary>
        public async Task<Detected?> LanAsync(string title, CancellationToken cancellationToken)
        {
            try
            {
                string? wikitext = await WikitextAsync(title, cancellationToken);
                if (wikitext == null)
                {
                    using JsonDocument found = JsonDocument.Parse(await wiki.GetStringAsync($"api.php?action=query&list=search&srnamespace=0&srlimit=3&format=json&formatversion=2&srsearch={Uri.EscapeDataString(title)}", cancellationToken));
                    string? same = found.RootElement.GetProperty("query").GetProperty("search").EnumerateArray()
                        .Select(r => r.GetProperty("title").GetString()).FirstOrDefault(t => t != null && Letters(t) == Letters(title));
                    if (same != null)
                        wikitext = await WikitextAsync(same, cancellationToken);
                }
                return Parse(wikitext);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
            {
                logger.LogInformation("PCGamingWiki did not answer for {Title}: {Error}", title, ex.Message);
                return null;
            }
        }

        private async Task<string?> WikitextAsync(string page, CancellationToken cancellationToken)
        {
            using JsonDocument json = JsonDocument.Parse(await wiki.GetStringAsync($"api.php?action=parse&prop=wikitext&redirects=1&format=json&formatversion=2&page={Uri.EscapeDataString(page)}", cancellationToken));
            return json.RootElement.TryGetProperty("parse", out var parsed) ? parsed.GetProperty("wikitext").GetString() : null;
        }

        private static string Letters(string title) => new string(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        public static Detected Parse(string? wikitext)
        {
            string lan = wikitext != null ? LanPlay().Match(wikitext).Groups[1].Value : "";
            Match players = wikitext != null ? LanPlayers().Match(wikitext) : Match.Empty;
            return new Detected(lan is "true" or "limited", players.Success && int.TryParse(players.Groups[1].Value, out int count) ? count : null);
        }
    }
}
