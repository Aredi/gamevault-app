using GameVault.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>One of the cloud saves the server keeps for a game (the newest first).</summary>
    public sealed class SaveVersion
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Upload time, in milliseconds since 1970.</summary>
        [JsonPropertyName("savedAt")]
        public long SavedAt { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>The installation (computer) that uploaded it.</summary>
        [JsonPropertyName("installationId")]
        public string InstallationId { get; set; } = "";

        public DateTime SavedAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(SavedAt).UtcDateTime;
    }

    /// <summary>The older cloud saves, read by the SanctuaryVault service from the server's savefiles volume.</summary>
    internal static class SaveHistoryService
    {
        /// <summary>The saves of the signed-in player for a game; null when the history is not available.</summary>
        public static async Task<List<SaveVersion>?> ListAsync(int gameId, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{SanctuaryService.Current.TrimEnd('/')}/savefiles/{gameId}");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                return null;
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<List<SaveVersion>>(await response.Content.ReadAsStringAsync(cancellationToken)) ?? new();
        }

        public static async Task DownloadAsync(int gameId, string name, string targetFile)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{SanctuaryService.Current.TrimEnd('/')}/savefiles/{gameId}/{Uri.EscapeDataString(name)}");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            await using Stream content = await response.Content.ReadAsStreamAsync();
            await using var file = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            await content.CopyToAsync(file);
        }
    }
}
