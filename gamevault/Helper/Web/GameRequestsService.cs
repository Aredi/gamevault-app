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
    /// <summary>A game request, as the RomM-ComM bot keeps it (with the ones made in Discord).</summary>
    public sealed class GameRequestItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("game_name")]
        public string Game { get; set; } = "";
        [JsonPropertyName("status")]
        public string Status { get; set; } = "pending";
        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        public string StatusText => Status switch
        {
            "fulfilled" => Loc.T("Added"),
            "reject" => Loc.T("Refused"),
            "cancelled" => Loc.T("Cancelled"),
            _ => Loc.T("Waiting"),
        };
        public bool IsDone => Status == "fulfilled";
        public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    }

    /// <summary>Game requests, through the SanctuaryVault service which passes them to the RomM-ComM bot.</summary>
    internal static class GameRequestsService
    {
        /// <summary>The signed-in player's requests, newest first; null when the server has no requests.</summary>
        public static async Task<List<GameRequestItem>?> ListAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{SanctuaryService.Current.TrimEnd('/')}/game-requests");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return null;
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<List<GameRequestItem>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        /// <summary>Asks for a game; true when it joined a request someone already made.</summary>
        public static async Task<bool> AskAsync(string game, string? details)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{SanctuaryService.Current.TrimEnd('/')}/game-requests")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { game, details }), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new InvalidOperationException(Loc.T("You already have many requests waiting: wait for some to be handled."));
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ErrorOf(body) ?? Loc.T("The request could not be sent."));
            using JsonDocument answer = JsonDocument.Parse(body);
            return answer.RootElement.TryGetProperty("result", out JsonElement result) && result.GetString() == "merged";
        }

        private static string? ErrorOf(string body)
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(body);
                return json.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() : null;
            }
            catch (JsonException) { return null; }
        }
    }
}
