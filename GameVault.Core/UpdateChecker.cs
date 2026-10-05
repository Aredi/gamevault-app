using System.Text.Json.Nodes;

namespace GameVault.Core
{
    public record ReleaseInfo(string Version, string PageUrl, string? DownloadUrl);

    public static class UpdateChecker
    {
        /// <summary>
        /// Returns the latest release of <see cref="AppRepository"/> if it is newer than <paramref name="currentVersion"/>.
        /// </summary>
        public static async Task<ReleaseInfo?> GetNewerReleaseAsync(string currentVersion, string? assetNameContains = null, CancellationToken cancellationToken = default)
        {
            string json = await HttpClients.Shared.GetStringAsync(AppRepository.LatestReleaseApi, cancellationToken);
            return ParseRelease(json, currentVersion, assetNameContains);
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
