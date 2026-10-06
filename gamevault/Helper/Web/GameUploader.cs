using GameVault.Core;
using gamevault.Localization;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Sends a game archive to the GameVault Uploader next to the server: chunk by chunk, continuing an
    /// interrupted upload where it stopped. The uploader accepts it only from administrators of the server.
    /// </summary>
    internal static class GameUploader
    {
        public const int ChunkSize = 16 * 1024 * 1024;

        public record UploadState(long Received, bool Exists);

        private static string Url(string uploader, string path) => uploader.TrimEnd('/') + path;

        /// <summary>The uploader's version, or an explanation why it can't be used.</summary>
        public static async Task<(bool Ok, string Message)> CheckAsync(string uploader)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using JsonDocument status = JsonDocument.Parse(await client.GetStringAsync(Url(uploader, "/status")));
                string version = status.RootElement.GetProperty("version").GetString() ?? "?";
                long free = status.RootElement.TryGetProperty("freeSpace", out var space) ? space.GetInt64() : -1;
                return (true, free >= 0
                    ? Loc.F("GameVault Uploader {0} is ready, {1} free on the server", version, GameVault.Core.Storage.StorageCleanup.FormatSize(free))
                    : Loc.F("GameVault Uploader {0} is ready", version));
            }
            catch (Exception ex)
            {
                return (false, Loc.F("The uploader can not be reached at {0}: {1}", uploader, ex.Message));
            }
        }

        public static async Task<UploadState> GetStateAsync(string uploader, string name, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url(uploader, $"/uploads/{Uri.EscapeDataString(name)}"));
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, cancellationToken);
            await EnsureSuccess(response);
            using JsonDocument state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return new UploadState(state.RootElement.GetProperty("received").GetInt64(), state.RootElement.GetProperty("exists").GetBoolean());
        }

        public static async Task UploadAsync(string uploader, string file, string name, bool overwrite, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            long size = new FileInfo(file).Length;
            string escaped = Uri.EscapeDataString(name);
            long offset = (await GetStateAsync(uploader, name, cancellationToken)).Received;
            // Continuing an earlier attempt is only safe with the very same file
            string configFile = LoginManager.Instance.GetUserProfile().UserConfigFile;
            string key = "Upload" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uploader.TrimEnd('/') + "/" + name)))[..16];
            string fingerprint = $"{size}|{File.GetLastWriteTimeUtc(file).Ticks}|{Path.GetFullPath(file)}";
            if (offset > 0 && Preferences.Get(key, configFile) != fingerprint)
                offset = size + 1;// drop the other upload below
            Preferences.Set(key, fingerprint, configFile);
            if (offset > size)
            {
                // Left over from another file with the same name
                using var drop = new HttpRequestMessage(HttpMethod.Delete, Url(uploader, $"/uploads/{escaped}"));
                using (await WebHelper.SendLongAsync(drop, cancellationToken)) { }
                offset = 0;
            }
            if (offset > 0)
                Log.Info($"Upload of {name} continues at {offset} of {size} bytes");

            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
            byte[] buffer = new byte[ChunkSize];
            int attempts = 0;
            while (offset < size)
            {
                source.Position = offset;
                int length = await source.ReadAtLeastAsync(buffer, (int)Math.Min(ChunkSize, size - offset), false, cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Put, Url(uploader, $"/uploads/{escaped}?offset={offset}&total={size}"))
                {
                    Content = new ByteArrayContent(buffer, 0, length),
                };
                try
                {
                    using HttpResponseMessage response = await WebHelper.SendLongAsync(request, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        // The uploader has another position (a chunk arrived partly): continue there
                        offset = await ReceivedFrom(response);
                        continue;
                    }
                    await EnsureSuccess(response);
                    offset = await ReceivedFrom(response);
                    attempts = 0;
                    progress?.Report((double)offset / size);
                }
                catch (Exception ex) when (IsTemporary(ex) && ++attempts <= 5 && !cancellationToken.IsCancellationRequested)
                {
                    // Lost connection: wait a little, ask where the upload stands, continue
                    Log.Ignored(ex);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempts), cancellationToken);
                    offset = (await GetStateAsync(uploader, name, cancellationToken)).Received;
                }
            }
            using var complete = new HttpRequestMessage(HttpMethod.Post, Url(uploader, $"/uploads/{escaped}/complete?size={size}&overwrite={(overwrite ? "true" : "false")}"));
            using HttpResponseMessage done = await WebHelper.SendLongAsync(complete, cancellationToken);
            await EnsureSuccess(done);
            Preferences.DeleteKey(key, configFile);
        }

        /// <summary>A lost connection or a passing server error, not a refusal (not an administrator, disk full).</summary>
        private static bool IsTemporary(Exception ex) => ex is IOException
            || ex is HttpRequestException http && (http.StatusCode == null || ((int)http.StatusCode >= 500 && http.StatusCode != HttpStatusCode.InsufficientStorage));

        private static async Task<long> ReceivedFrom(HttpResponseMessage response)
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("received").GetInt64();
        }

        /// <summary>The uploader's own explanation ("not an administrator", "not enough free space") as the error.</summary>
        private static async Task EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;
            string message = response.ReasonPhrase ?? response.StatusCode.ToString();
            try
            {
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (body.RootElement.TryGetProperty("error", out var error))
                    message = error.GetString() ?? message;
            }
            catch (JsonException) { }
            throw new HttpRequestException(Loc.F("Uploader: {0}", message), null, response.StatusCode);
        }
    }
}
