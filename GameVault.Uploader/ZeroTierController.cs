using System.Net.Http.Json;
using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The ZeroTier network controller (ZeroTier One 1.14.2, Apache 2.0 since 2026-01-01) through its JSON API: one
    /// private network per room, with broadcast on (the games find each other's parties), and the players' PCs let in
    /// with the address the service chose for them.
    /// </summary>
    public sealed class ZeroTierController(HttpClient controller, UploaderOptions options)
    {
        public sealed class ControllerException(string message, Exception? inner = null) : Exception(message, inner);

        public bool Enabled => controller.BaseAddress != null && !string.IsNullOrEmpty(options.ZeroTierApiSecret);

        private string? address;

        private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent.Create(body) };
            request.Headers.Add("X-ZT1-Auth", options.ZeroTierApiSecret);
            try
            {
                using HttpResponseMessage response = await controller.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    throw new ControllerException($"The ZeroTier controller answered {(int)response.StatusCode} to {method} {path}.");
                string text = await response.Content.ReadAsStringAsync(ct);
                return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new ControllerException("The ZeroTier controller could not be reached.", ex);
            }
        }

        /// <summary>A new private network for the subnet 10.147.{subnet}.0/24; its id.</summary>
        public async Task<string> CreateNetworkAsync(string name, int subnet, CancellationToken ct)
        {
            address ??= (await SendAsync(HttpMethod.Get, "status", null, ct)).GetProperty("address").GetString();
            JsonElement network = await SendAsync(HttpMethod.Post, $"controller/network/{address}______", new
            {
                name,
                @private = true,
                enableBroadcast = true,
                // The service gives each PC its address: none taken from a pool
                v4AssignMode = new { zt = false },
                routes = new[] { new { target = $"10.147.{subnet}.0/24" } },
            }, ct);
            return network.GetProperty("id").GetString() ?? throw new ControllerException("The ZeroTier controller made a network without an id.");
        }

        public Task AuthorizeAsync(string networkId, string nodeId, string ip, CancellationToken ct) =>
            SendAsync(HttpMethod.Post, $"controller/network/{networkId}/member/{nodeId}", new { authorized = true, ipAssignments = new[] { ip } }, ct);

        /// <summary>Shut out at once (a deleted member alone would keep its certificate until it expires), then forgotten.</summary>
        public async Task RemoveAsync(string networkId, string nodeId, CancellationToken ct)
        {
            await SendAsync(HttpMethod.Post, $"controller/network/{networkId}/member/{nodeId}", new { authorized = false }, ct);
            await SendAsync(HttpMethod.Delete, $"controller/network/{networkId}/member/{nodeId}", null, ct);
        }

        public Task DeleteNetworkAsync(string networkId, CancellationToken ct) => SendAsync(HttpMethod.Delete, $"controller/network/{networkId}", null, ct);
    }
}
