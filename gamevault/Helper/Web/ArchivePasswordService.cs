using gamevault.Localization;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>The password of a game's archive, kept by the SanctuaryVault service: set on the game's page by an administrator, used to extract it.</summary>
    internal static class ArchivePasswordService
    {
        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>
        /// The game's password: "" when it has none, null when the service keeps none (an older one, or no volume for them).
        /// </summary>
        public static async Task<string?> GetAsync(int gameId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/archive-passwords/games/{gameId}");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            using JsonDocument answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return answer.RootElement.TryGetProperty("password", out JsonElement password) && password.ValueKind == JsonValueKind.String ? password.GetString() ?? "" : "";
        }

        /// <summary>Empty: none any more.</summary>
        public static async Task SetAsync(int gameId, string password)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Service}/archive-passwords/games/{gameId}")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { password }), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(Loc.F("The SanctuaryVault service answered {0}.", (int)response.StatusCode));
        }
    }
}
