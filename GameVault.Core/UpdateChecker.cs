using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace GameVault.Core
{
    public record ReleaseInfo(string Version, string PageUrl, string? DownloadUrl);

    public static class UpdateChecker
    {
        /// <summary>
        /// Returns the latest release of <see cref="AppRepository"/> if it is newer than <paramref name="currentVersion"/>.
        /// Having no release yet, a missing repository or the GitHub rate limit are not errors: there is simply no update.
        /// </summary>
        public static async Task<ReleaseInfo?> GetNewerReleaseAsync(string currentVersion, string? assetNameContains = null, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AppRepository.ReleasesApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using HttpResponseMessage response = await HttpClients.Shared.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                Log.Info($"Update check skipped: GitHub answered {(int)response.StatusCode}");
                return null;
            }
            response.EnsureSuccessStatusCode();
            return ParseReleaseList(await response.Content.ReadAsStringAsync(cancellationToken), currentVersion, assetNameContains);
        }

        /// <summary>
        /// Picks the newest published (not draft, not pre-release) release of a GitHub release list.
        /// </summary>
        internal static ReleaseInfo? ParseReleaseList(string json, string currentVersion, string? assetNameContains)
        {
            if (JsonNode.Parse(json) is not JsonArray releases)
                return null;
            foreach (JsonNode? release in releases)
            {
                if (release == null || release["draft"]?.GetValue<bool>() == true || release["prerelease"]?.GetValue<bool>() == true)
                    continue;
                return ParseRelease(release.ToJsonString(), currentVersion, assetNameContains);
            }
            return null;
        }

        internal static ReleaseInfo? ParseRelease(string json, string currentVersion, string? assetNameContains)
        {
            JsonNode? release = JsonNode.Parse(json);
            string? tag = release?["tag_name"]?.GetValue<string>();
            if (tag == null || !VersionHelper.IsNewer(tag, currentVersion))
                return null;

            string page = release?["html_url"]?.GetValue<string>() ?? AppRepository.ReleasesPage;
            string? download = null;
            if (release?["assets"] is JsonArray assets)
            {
                foreach (JsonNode? asset in assets)
                {
                    string? name = asset?["name"]?.GetValue<string>();
                    if (name != null && (assetNameContains == null || name.Contains(assetNameContains, StringComparison.OrdinalIgnoreCase)))
                    {
                        download = asset?["browser_download_url"]?.GetValue<string>();
                        break;
                    }
                }
            }
            return new ReleaseInfo(tag, page, download);
        }
    }
}
