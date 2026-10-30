using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using gamevault.Localization;
using System;

namespace gamevault.Helper
{
    /// <summary>The games marked as using a hypervisor, and the text shown to who downloads them.</summary>
    public sealed class HypervisorNotice
    {
        [JsonPropertyName("games")] public List<int> Games { get; set; } = new();
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    internal static class HypervisorService
    {
        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>Null when the service offers none.</summary>
        public static async Task<HypervisorNotice?> GetAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/hypervisor");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return null;
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<HypervisorNotice>(await response.Content.ReadAsStringAsync());
        }

        public static Task SetGameAsync(int gameId, bool on) => PutAsync($"hypervisor/games/{gameId}", new { on });

        public static Task SetTextAsync(string text) => PutAsync("hypervisor/text", new { text });

        private static async Task PutAsync(string path, object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Service}/{path}")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(Loc.F("The SanctuaryVault service answered {0}.", (int)response.StatusCode));
        }
    }
}
