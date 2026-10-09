using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// Only administrators of the GameVault server may upload: the request's Authorization header is passed to
    /// GET /api/users/me of that server. Answers are cached for a minute, so a big upload does not ask for every chunk.
    /// </summary>
    public sealed class AdminCheck(HttpClient gameVault, ILogger<AdminCheck> logger)
    {
        private static readonly ConcurrentDictionary<string, (bool Admin, string User, int Id, DateTime Until)> cache = new();
        private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(1);

        public sealed record Result(bool Allowed, int Status, string Message, string User = "", int Id = 0, bool Admin = false);

        /// <summary>Any signed-in user of the GameVault server (profiles are seen by every player).</summary>
        public async Task<Result> CheckUserAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            Result result = await CheckAsync(request, cancellationToken);
            return result.Status == StatusCodes.Status403Forbidden ? result with { Allowed = true, Status = 200, Message = "" } : result;
        }

        public async Task<Result> CheckAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            string? authorization = request.Headers.Authorization;
            if (string.IsNullOrWhiteSpace(authorization) || !AuthenticationHeaderValue.TryParse(authorization, out var header))
                return new Result(false, StatusCodes.Status401Unauthorized, "Sign in to GameVault: the Authorization header is missing.");

            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authorization)));
            if (cache.TryGetValue(key, out var cached) && cached.Until > DateTime.UtcNow)
                return cached.Admin ? new Result(true, 200, "", cached.User, cached.Id, true) : Forbidden(cached.User, cached.Id);

            using var me = new HttpRequestMessage(HttpMethod.Get, "api/users/me");
            me.Headers.Authorization = header;
            HttpResponseMessage response;
            try
            {
                response = await gameVault.SendAsync(me, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "The GameVault server could not be reached");
                return new Result(false, StatusCodes.Status502BadGateway, "The GameVault server could not be reached to check the user.");
            }
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    return new Result(false, StatusCodes.Status401Unauthorized, "GameVault did not accept this sign-in.");
                using JsonDocument user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                string name = user.RootElement.TryGetProperty("username", out var username) ? username.GetString() ?? "" : "";
                bool admin = user.RootElement.TryGetProperty("role", out var role) && IsAdmin(role);
                int id = user.RootElement.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.Number ? idValue.GetInt32() : 0;
                cache[key] = (admin, name, id, DateTime.UtcNow + CacheTime);
                return admin ? new Result(true, 200, "", name, id, true) : Forbidden(name, id);
            }
        }

        /// <summary>GameVault roles: 0 guest, 1 user, 2 editor, 3 admin (as a number, or its name).</summary>
        internal static bool IsAdmin(JsonElement role) => role.ValueKind switch
        {
            JsonValueKind.Number => role.TryGetInt32(out int value) && value >= 3,
            JsonValueKind.String => string.Equals(role.GetString(), "ADMIN", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        private static Result Forbidden(string user, int id) => new(false, StatusCodes.Status403Forbidden, $"'{user}' is not an administrator of the GameVault server.", user, id);

        internal static void ClearCache() => cache.Clear();
    }
}
