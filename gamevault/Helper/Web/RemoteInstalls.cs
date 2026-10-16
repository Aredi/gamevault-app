using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace gamevault.Helper
{
    /// <summary>
    /// "Install on my PC", asked from the SanctuaryVault web page (a phone...): every minute the list of the signed-in
    /// player is read, each game is downloaded here without asking, and taken off the list.
    /// </summary>
    internal static class RemoteInstalls
    {
        private sealed class Entry
        {
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

        public static async Task CheckAsync()
        {
            if (!LoginManager.Instance.IsLoggedIn() || !await checking.WaitAsync(0))
                return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{SanctuaryService.Current.TrimEnd('/')}/remote-installs");
                using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
                if (!response.IsSuccessStatusCode)
                    return;// a service without remote installs, or out of reach: next minute
                var entries = JsonSerializer.Deserialize<List<Entry>>(await response.Content.ReadAsStringAsync()) ?? new();
                foreach (Entry entry in entries)
                {
                    await StartAsync(entry);
                    using var taken = new HttpRequestMessage(HttpMethod.Delete, $"{SanctuaryService.Current.TrimEnd('/')}/remote-installs/{entry.GameId}");
                    using HttpResponseMessage _ = await WebHelper.SendLongAsync(taken, CancellationToken.None);
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            finally { checking.Release(); }
        }

        private static async Task StartAsync(Entry entry)
        {
            Game? game = null;
            try
            {
                game = JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games/{entry.GameId}"));
            }
            catch (Exception ex) { Log.Ignored(ex); }
            if (game == null)
            {
                Log.Info($"Remote install of game {entry.GameId} ({entry.Title}): not on the server any more");
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == game.ID))
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' asked from the web page is already installed", game.Title);
                    return;
                }
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(game, unattended: true);
                ToastMessageHelper.CreateToastMessage(Loc.T("Install asked from the web page"), game.Title);
                Log.Info($"Remote install of {game.Title} started");
            });
        }
    }
}
