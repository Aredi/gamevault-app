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
    /// <summary>A problem a player reported on a game, as the SanctuaryVault service keeps it.</summary>
    public sealed class ProblemReportItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        /// <summary>"pc" (a GameVault game) or "retro" (a RomM game of <see cref="Platform"/>).</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "pc";
        [JsonPropertyName("gameId")] public int GameId { get; set; }
        [JsonPropertyName("platform")] public string? Platform { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "other";
        [JsonPropertyName("details")] public string? Details { get; set; }
        [JsonPropertyName("context")] public string? Context { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("at")] public DateTime At { get; set; }
        [JsonPropertyName("resolvedAt")] public DateTime? ResolvedAt { get; set; }

        public bool IsRetro => Kind == "retro";
        public bool IsOpen => ResolvedAt == null;
        public string CategoryText => ReportsService.CategoryText(Category);
        public string StatusText => IsOpen ? Loc.T("Not handled yet") : Loc.T("Resolved");
        public string Header => IsRetro && Platform != null ? $"{Title} · {Platform}" : Title;
        public string Byline => $"{Username} · {At.ToLocalTime():g}";
        public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    }

    /// <summary>Problem reports, kept by the SanctuaryVault service for the administrators' console.</summary>
    internal static class ReportsService
    {
        /// <summary>The kinds of problems, as the service knows them, in the order of the window.</summary>
        public static readonly string[] Categories = { "launch", "match", "file", "media", "other" };

        public static string CategoryText(string category) => category switch
        {
            "launch" => Loc.T("The game does not start or crashes"),
            "match" => Loc.T("Wrong cover or wrong game matched"),
            "file" => Loc.T("Corrupted or incomplete file"),
            "media" => Loc.T("Sound, picture or controller"),
            _ => Loc.T("Other"),
        };

        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>All the reports for an administrator, else the player's; newest first, null when the server keeps none.</summary>
        public static async Task<List<ProblemReportItem>?> ListAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/reports");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return null;
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<List<ProblemReportItem>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        public static async Task SendAsync(string kind, int gameId, string? platform, string title, string category, string? details, string context)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Service}/reports")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { kind, gameId, platform, title, category, details, context }), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ErrorOf(await response.Content.ReadAsStringAsync()) ?? Loc.T("The report could not be sent."));
        }

        public static async Task ResolveAsync(string id)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Service}/reports/{Uri.EscapeDataString(id)}/resolve");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ErrorOf(await response.Content.ReadAsStringAsync()) ?? Loc.T("The report could not be sent."));
        }

        private static string? ErrorOf(string body)
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(body);
                return json.RootElement.TryGetProperty("error", out JsonElement error) ? Loc.T(error.GetString() ?? "") : null;
            }
            catch (JsonException) { return null; }
        }
    }
}
