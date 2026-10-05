using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace GameVault.Core.Compatibility
{
    public enum ToolFlavor
    {
        /// <summary>GloriousEggroll/proton-ge-custom: Proton with extra fixes, the usual choice on the Steam Deck.</summary>
        GeProton,
        /// <summary>Kron4ek/Wine-Builds: upstream Wine.</summary>
        Wine,
        /// <summary>Kron4ek/Wine-Builds: Wine with the Staging patch set.</summary>
        WineStaging,
    }

    /// <param name="FolderName">Name of the folder the archive extracts to.</param>
    /// <param name="ChecksumUrl">File listing the archive's checksum (sha512sum file or sha256sums.txt).</param>
    public record DownloadableTool(ToolFlavor Flavor, string Version, string FolderName, string ArchiveName, string DownloadUrl, long Size, string? ChecksumUrl)
    {
        public string SizeText => $"{Size / 1024d / 1024d:0} MB";
        public override string ToString() => $"{Version} ({SizeText})";
    }

    public static class ToolCatalog
    {
        public static string ReleasesApi(ToolFlavor flavor) => flavor == ToolFlavor.GeProton
            ? "https://api.github.com/repos/GloriousEggroll/proton-ge-custom/releases?per_page=25"
            : "https://api.github.com/repos/Kron4ek/Wine-Builds/releases?per_page=25";

        public static string DisplayName(ToolFlavor flavor) => flavor switch
        {
            ToolFlavor.GeProton => "GE-Proton",
            ToolFlavor.Wine => "Wine",
            _ => "Wine Staging",
        };

        public static async Task<List<DownloadableTool>> GetAvailableAsync(ToolFlavor flavor, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi(flavor));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using HttpResponseMessage response = await HttpClients.Shared.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return Parse(flavor, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        /// <summary>
        /// Picks the x86_64 archive of each published release. Wine builds use the WoW64 variant
        /// (64-bit Wine that also runs 32-bit games without 32-bit system libraries).
        /// </summary>
        internal static List<DownloadableTool> Parse(ToolFlavor flavor, string releasesJson)
        {
            var result = new List<DownloadableTool>();
            if (JsonNode.Parse(releasesJson) is not JsonArray releases)
                return result;
            foreach (JsonNode? release in releases)
            {
                if (release == null || release["draft"]?.GetValue<bool>() == true || release["prerelease"]?.GetValue<bool>() == true)
                    continue;
                string tag = release["tag_name"]?.GetValue<string>() ?? "";
                var assets = (release["assets"] as JsonArray)?
                    .Where(a => a != null)
                    .Select(a => (Name: a!["name"]?.GetValue<string>() ?? "", Url: a["browser_download_url"]?.GetValue<string>() ?? "", Size: a["size"]?.GetValue<long>() ?? 0))
                    .ToList() ?? new();

                (string Name, string Url, long Size) archive = default;
                string? checksum = null;
                if (flavor == ToolFlavor.GeProton)
                {
                    archive = assets.FirstOrDefault(a => a.Name.EndsWith(".tar.gz") && !a.Name.Contains("aarch64"));
                    if (archive.Name != null)
                        checksum = assets.FirstOrDefault(a => a.Name == archive.Name.Replace(".tar.gz", ".sha512sum")).Url;
                }
                else
                {
                    string expected = flavor == ToolFlavor.Wine ? $"wine-{tag}-amd64-wow64.tar.xz" : $"wine-{tag}-staging-amd64-wow64.tar.xz";
                    archive = assets.FirstOrDefault(a => a.Name == expected);
                    checksum = assets.FirstOrDefault(a => a.Name == "sha256sums.txt").Url;
                }
                if (string.IsNullOrEmpty(archive.Name))
                    continue;
                string folder = archive.Name.Replace(".tar.gz", "").Replace(".tar.xz", "");
                // GE-Proton archives are named after the release, newer ones carry an architecture suffix
                if (flavor == ToolFlavor.GeProton)
                    folder = tag;
                result.Add(new DownloadableTool(flavor, tag, folder, archive.Name, archive.Url, archive.Size, string.IsNullOrEmpty(checksum) ? null : checksum));
            }
            return result;
        }

        /// <summary>
        /// Finds the expected checksum of <paramref name="archiveName"/> in a "hash  file" list
        /// (sha512sum files of GE-Proton, sha256sums.txt of the Wine builds).
        /// </summary>
        internal static string? FindChecksum(string checksumFile, string archiveName)
        {
            foreach (string line in checksumFile.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && Path.GetFileName(parts[1].TrimStart('*').Trim()) == archiveName)
                    return parts[0].ToLowerInvariant();
            }
            return null;
        }

        /// <summary>sha256 or sha512 depending on the length of <paramref name="expected"/>.</summary>
        public static async Task<bool> VerifyAsync(string file, string expected, CancellationToken cancellationToken = default)
        {
            await using FileStream stream = File.OpenRead(file);
            byte[] hash = expected.Length == 128
                ? await SHA512.HashDataAsync(stream, cancellationToken)
                : await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Downloads, verifies and extracts <paramref name="tool"/> into <paramref name="targetDirectory"/>.
        /// Returns the folder of the installed tool.
        /// </summary>
        public static async Task<string> InstallAsync(DownloadableTool tool, string targetDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(targetDirectory);
            string work = Path.Combine(targetDirectory, $".download-{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);
            try
            {
                string archive = Path.Combine(work, tool.ArchiveName);
                using (HttpResponseMessage response = await HttpClients.Shared.GetAsync(tool.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? tool.Size;
                    await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using FileStream target = File.Create(archive);
                    byte[] buffer = new byte[1 << 16];
                    long done = 0;
                    int read;
                    while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        done += read;
                        if (total > 0)
                            progress?.Report(done * 0.9 / total);
                    }
                }

                if (tool.ChecksumUrl != null)
                {
                    string list = await HttpClients.Shared.GetStringAsync(tool.ChecksumUrl, cancellationToken);
                    string? expected = FindChecksum(list, tool.ArchiveName);
                    if (expected != null && !await VerifyAsync(archive, expected, cancellationToken))
                        throw new InvalidDataException($"Checksum mismatch for {tool.ArchiveName}, the download is corrupted.");
                }

                // tar handles .tar.gz and .tar.xz; extracting next to the target keeps the move atomic
                string extract = Path.Combine(work, "extract");
                Directory.CreateDirectory(extract);
                var tar = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("tar")
                {
                    ArgumentList = { "-xf", archive, "-C", extract },
                    UseShellExecute = false,
                    RedirectStandardError = true,
                }) ?? throw new InvalidOperationException("tar could not be started");
                string error = await tar.StandardError.ReadToEndAsync(cancellationToken);
                await tar.WaitForExitAsync(cancellationToken);
                if (tar.ExitCode != 0)
                    throw new InvalidOperationException($"Extracting {tool.ArchiveName} failed: {error.Trim()}");

                string[] extracted = Directory.GetDirectories(extract);
                string source = extracted.Length == 1 ? extracted[0] : extract;
                string destination = Path.Combine(targetDirectory, tool.FolderName);
                if (Directory.Exists(destination))
                    Directory.Delete(destination, true);
                Directory.Move(source, destination);
                progress?.Report(1);
                return destination;
            }
            finally
            {
                try { Directory.Delete(work, true); }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }
    }
}
