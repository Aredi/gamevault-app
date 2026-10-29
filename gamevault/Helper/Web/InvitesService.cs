using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>An invitation code of the administrators, named after who it is for.</summary>
    public sealed class InviteItem
    {
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("usedAt")] public DateTime? UsedAt { get; set; }
        [JsonPropertyName("userId")] public int? UserId { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }

        public bool IsUnused => UsedAt == null;
        public string StatusText => UsedAt is DateTime used
            ? Loc.F("Used by {0} · {1:dd/MM/yyyy}", Username, used.ToLocalTime())
            : Loc.F("Not used yet · {0:dd/MM/yyyy}", CreatedAt.ToLocalTime());
    }

    /// <summary>
    /// The invitation codes of the SanctuaryVault service: a new account waits for its activation until it gives one.
    /// </summary>
    internal static class InvitesService
    {
        /// <summary>Before any sign-in: the account can not sign in until it is activated.</summary>
        private static readonly HttpClient anonymous = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>The codes, newest first; null when the service offers none.</summary>
        public static async Task<List<InviteItem>?> ListAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/invites");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return null;
            await ThrowIfFailedAsync(response);
            return JsonSerializer.Deserialize<List<InviteItem>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        public static async Task<InviteItem> CreateAsync(string name)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Service}/invites")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            await ThrowIfFailedAsync(response);
            return JsonSerializer.Deserialize<InviteItem>(await response.Content.ReadAsStringAsync())!;
        }

        public static async Task DeleteAsync(string code)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Service}/invites/{Uri.EscapeDataString(code)}");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            await ThrowIfFailedAsync(response);
        }

        /// <summary>Activates the account of this name, waiting for its activation, with an invitation code.</summary>
        public static async Task RedeemAsync(string code, string username)
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { code, username }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await anonymous.PostAsync($"{Service}/invites/redeem", content);
            await ThrowIfFailedAsync(response);
        }

        private static async Task ThrowIfFailedAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;
            string? error = null;
            try
            {
                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.TryGetProperty("error", out JsonElement value))
                    error = WebExceptionHelper.Translate(value.GetString() ?? "");
            }
            catch (JsonException) { }
            throw new InvalidOperationException(error ?? Loc.F("The SanctuaryVault service answered {0}.", (int)response.StatusCode));
        }
    }
}
