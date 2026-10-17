using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// Game requests go to the RomM-ComM Discord bot (its requests API), so the requests made in Discord and in
    /// SanctuaryVault end up in one list. The service adds who asks: the player it checked with GameVault.
    /// </summary>
    public sealed class GameRequestsProxy(HttpClient bot, UploaderOptions options, ILogger<GameRequestsProxy> logger)
    {
        public bool Enabled => !string.IsNullOrEmpty(options.RommCommUrl) && !string.IsNullOrEmpty(options.RommCommApiKey);

        public sealed record Forwarded(int Status, string Body);

        public Task<Forwarded> ListAsync(int userId, string username, CancellationToken cancellationToken) =>
            SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/requests?requester_id={userId}&requester_name={Uri.EscapeDataString(username)}"), cancellationToken);

        public Task<Forwarded> AskAsync(int userId, string username, string game, string? details, CancellationToken cancellationToken)
        {
            string body = JsonSerializer.Serialize(new
            {
                game,
                details,
                // RomM's platform for the GameVault games folder (roms/win): a game published there fulfils the request
                platform = "Windows",
                source = "SanctuaryVault",
                requester = new { id = userId, name = username },
            });
            return SendAsync(new HttpRequestMessage(HttpMethod.Post, "api/requests") { Content = new StringContent(body, Encoding.UTF8, "application/json") }, cancellationToken);
        }

        private async Task<Forwarded> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (request)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.RommCommApiKey);
                try
                {
                    using HttpResponseMessage response = await bot.SendAsync(request, cancellationToken);
                    return new Forwarded((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    logger.LogWarning(ex, "The RomM-ComM bot could not be reached");
                    return new Forwarded(StatusCodes.Status502BadGateway, JsonSerializer.Serialize(new { error = "The requests bot could not be reached." }));
                }
            }
        }
    }
}
