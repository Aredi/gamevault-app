using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// What the web page needs from the GameVault server, asked by the service (same origin for the browser, no CORS):
    /// signing in with the GameVault account, refreshing the session, and the games already on the server.
    /// </summary>
    public sealed class GameVaultProxy(HttpClient gameVault, ILogger<GameVaultProxy> logger)
    {
        public sealed record Forwarded(int Status, string Body);

        /// <summary>The GameVault login (basic auth): the token pair, or the server's refusal.</summary>
        public async Task<Forwarded> LoginAsync(string username, string password, string? userAgent, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/basic/login");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
            if (!string.IsNullOrEmpty(userAgent))
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            return await SendAsync(request, cancellationToken);
        }

        public async Task<Forwarded> RefreshAsync(string authorization, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh");
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            return await SendAsync(request, cancellationToken);
        }

        public sealed record GameSummary(int Id, string Title, string? Version, int? Year);

        /// <summary>Games of the server matching a search, for "publish as a new version of".</summary>
        public Task<List<GameSummary>> SearchGamesAsync(string authorization, string search, CancellationToken cancellationToken) =>
            GamesAsync(authorization, $"api/games?search={Uri.EscapeDataString(search)}&limit=10", cancellationToken);

        /// <summary>All the games of the server, by their metadata's title when they have one (not their file's), for the games playable in LAN.</summary>
        public Task<List<GameSummary>> AllGamesAsync(string authorization, CancellationToken cancellationToken) =>
            GamesAsync(authorization, "api/games?limit=-1", cancellationToken, metadataTitle: true);

        private async Task<List<GameSummary>> GamesAsync(string authorization, string path, CancellationToken cancellationToken, bool metadataTitle = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            Forwarded answer = await SendAsync(request, cancellationToken);
            var games = new List<GameSummary>();
            if (answer.Status != 200)
                return games;
            using JsonDocument json = JsonDocument.Parse(answer.Body);
            if (!json.RootElement.TryGetProperty("data", out JsonElement data))
                return games;
            foreach (JsonElement game in data.EnumerateArray())
            {
                if (game.TryGetProperty("deleted_at", out var deleted) && deleted.ValueKind != JsonValueKind.Null)
                    continue;
                int? year = game.TryGetProperty("release_date", out var date) && date.ValueKind == JsonValueKind.String && DateTime.TryParse(date.GetString(), out DateTime d) ? d.Year : null;
                string? named = metadataTitle && game.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                    && metadata.TryGetProperty("title", out var metaTitle) && metaTitle.ValueKind == JsonValueKind.String ? metaTitle.GetString() : null;
                games.Add(new GameSummary(
                    game.GetProperty("id").GetInt32(),
                    !string.IsNullOrWhiteSpace(named) ? named : game.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    game.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString() : null,
                    year));
            }
            return games;
        }

        /// <summary>One game of the server, null when there is none (the game asked from the GameVault web page).</summary>
        public async Task<GameSummary?> GetGameAsync(string authorization, int id, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/games/{id}");
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            Forwarded answer = await SendAsync(request, cancellationToken);
            if (answer.Status != 200)
                return null;
            using JsonDocument game = JsonDocument.Parse(answer.Body);
            return new GameSummary(id, game.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "", null, null);
        }

        private async Task<Forwarded> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response = await gameVault.SendAsync(request, cancellationToken);
                return new Forwarded((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "The GameVault server could not be reached");
                return new Forwarded(StatusCodes.Status502BadGateway, JsonSerializer.Serialize(new { message = "The game server could not be reached." }));
            }
        }
    }
}
