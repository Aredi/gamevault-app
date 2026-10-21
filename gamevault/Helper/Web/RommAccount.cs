using GameVault.Core;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core.Retro;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>A save of the player's RomM (theirs, from any device or its web page).</summary>
    internal sealed class RommSave
    {
        public int Id { get; init; }
        public string FileName { get; init; } = "";
        public string? Slot { get; init; }
        public string? Emulator { get; init; }
        public DateTime UpdatedAt { get; init; }
        public long Size { get; init; }
        public string? Device { get; init; }
        /// <summary>The RomM device that sent it.</summary>
        public string? OriginDevice { get; init; }
        /// <summary>This PC sent it or took it: it is this PC's save.</summary>
        public bool IsCurrentHere { get; init; }
        public bool IsOurs => Slot is RetroCloudSaves.MainSlot or RetroCloudSaves.RestSlot;

        public static RommSave Read(JsonElement save) => new()
        {
            Id = save.GetProperty("id").GetInt32(),
            FileName = save.GetProperty("file_name").GetString() ?? "",
            Slot = save.TryGetProperty("slot", out JsonElement slot) && slot.ValueKind == JsonValueKind.String ? slot.GetString() : null,
            Emulator = save.TryGetProperty("emulator", out JsonElement emulator) && emulator.ValueKind == JsonValueKind.String ? emulator.GetString() : null,
            UpdatedAt = DateTime.Parse(save.GetProperty("updated_at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            Size = save.TryGetProperty("file_size_bytes", out JsonElement size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
            Device = save.TryGetProperty("device_syncs", out JsonElement syncs) && syncs.ValueKind == JsonValueKind.Array
                ? syncs.EnumerateArray().Where(s => s.TryGetProperty("is_current", out JsonElement current) && current.ValueKind == JsonValueKind.True)
                    .Select(s => s.GetProperty("device_name").GetString()).FirstOrDefault()
                : null,
            OriginDevice = save.TryGetProperty("origin_device_id", out JsonElement origin) && origin.ValueKind == JsonValueKind.String ? origin.GetString() : null,
            IsCurrentHere = RommAccount.DeviceId.Length > 0 && save.TryGetProperty("device_syncs", out JsonElement mine) && mine.ValueKind == JsonValueKind.Array
                && mine.EnumerateArray().Any(s => s.GetProperty("device_id").ToString() == RommAccount.DeviceId
                    && s.TryGetProperty("is_current", out JsonElement current) && current.ValueKind == JsonValueKind.True),
        };
    }

    /// <summary>
    /// The player's own RomM account, linked as RomM links its devices: the client shows a code, the player approves it
    /// on RomM's page, and the client gets a token for that account (no password typed here). With it, the retro saves
    /// go to the player's RomM (its web page's emulator uses them too); without it, to the SanctuaryVault service.
    /// </summary>
    internal static class RommAccount
    {
        /// <summary>What the client may do in the player's RomM: read the games, read and write their saves.</summary>
        private static readonly string[] Scopes = { "me.read", "roms.read", "assets.read", "assets.write", "devices.read", "devices.write" };

        private static string Config => LoginManager.Instance.GetUserProfile().UserConfigFile;
        public static string Token => Preferences.Get(AppConfigKey.RommToken, Config);
        public static string Username => Preferences.Get(AppConfigKey.RommUsername, Config);
        public static string DeviceId => Preferences.Get(AppConfigKey.RommDeviceId, Config);
        public static bool IsLinked => Token.Length > 0;

        /// <summary>Linked or unlinked: the pages say where the saves go.</summary>
        public static event Action? Changed;

        private static string? url;
        /// <summary>RomM as the players reach it, told by the SanctuaryVault service; null when the server has none.</summary>
        public static async Task<string?> UrlAsync()
        {
            if (url != null)
                return url;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{SanctuaryService.Current.TrimEnd('/')}/status");
                using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
                using JsonDocument status = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                url = status.RootElement.TryGetProperty("rommUrl", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.TrimEnd('/') : null;
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return url;
        }

        /// <summary>The tests point at their fake RomM.</summary>
        internal static void ForgetUrl() => url = null;

        /// <summary>A request to the player's RomM, with their token.</summary>
        public static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
        {
            string root = await UrlAsync() ?? throw new InvalidOperationException(Loc.T("RomM is not set up on this server."));
            using var request = new HttpRequestMessage(method, root + path) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            HttpResponseMessage response = await HttpClients.Shared.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Revoked or expired in RomM: the saves go to the service again until it is linked again
                Unlink();
                MainWindowViewModel.Instance.AppBarText = Loc.T("The link with your RomM account has ended: link it again in Settings > Retro.");
            }
            return response;
        }

        private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

        /// <summary>RomM's pairing: the code is shown, then the client waits for the player's approval.</summary>
        /// <param name="showCode">The code and the page where the player approves it (the caller opens it).</param>
        /// <returns>Null when linked, else why not.</returns>
        public static async Task<string?> LinkAsync(Action<string, string> showCode, CancellationToken cancellationToken)
        {
            if (await UrlAsync() is not string root)
                return Loc.T("RomM is not set up on this server.");
            JsonElement init;
            using (HttpResponseMessage started = await HttpClients.Shared.PostAsync($"{root}/api/auth/device/init", Json(new
            {
                client_device_identifier = RemoteInstalls.DeviceId(),
                name = Environment.MachineName,
                client = "SanctuaryVault",
                platform = PlatformInfo.IsWindows ? "Windows" : "Linux",
                client_version = typeof(RommAccount).Assembly.GetName().Version?.ToString(3),
                requested_scopes = Scopes,
            }), cancellationToken))
            {
                if (!started.IsSuccessStatusCode)
                    return Loc.F("RomM refused the link ({0}).", (int)started.StatusCode);
                init = JsonDocument.Parse(await started.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            }
            string deviceCode = init.GetProperty("device_code").GetString()!;
            string page = root + init.GetProperty("verification_path_complete").GetString();
            showCode(init.GetProperty("user_code").GetString()!, page);

            int interval = Math.Max(2, init.TryGetProperty("interval", out JsonElement every) ? every.GetInt32() : 5);
            DateTime deadline = DateTime.UtcNow.AddSeconds(init.TryGetProperty("expires_in", out JsonElement life) ? life.GetInt32() : 600);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
                using HttpResponseMessage answer = await HttpClients.Shared.PostAsync($"{root}/api/auth/device/token", Json(new { device_code = deviceCode }), cancellationToken);
                string body = await answer.Content.ReadAsStringAsync(cancellationToken);
                if (answer.IsSuccessStatusCode)
                {
                    using JsonDocument token = JsonDocument.Parse(body);
                    Preferences.Set(AppConfigKey.RommToken, token.RootElement.GetProperty("access_token").GetString()!, Config);
                    Preferences.Set(AppConfigKey.RommDeviceId, token.RootElement.TryGetProperty("device_id", out JsonElement device) ? device.ToString() : "", Config);
                    using (HttpResponseMessage me = await SendAsync(HttpMethod.Get, "/api/users/me"))
                        if (me.IsSuccessStatusCode)
                            Preferences.Set(AppConfigKey.RommUsername, JsonDocument.Parse(await me.Content.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("username").GetString() ?? "", Config);
                    Changed?.Invoke();
                    return null;
                }
                // Still waiting for the player (as RFC 8628 says), or refused
                if (body.Contains("denied", StringComparison.OrdinalIgnoreCase))
                    return Loc.T("The link was refused in RomM.");
                if (body.Contains("expired", StringComparison.OrdinalIgnoreCase))
                    break;
            }
            return Loc.T("The code expired before it was approved in RomM.");
        }

        #region Saves
        /// <summary>RomM keeps which saves each device sent or took (the devices of the saves, and this PC's): told on each call.</summary>
        private static string Device(char separator) => DeviceId.Length > 0 ? $"{separator}device_id={Uri.EscapeDataString(DeviceId)}" : "";

        /// <summary>The player's RomM saves of a game, the newest first.</summary>
        public static async Task<List<RommSave>> SavesAsync(int romId)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/api/saves?rom_id={romId}{Device('&')}");
            response.EnsureSuccessStatusCode();
            using JsonDocument saves = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return saves.RootElement.EnumerateArray().Select(RommSave.Read).OrderByDescending(s => s.UpdatedAt).ToList();
        }

        public static async Task<byte[]> SaveContentAsync(RommSave save)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/api/saves/{save.Id}/content{Device('?')}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <param name="slot">A stable name: RomM pairs each new save with the previous one of the slot, and keeps the last 10.</param>
        /// <remarks>
        /// Sent over the slot's newest even when another device sent one since this PC took it (RomM refuses it
        /// otherwise): the callers decided before (taken first, or asked), and the slot keeps the other one.
        /// </remarks>
        public static async Task<RommSave> UploadSaveAsync(int romId, string emulator, string slot, string fileName, byte[] content)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "saveFile", fileName);
            using HttpResponseMessage response = await SendAsync(HttpMethod.Post,
                $"/api/saves?rom_id={romId}&emulator={Uri.EscapeDataString(emulator)}&slot={slot}{Device('&')}&overwrite=true&autocleanup=true&autocleanup_limit=10", form);
            response.EnsureSuccessStatusCode();
            using JsonDocument saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return RommSave.Read(saved.RootElement);
        }

        private static readonly Dictionary<string, int?> pcGames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The game of RomM that is this file of the library: RomM and GameVault read the same folder (RomM's Windows
        /// platform is GameVault's files), so the file names are the same. Not one of the retro platforms, where a file
        /// of the same name is another game. Null when RomM does not have it.
        /// </summary>
        public static async Task<int?> PcGameIdAsync(string fileName)
        {
            if (pcGames.TryGetValue(fileName, out int? known))
                return known;
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get,
                $"/api/roms?search_term={Uri.EscapeDataString(System.IO.Path.GetFileNameWithoutExtension(fileName))}&limit=50");
            response.EnsureSuccessStatusCode();
            using JsonDocument page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            int? id = page.RootElement.GetProperty("items").EnumerateArray()
                .Where(r => string.Equals(r.GetProperty("fs_name").GetString(), fileName, StringComparison.OrdinalIgnoreCase)
                    && !RetroSystems.All.ContainsKey(r.GetProperty("platform_slug").GetString() ?? ""))
                .Select(r => (int?)r.GetProperty("id").GetInt32()).FirstOrDefault();
            return pcGames[fileName] = id;
        }
        #endregion

        public static void Unlink()
        {
            pcGames.Clear();
            foreach (AppConfigKey key in new[] { AppConfigKey.RommToken, AppConfigKey.RommUsername, AppConfigKey.RommDeviceId })
                Preferences.DeleteKey(key, Config);
            Changed?.Invoke();
        }
    }
}
