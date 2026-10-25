using GameVault.Core;
using GameVault.Core.Integrations;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Sunshine on this PC (Apollo, its Windows fork, on Windows; installed once by its player): it streams this screen to
    /// the room's members, who play with their own gamepads. Through its local API (its login kept for the player), the
    /// members' Moonlight is paired with the PIN typed for the player, while the screen is shared.
    /// </summary>
    internal static class SunshineHost
    {
        public static string Name => PlatformInfo.IsWindows ? "Apollo" : "Sunshine";
        public const string ApolloVersion = "0.4.6";
        public static string WindowsInstaller => $"https://github.com/ClassicOldSong/Apollo/releases/download/v{ApolloVersion}/Apollo-{ApolloVersion}.exe";
        public const string LinuxGuide = "https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html";

        /// <summary>Sunshine's API; the tests' fake one.</summary>
        internal static string ApiUrl { get; set; } = "https://127.0.0.1:47990";
        /// <summary>Its page, where its login is chosen the first time.</summary>
        public static string WebPage => "https://localhost:47990";

        // Its certificate is its own, made at its installation: trusted on this PC only, the API being this PC's
        private static readonly HttpClient http = new(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { Timeout = TimeSpan.FromSeconds(30) };

        private static string ConfigFile => LoginManager.Instance.GetUserProfile().UserConfigFile;

        public static (string User, string Password)? Login
        {
            get
            {
                try
                {
                    string user = Preferences.Get(AppConfigKey.SunshineUser, ConfigFile), password = Preferences.Get(AppConfigKey.SunshinePassword, ConfigFile, true);
                    return user.Length > 0 ? (user, password) : null;
                }
                catch (Exception ex) { Log.Ignored(ex); return null; }
            }
            private set
            {
                Preferences.Set(AppConfigKey.SunshineUser, value?.User ?? "", ConfigFile);
                Preferences.Set(AppConfigKey.SunshinePassword, value?.Password ?? "", ConfigFile, true);
            }
        }

        private static async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null, (string User, string Password)? login = null)
        {
            var (user, password) = login ?? Login ?? throw new InvalidOperationException(Loc.F("Give SanctuaryVault your {0} login first.", Name));
            using var request = new HttpRequestMessage(method, $"{ApiUrl}/{path}")
            {
                Content = body == null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
            using HttpResponseMessage response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(Loc.F("{0} did not accept this login.", Name));
            response.EnsureSuccessStatusCode();
            string text = await response.Content.ReadAsStringAsync();
            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        }

        /// <summary>Sunshine answers on this PC (its page needs no login).</summary>
        public static async Task<bool> RunningAsync()
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync($"{ApiUrl}/api/configLocale");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
        }

        /// <summary>Running, and its login known and accepted.</summary>
        public static async Task<bool> ReadyAsync()
        {
            if (Login == null || !await RunningAsync())
                return false;
            try { await SendAsync(HttpMethod.Get, "api/apps"); return true; }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException) { return false; }
        }

        /// <summary>Kept once Sunshine accepted it.</summary>
        public static async Task SaveLoginAsync(string user, string password)
        {
            await SendAsync(HttpMethod.Get, "api/apps", login: (user, password));
            Login = (user, password);
        }

        /// <summary>
        /// Windows: Apollo's official installer, with its choices shown (its virtual gamepads' driver among them) and the
        /// administrator's rights.
        /// </summary>
        public static async Task InstallAsync(Action<string> status)
        {
            string installer = Path.Combine(Path.GetTempPath(), "Apollo-installer.exe");
            await RetroArchManager.DownloadAsync(WindowsInstaller, installer, done => status(Loc.F("Downloading {0}... {1:0}%", Name, done * 100)));
            status(Loc.F("Installing {0}: accept Windows' prompt, then follow its installer...", Name));
            Process process;
            try { process = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" }) ?? throw new InvalidOperationException(Loc.F("{0} could not be installed.", Name)); }
            catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException(Loc.T("The administrator's rights were not given.")); }
            using (process)
                await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(Loc.F("{0} could not be installed.", Name));
        }

        private static CancellationTokenSource? sharing;

        /// <summary>The room this PC shares its screen with, while it does.</summary>
        public static string? SharedRoom { get; private set; }

        public static void Start(string roomId)
        {
            Stop();
            SharedRoom = roomId;
            sharing = new CancellationTokenSource();
            _ = PairMembersAsync(roomId, sharing.Token);
        }

        public static void Stop()
        {
            sharing?.Cancel();
            sharing = null;
            SharedRoom = null;
        }

        /// <summary>
        /// Every 2 seconds, the pairings asked to Sunshine by the room's members get the room's PIN; the others wait for the
        /// player on Sunshine's page. Apollo does not list them: the PIN goes to the one asked first, whoever asks (a
        /// stranger's Moonlight types its own PIN: refused), and the members paired get the gamepads Apollo does not give
        /// them. The room read again every 15 seconds, for its new members.
        /// shortcut: the members paired stay so (Sunshine's page removes them); unpair them as the sharing stops if asked.
        /// </summary>
        private static async Task PairMembersAsync(string roomId, CancellationToken ct)
        {
            RoomItem? room = null;
            DateTime read = DateTime.MinValue;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (DateTime.UtcNow - read > TimeSpan.FromSeconds(15))
                    {
                        room = (await RoomsService.ListAsync()).FirstOrDefault(r => r.Id == roomId);
                        read = DateTime.UtcNow;
                    }
                    if (room?.Code is string code && await PairingsAsync() is JsonElement pairings)
                        foreach (JsonElement pairing in pairings.EnumerateArray())
                        {
                            if (ScreenSharing.MemberAddress(pairing.GetProperty("address").GetString() ?? "", room.Members.Select(m => m.Ip)) is not string ip)
                                continue;
                            string name = room.Members.First(m => m.Ip == ip).UserName;
                            await SendAsync(HttpMethod.Post, "api/pin", new { pairing_id = pairing.GetProperty("id").GetString(), pin = ScreenSharing.Pin(code), name = $"SanctuaryVault · {name}" });
                        }
                    else if (room?.Code is string apolloCode)
                    {
                        await SendAsync(HttpMethod.Post, "api/pin", new { pin = ScreenSharing.Pin(apolloCode), name = PairedName });
                        await GiveGamepadsAsync();
                    }
                }
                catch (Exception ex) { Log.Ignored(ex); }
                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { }
            }
        }

        /// <summary>The pairings Moonlight asks Sunshine for; null for Apollo, that has no list of them.</summary>
        private static async Task<JsonElement?> PairingsAsync()
        {
            try { return (await SendAsync(HttpMethod.Get, "api/pin")).GetProperty("pairings"); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
        }

        private const string PairedName = "SanctuaryVault";
        // Apollo's permissions: the gamepad's input, then listing, watching and starting its apps
        private const uint Gamepad = 0x100, Apps = 0x7000000;

        /// <summary>Apollo's members paired here: their gamepads allowed (Apollo lets its later devices only watch).</summary>
        private static async Task GiveGamepadsAsync()
        {
            foreach (JsonElement client in (await SendAsync(HttpMethod.Get, "api/clients/list")).GetProperty("named_certs").EnumerateArray())
            {
                uint perm = client.GetProperty("perm").GetUInt32();
                if (!(client.GetProperty("name").GetString() ?? "").StartsWith(PairedName) || (perm & (Gamepad | Apps)) == (Gamepad | Apps))
                    continue;
                // Its other settings sent back as they are: Apollo resets the ones missing
                JsonObject update = JsonNode.Parse(client.GetRawText())!.AsObject();
                update.Remove("cert");
                update["perm"] = perm | Gamepad | Apps;
                await SendAsync(HttpMethod.Post, "api/clients/update", update);
            }
        }
    }
}
