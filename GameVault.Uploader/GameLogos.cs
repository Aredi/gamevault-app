using System.Net.Http.Headers;
using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The logos of the games (their title as it is drawn on their box), and the retro games' large pictures, for the living-room mode. A retro game's
    /// comes from RomM, which got it from ScreenScraper; without one, and for the library's games, SteamGridDB is
    /// asked when its key is given (STEAMGRIDDB_API_KEY). Each logo is fetched once and kept in LOGOS_DIRECTORY; the
    /// addresses RomM keeps carry its own keys and never leave this service.
    /// </summary>
    public sealed class GameLogos(HttpClient web, UploaderOptions options, RommProxy romm, ILogger<GameLogos> logger)
    {
        // A hero is larger than a logo (1920 pixels wide)
        private const int MaxSize = 12 * 1024 * 1024;
        /// <summary>A game without a logo is asked again after this long (it may get one).</summary>
        public static readonly TimeSpan AskAgain = TimeSpan.FromDays(7);
        private static readonly SemaphoreSlim fetching = new(3, 3);

        public bool HasSteamGrid => !string.IsNullOrWhiteSpace(options.SteamGridDbKey);
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        private string File(string name) => Path.Combine(options.LogosDirectory, name);

        /// <summary>Nothing can be said now (no key, a site that fails): the game is not marked as having no logo.</summary>
        private sealed class NotNowException(string message) : Exception(message);

        /// <summary>The picture's type by its first bytes; null when it is not a picture a client shows.</summary>
        public static string? TypeOf(ReadOnlySpan<byte> start) =>
            start.Length >= 8 && start[0] == 0x89 && start[1] == 0x50 && start[2] == 0x4E && start[3] == 0x47 ? "image/png"
            : start.Length >= 3 && start[0] == 0xFF && start[1] == 0xD8 && start[2] == 0xFF ? "image/jpeg"
            : start.Length >= 12 && start[0] == (byte)'R' && start[1] == (byte)'I' && start[2] == (byte)'F' && start[3] == (byte)'F' && start[8] == (byte)'W' && start[9] == (byte)'E' && start[10] == (byte)'B' && start[11] == (byte)'P' ? "image/webp"
            : null;

        /// <summary>The logo kept for this game, or the one found now; null when it has none.</summary>
        private async Task<string?> KeptOrAsync(string name, Func<Task<byte[]?>> find, CancellationToken cancellationToken)
        {
            string file = File(name + ".img"), none = File(name + ".none");
            if (System.IO.File.Exists(file))
                return file;
            if (System.IO.File.Exists(none) && Clock() - System.IO.File.GetLastWriteTimeUtc(none) < AskAgain)
                return null;
            await fetching.WaitAsync(cancellationToken);
            try
            {
                if (System.IO.File.Exists(file))
                    return file;
                Directory.CreateDirectory(options.LogosDirectory);
                byte[]? logo = null;
                try { logo = await find(); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or RommProxy.RommException or NotNowException && !cancellationToken.IsCancellationRequested)
                {
                    // Asked again at the next game's screen: nothing is kept of a failure
                    logger.LogInformation("The logo {Name} could not be fetched: {Error}", name, ex.Message);
                    return null;
                }
                if (logo == null || TypeOf(logo) == null)
                {
                    System.IO.File.WriteAllText(none, "");
                    System.IO.File.SetLastWriteTimeUtc(none, Clock());
                    return null;
                }
                // Written aside then moved: never half written
                System.IO.File.WriteAllBytes(file + ".part", logo);
                System.IO.File.Move(file + ".part", file, true);
                System.IO.File.Delete(none);
                return file;
            }
            finally { fetching.Release(); }
        }

        /// <summary>A retro game's logo: RomM's (ScreenScraper), else SteamGridDB's by the id RomM matched or by its name.</summary>
        public Task<string?> RetroAsync(int romId, CancellationToken cancellationToken) => KeptOrAsync($"retro-{romId}", async () =>
        {
            JsonElement? rom = await romm.RomAsync(romId, cancellationToken);
            if (rom is not JsonElement game)
                return null;
            if (game.TryGetProperty("ss_metadata", out JsonElement screenScraper) && screenScraper.ValueKind == JsonValueKind.Object)
            {
                if (Text(screenScraper, "logo_path") is string path && await romm.ResourceAsync(path, cancellationToken) is byte[] kept && TypeOf(kept) != null)
                    return kept;
                // ScreenScraper refuses at times (its quota): SteamGridDB is asked instead
                try
                {
                    if (Text(screenScraper, "logo_url") is string address && IsScreenScraper(address) && await DownloadAsync(address, null, cancellationToken) is byte[] fetched && TypeOf(fetched) != null)
                        return fetched;
                }
                catch (HttpRequestException ex) { logger.LogInformation("ScreenScraper's logo of {Rom} could not be fetched: {Error}", romId, ex.Message); }
            }
            // Without the key, nobody else can be asked: asked again once it is given
            if (!HasSteamGrid)
                throw new NotNowException("No SteamGridDB key");
            int? id = game.TryGetProperty("sgdb_id", out JsonElement matched) && matched.ValueKind == JsonValueKind.Number ? matched.GetInt32() : null;
            id ??= Text(game, "name") is string name ? await SteamGridIdAsync(name, cancellationToken) : null;
            return id is int found ? await SteamGridLogoAsync(found, cancellationToken) : null;
        }, cancellationToken);

        /// <summary>
        /// A retro game's large picture behind its name in the living-room mode, as GameVault's backgrounds (a screenshot
        /// of a small console is blurred and dark at that size): ScreenScraper's fanart RomM has, else SteamGridDB's hero.
        /// </summary>
        public Task<string?> RetroHeroAsync(int romId, CancellationToken cancellationToken) => KeptOrAsync($"hero-retro-{romId}", async () =>
        {
            JsonElement? rom = await romm.RomAsync(romId, cancellationToken);
            if (rom is not JsonElement game)
                return null;
            if (game.TryGetProperty("ss_metadata", out JsonElement screenScraper) && screenScraper.ValueKind == JsonValueKind.Object)
            {
                if (Text(screenScraper, "fanart_path") is string path && await romm.ResourceAsync(path, cancellationToken) is byte[] kept && TypeOf(kept) != null)
                    return kept;
                try
                {
                    if (Text(screenScraper, "fanart_url") is string address && IsScreenScraper(address) && await DownloadAsync(address, null, cancellationToken) is byte[] fetched && TypeOf(fetched) != null)
                        return fetched;
                }
                catch (HttpRequestException ex) { logger.LogInformation("ScreenScraper's fanart of {Rom} could not be fetched: {Error}", romId, ex.Message); }
            }
            if (!HasSteamGrid)
                throw new NotNowException("No SteamGridDB key");
            int? id = game.TryGetProperty("sgdb_id", out JsonElement matched) && matched.ValueKind == JsonValueKind.Number ? matched.GetInt32() : null;
            id ??= Text(game, "name") is string name ? await SteamGridIdAsync(name, cancellationToken) : null;
            return id is int found ? await SteamGridPictureAsync("heroes", found, cancellationToken) : null;
        }, cancellationToken);

        /// <summary>A library game's logo, from SteamGridDB by its title; null without the key.</summary>
        public Task<string?> PcAsync(int gameId, string title, CancellationToken cancellationToken) => !HasSteamGrid || string.IsNullOrWhiteSpace(title)
            ? Task.FromResult<string?>(null)
            : KeptOrAsync($"pc-{gameId}", async () => await SteamGridIdAsync(title, cancellationToken) is int id ? await SteamGridLogoAsync(id, cancellationToken) : null, cancellationToken);

        private static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

        /// <summary>Only ScreenScraper's own addresses are fetched: RomM's data is not a way to make this service call anything.</summary>
        internal static bool IsScreenScraper(string address) =>
            Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "screenscraper.fr" || uri.Host.EndsWith(".screenscraper.fr", StringComparison.Ordinal));

        private static bool IsSteamGrid(string address) =>
            Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "steamgriddb.com" || uri.Host.EndsWith(".steamgriddb.com", StringComparison.Ordinal));

        private async Task<byte[]?> DownloadAsync(string address, string? bearer, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            if (bearer != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using HttpResponseMessage response = await web.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            // Not there: no picture. Any other refusal (a wrong key, a site down) is a failure, not kept
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"{address.Split('?')[0]} answered {(int)response.StatusCode}", null, response.StatusCode);
            if (response.Content.Headers.ContentLength > MaxSize)
                return null;
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (bytes.Length + read > MaxSize)
                    return null;
                bytes.Write(buffer, 0, read);
            }
            return bytes.ToArray();
        }

        private async Task<JsonDocument?> SteamGridAsync(string path, CancellationToken cancellationToken)
        {
            byte[]? answer = await DownloadAsync("https://www.steamgriddb.com/api/v2/" + path, options.SteamGridDbKey!.Trim(), cancellationToken);
            return answer == null ? null : JsonDocument.Parse(answer);
        }

        /// <summary>SteamGridDB's game of this name: the first it finds.</summary>
        private async Task<int?> SteamGridIdAsync(string name, CancellationToken cancellationToken)
        {
            using JsonDocument? found = await SteamGridAsync("search/autocomplete/" + Uri.EscapeDataString(name.Trim()), cancellationToken);
            return found != null && found.RootElement.TryGetProperty("data", out JsonElement games) && games.ValueKind == JsonValueKind.Array && games.GetArrayLength() > 0
                && games[0].TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : null;
        }

        private Task<byte[]?> SteamGridLogoAsync(int id, CancellationToken cancellationToken) => SteamGridPictureAsync("logos", id, cancellationToken);

        /// <summary>The game's best logo or hero ("logos", "heroes") that is a still picture (SteamGridDB gives the best first).</summary>
        private async Task<byte[]?> SteamGridPictureAsync(string kind, int id, CancellationToken cancellationToken)
        {
            using JsonDocument? found = await SteamGridAsync($"{kind}/game/{id}?types=static", cancellationToken);
            if (found == null || !found.RootElement.TryGetProperty("data", out JsonElement logos) || logos.ValueKind != JsonValueKind.Array)
                return null;
            foreach (JsonElement logo in logos.EnumerateArray().Take(3))
                if (Text(logo, "url") is string address && IsSteamGrid(address) && await DownloadAsync(address, null, cancellationToken) is byte[] picture && TypeOf(picture) != null)
                    return picture;
            return null;
        }
    }
}
