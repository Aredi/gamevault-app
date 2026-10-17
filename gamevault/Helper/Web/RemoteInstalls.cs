using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace gamevault.Helper
{
    /// <summary>
    /// "Install on my PC", asked from the SanctuaryVault web page (a phone...). Every minute this PC says it is open,
    /// so the player can choose it on the page, then takes the games asked for it: each download is started without
    /// asking, and the page is told whether it started or why not.
    /// </summary>
    internal static class RemoteInstalls
    {
        private sealed class Entry
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";
            [JsonPropertyName("gameId")]
            public int GameId { get; set; }
            [JsonPropertyName("title")]
            public string Title { get; set; } = "";
        }

        private static Timer? timer;
        private static readonly SemaphoreSlim checking = new(1, 1);

        public static void Start()
        {
            timer ??= new Timer(async _ => await CheckAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1));
        }

        /// <summary>The id of this PC for the signed-in profile, made the first time.</summary>
        public static string DeviceId()
        {
            string configFile = LoginManager.Instance.GetUserProfile().UserConfigFile;
            string id = Preferences.Get(AppConfigKey.RemoteDeviceId, configFile);
            if (id.Length == 0)
            {
                id = Guid.NewGuid().ToString("N");
                Preferences.Set(AppConfigKey.RemoteDeviceId, id, configFile);
            }
            return id;
        }

        private static string Service => SanctuaryService.Current.TrimEnd('/');

        public static async Task CheckAsync()
        {
            if (!LoginManager.Instance.IsLoggedIn() || !await checking.WaitAsync(0))
                return;
            try
            {
                string device = DeviceId();
                using (var seen = new HttpRequestMessage(HttpMethod.Post, $"{Service}/remote/devices") { Content = Json(new { id = device, name = Environment.MachineName }) })
                using (HttpResponseMessage answer = await WebHelper.SendLongAsync(seen, CancellationToken.None))
                    if (!answer.IsSuccessStatusCode)
                        return;// a service without remote installs, or out of reach: next minute

                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/remote/installs?device={device}");
                using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
                if (!response.IsSuccessStatusCode)
                    return;
                var entries = JsonSerializer.Deserialize<List<Entry>>(await response.Content.ReadAsStringAsync()) ?? new();
                foreach (Entry entry in entries)
                {
                    string? failure;
                    try
                    {
                        failure = await StartAsync(entry);
                    }
                    catch (Exception ex)
                    {
                        // Told to the page too: one game that fails does not keep the others waiting
                        Log.Ignored(ex);
                        failure = ex.Message;
                    }
                    using var report = new HttpRequestMessage(HttpMethod.Put, $"{Service}/remote/installs/{entry.Id}")
                    {
                        Content = Json(new { status = failure == null ? "started" : "failed", message = failure }),
                    };
                    using HttpResponseMessage _ = await WebHelper.SendLongAsync(report, CancellationToken.None);
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            finally { checking.Release(); }
        }

        private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

        /// <returns>Null when the download started, else why not (shown on the web page).</returns>
        private static async Task<string?> StartAsync(Entry entry)
        {
            Game? game = null;
            try
            {
                game = JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{entry.GameId}"));
            }
            catch (Exception ex) { Log.Ignored(ex); }
            if (game == null || game.ID != entry.GameId)
            {
                Log.Info($"Remote install of game {entry.GameId} ({entry.Title}): not on the server any more");
                return Loc.T("This game is no longer on the server");
            }
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == game.ID))
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' asked from the web page is already installed", game.Title);
                    return Loc.T("It is already installed on this PC");
                }
                string? failure = await MainWindowViewModel.Instance.Downloads.TryStartDownload(game, unattended: true);
                if (failure == null)
                    ToastMessageHelper.CreateToastMessage(Loc.T("Install asked from the web page"), game.Title);
                Log.Info($"Remote install of {game.Title}: {failure ?? "started"}");
                return failure;
            });
        }
    }
}
