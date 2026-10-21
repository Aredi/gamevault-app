using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// The games of the other platforms (NES, GBA...) are in RomM: the SanctuaryVault clients see them through here
    /// with their GameVault sign-in, and the service reads RomM with its own account (a RomM viewer is enough).
    /// </summary>
    public sealed partial class RommProxy(HttpClient romm, UploaderOptions options, ILogger<RommProxy> logger)
    {
        public bool Enabled => !string.IsNullOrEmpty(options.RommUrl) && !string.IsNullOrEmpty(options.RommUsername);

        /// <summary>
        /// The pictures and manuals RomM keeps for a game (roms/&lt;platform&gt;/&lt;rom&gt;/cover/big.png, …/screenshots/0.jpg,
        /// …/manual/523.pdf): nothing else of RomM is reachable this way.
        /// </summary>
        [GeneratedRegex(@"^roms/\d+/\d+/(cover/(small|big)\.(png|jpe?g|webp)|screenshots/\d+\.(png|jpe?g|webp)|manual/\d+\.pdf)$")]
        private static partial Regex MediaPath();

        [GeneratedRegex(@"roms/\d+/\d+/(cover|screenshots|manual)/[A-Za-z0-9]+\.[a-z]+")]
        private static partial Regex MediaInRomm();

        /// <summary>A path of RomM's pictures and manuals, as the games list gives them.</summary>
        public static bool IsMedia(string? path) => path != null && MediaPath().IsMatch(path);

        private static string? Media(string? path) => MediaInRomm().Match(path ?? "") is { Success: true } m && MediaPath().IsMatch(m.Value) ? m.Value : null;

        private static string[] Texts(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();

        private static double? Decimal(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

        public sealed class RommException(string message) : Exception(message);

        private HttpRequestMessage Get(string path)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.RommUsername}:{options.RommPassword}")));
            return request;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await romm.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RomM could not be reached");
                throw new RommException("RomM could not be reached.");
            }
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                response.Dispose();
                logger.LogWarning("RomM refused the account {User} (ROMM_USERNAME, ROMM_PASSWORD)", options.RommUsername);
                throw new RommException("RomM refused the account of the SanctuaryVault service.");
            }
            return response;
        }

        private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SendAsync(Get(path), cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new RommException($"RomM answered {(int)response.StatusCode}.");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        /// <summary>RomM's details of a rom or BIOS (the name its download address ends with); null when RomM has no such id.</summary>
        private async Task<JsonElement?> DetailsAsync(string path, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SendAsync(Get(path), cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new RommException($"RomM answered {(int)response.StatusCode}.");
            using JsonDocument details = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return details.RootElement.Clone();
        }

        private static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

        private static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static long Number(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

        /// <summary>The platforms that have games; the client keeps the ones it has an emulator for.</summary>
        public async Task<object> PlatformsAsync(CancellationToken cancellationToken)
        {
            using JsonDocument platforms = await GetJsonAsync("api/platforms", cancellationToken);
            return platforms.RootElement.EnumerateArray()
                .Where(p => Number(p, "rom_count") > 0)
                .Select(p => new { id = Number(p, "id"), slug = Text(p, "slug"), name = Text(p, "display_name") ?? Text(p, "name"), count = Number(p, "rom_count") })
                .ToList();
        }

        public async Task<object> GamesAsync(int platformId, CancellationToken cancellationToken)
        {
            using JsonDocument page = await GetJsonAsync($"api/roms?platform_ids={platformId}&limit=10000&order_by=name", cancellationToken);
            return page.RootElement.GetProperty("items").EnumerateArray().Select(r =>
            {
                JsonElement about = r.TryGetProperty("metadatum", out JsonElement metadatum) ? metadatum : default;
                JsonElement hltb = r.TryGetProperty("hltb_metadata", out JsonElement howLong) ? howLong : default;
                return new
                {
                    id = Number(r, "id"),
                    name = Text(r, "name") ?? Text(r, "fs_name_no_ext"),
                    fileName = Text(r, "fs_name"),
                    size = Number(r, "fs_size_bytes"),
                    cover = Media(Text(r, "path_cover_large") ?? Text(r, "path_cover_small")),
                    summary = Text(r, "summary"),
                    // What the library shows of a game: picked one by one (RomM's metadata also hold its providers' keys)
                    released = Decimal(about, "first_release_date") is double date ? (long?)date : null,
                    rating = Decimal(about, "average_rating"),
                    genres = Texts(about, "genres"),
                    companies = Texts(about, "companies"),
                    franchises = Texts(about, "franchises"),
                    modes = Texts(about, "game_modes"),
                    ageRatings = Texts(about, "age_ratings"),
                    // RomM writes "null" when it does not know
                    players = about.ValueKind == JsonValueKind.Object && Text(about, "player_count") is string players && players != "null" ? players : null,
                    regions = Texts(r, "regions"),
                    languages = Texts(r, "languages"),
                    screenshots = r.TryGetProperty("merged_screenshots", out JsonElement shots) && shots.ValueKind == JsonValueKind.Array
                        ? shots.EnumerateArray().Select(s => Media(s.GetString())).OfType<string>().Take(12).ToArray()
                        : Array.Empty<string>(),
                    manual = Media(Text(r, "path_manual")),
                    video = Text(r, "youtube_video_id"),
                    // Seconds to finish the main story, from HowLongToBeat
                    mainStory = Decimal(hltb, "main_story") is double story && story > 0 ? (long?)story : null,
                    // Several files: RomM sends them as one zip, which the client unpacks
                    multi = r.TryGetProperty("has_multiple_files", out JsonElement multi) && multi.ValueKind == JsonValueKind.True,
                };
            }).ToList();
        }

        /// <summary>The BIOS RomM keeps for a platform.</summary>
        public async Task<object> FirmwareAsync(int platformId, CancellationToken cancellationToken)
        {
            using JsonDocument firmware = await GetJsonAsync($"api/firmware?platform_id={platformId}", cancellationToken);
            return firmware.RootElement.EnumerateArray()
                .Where(f => !(f.TryGetProperty("missing_from_fs", out JsonElement missing) && missing.ValueKind == JsonValueKind.True))
                .Select(f => new { id = Number(f, "id"), fileName = Text(f, "file_name"), size = Number(f, "file_size_bytes") })
                .ToList();
        }

        public async Task FirmwareContentAsync(int id, HttpResponse response, CancellationToken cancellationToken)
        {
            string? name = await DetailsAsync($"api/firmware/{id}", cancellationToken) is JsonElement details ? Text(details, "file_name") : null;
            if (string.IsNullOrEmpty(name))
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            using HttpResponseMessage file = await SendAsync(Get($"api/firmware/{id}/content/{Uri.EscapeDataString(name)}"), cancellationToken);
            await CopyAsync(file, response, cancellationToken);
        }

        public async Task MediaAsync(string path, HttpResponse response, CancellationToken cancellationToken)
        {
            if (!MediaPath().IsMatch(path))
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            using HttpResponseMessage cover = await SendAsync(Get($"assets/romm/resources/{path}"), cancellationToken);
            await CopyAsync(cover, response, cancellationToken);
        }

        /// <summary>The rom's file, in parts when asked (Range), so a download can continue.</summary>
        public async Task ContentAsync(int id, HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
        {
            JsonElement? rom = await DetailsAsync($"api/roms/{id}", cancellationToken);
            string? name = rom is JsonElement details ? Text(details, "fs_name") : null;
            if (string.IsNullOrEmpty(name))
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            HttpRequestMessage content = Get($"api/roms/{id}/content/{Uri.EscapeDataString(name)}");
            // A game in several files or a folder (a PS3 game) comes as a zip RomM makes as it sends it: asked a part of
            // it, RomM reads all the files first (a PS3 game's 7 GB: the request times out). Sent whole instead.
            bool zipped = Flag(rom!.Value, "has_multiple_files") || Flag(rom.Value, "has_nested_single_file");
            if (request.Headers.Range.Count > 0 && !zipped)
                content.Headers.TryAddWithoutValidation("Range", request.Headers.Range.ToString());
            using HttpResponseMessage file = await SendAsync(content, cancellationToken);
            // RomM names what it sends: the file in a game's folder (Wii), or the zip of a game in several files
            response.Headers.ContentDisposition = (file.Content.Headers.ContentDisposition ?? new ContentDispositionHeaderValue("attachment") { FileNameStar = name }).ToString();
            // The client's download limit (Settings), in kB/s as the GameVault server takes it
            long limit = long.TryParse(request.Headers["X-Download-Speed-Limit"], out long kilobytes) && kilobytes > 0 ? kilobytes * 1000 : 0;
            await CopyAsync(file, response, cancellationToken, limit);
        }

        /// <param name="bytesPerSecond">0: as fast as it goes.</param>
        private static async Task CopyAsync(HttpResponseMessage from, HttpResponse to, CancellationToken cancellationToken, long bytesPerSecond = 0)
        {
            to.StatusCode = (int)from.StatusCode;
            if (!from.IsSuccessStatusCode)
                return;
            to.ContentType = from.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            to.ContentLength = from.Content.Headers.ContentLength;
            if (from.Content.Headers.ContentRange != null)
                to.Headers.ContentRange = from.Content.Headers.ContentRange.ToString();
            if (from.Headers.AcceptRanges.Count > 0)
                to.Headers.AcceptRanges = from.Headers.AcceptRanges.ToString();
            if (bytesPerSecond <= 0)
            {
                await from.Content.CopyToAsync(to.Body, cancellationToken);
                return;
            }
            await using Stream source = await from.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = new byte[Math.Clamp(bytesPerSecond / 10, 4096, 1 << 20)];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long sent = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await to.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                sent += read;
                // Ahead of the allowed pace: wait until the sent bytes match it
                TimeSpan due = TimeSpan.FromSeconds((double)sent / bytesPerSecond) - clock.Elapsed;
                if (due > TimeSpan.Zero)
                    await Task.Delay(due, cancellationToken);
            }
        }

        /// <summary>The games added to RomM last, for the new games notification.</summary>
        public async Task<object> LatestAsync(CancellationToken cancellationToken)
        {
            using JsonDocument page = await GetJsonAsync("api/roms?order_by=id&order_dir=desc&limit=30", cancellationToken);
            return page.RootElement.GetProperty("items").EnumerateArray().Select(r => new
            {
                id = Number(r, "id"),
                name = Text(r, "name") ?? Text(r, "fs_name_no_ext"),
                platform = Text(r, "platform_slug"),
                platformName = Text(r, "platform_display_name") ?? Text(r, "platform_custom_name"),
            }).ToList();
        }
    }
}
