using GameVault.Core;
using gamevault.Models;
using gamevault.ViewModels;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>What a player is doing, as the GameVault server shares it between the clients.</summary>
    public sealed class PlayerActivity
    {
        [JsonPropertyName("user_id")]
        public int UserId { get; set; }

        /// <summary>ONLINE, BUSY or PLAYING.</summary>
        [JsonPropertyName("state")]
        public string State { get; set; } = "ONLINE";

        [JsonPropertyName("game_id")]
        public int? GameId { get; set; }

        public bool IsPlaying => State == "PLAYING" && GameId != null;
    }

    /// <summary>
    /// The players online and the games they are playing right now, through the server's activity websocket
    /// (socket.io, "set-activity" / "activities"). Says this client's own activity unless the player hides it: then
    /// only the administrators see their game, through the SanctuaryVault service (/activity), read each minute.
    /// Servers with SERVER_ONLINE_ACTIVITIES_DISABLED, or a proxy without websockets, simply leave everyone offline.
    /// </summary>
    internal sealed class ActivityService
    {
        public static ActivityService Instance { get; } = new();

        private SocketIOClient.SocketIO? socket;
        private readonly Dictionary<int, PlayerActivity> activities = new();
        /// <summary>For an administrator: the games of the players who hide them, by player.</summary>
        private Dictionary<int, PlayerActivity> hidden = new();
        private int? playing;
        private bool started;

        /// <summary>Raised on the UI thread when someone's activity changed.</summary>
        public event Action? Changed;

        public bool IsConnected => socket?.Connected == true;

        public PlayerActivity? Of(int userId)
        {
            lock (activities)
                return activities.GetValueOrDefault(userId) is { IsPlaying: true } shared ? shared : hidden.GetValueOrDefault(userId) ?? activities.GetValueOrDefault(userId);
        }

        public IReadOnlyList<PlayerActivity> All
        {
            get
            {
                lock (activities)
                    return activities.Keys.Union(hidden.Keys).Select(id => Of(id)!).ToList();
            }
        }

        /// <summary>The player chose to hide what they play (Community page).</summary>
        public static bool ShareMine
        {
            get
            {
                try { return Preferences.Get(AppConfigKey.ShareActivity, LoginManager.Instance.GetUserProfile().UserConfigFile) != "0"; }
                catch (Exception ex) { Log.Ignored(ex); return true; }
            }
            set
            {
                Preferences.Set(AppConfigKey.ShareActivity, value ? "1" : "0", LoginManager.Instance.GetUserProfile().UserConfigFile);
                _ = Instance.SendMineAsync();
            }
        }

        public async Task StartAsync()
        {
            if (started)
                return;
            started = true;
            try
            {
                User? me = LoginManager.Instance.GetCurrentUser();
                if (me == null || string.IsNullOrEmpty(me.ApiKey) || string.IsNullOrEmpty(SettingsViewModel.Instance.ServerUrl))
                    return;
                socket = new SocketIOClient.SocketIO(SettingsViewModel.Instance.ServerUrl, new SocketIOOptions
                {
                    ExtraHeaders = new Dictionary<string, string> { ["X-Api-Key"] = me.ApiKey },
                    Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                    Reconnection = true,
                    ReconnectionDelay = 5000,
                    ConnectionTimeout = TimeSpan.FromSeconds(15),
                });
                socket.On("activities", response => Apply(response.GetValue<List<PlayerActivity>>(), replaceAll: true));
                socket.On("activity", response => Apply(new List<PlayerActivity> { response.GetValue<PlayerActivity>() }, replaceAll: false));
                socket.OnConnected += async (_, _) => await SendMineAsync();
                socket.OnDisconnected += (_, _) => Apply(new List<PlayerActivity>(), replaceAll: true);
                if (me.Role == PERMISSION_ROLE.ADMIN)
                    _ = ReadHiddenAsync();
                await socket.ConnectAsync();
            }
            catch (Exception ex)
            {
                // Activities off on the server, or no websocket through the proxy: everyone shows as offline
                Log.Ignored(ex);
            }
        }

        /// <summary>The game played now (null: none), from the play time tracker.</summary>
        public void SetPlaying(int? gameId)
        {
            if (playing == gameId)
            {
                // Hidden: said again each minute (the tracker's), the service forgets a game not said for 3
                if (playing != null && !ShareMine)
                    _ = SendHiddenAsync();
                return;
            }
            playing = gameId;
            _ = SendMineAsync();
        }

        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>The game of a player who hides it, for the administrators; none when shown to all.</summary>
        private async Task SendHiddenAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"{Service}/activity/me")
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { gameId = ShareMine ? null : playing }), System.Text.Encoding.UTF8, "application/json"),
                };
                using var _ = await WebHelper.SendLongAsync(request, System.Threading.CancellationToken.None);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>An administrator's client: the hidden games each minute, while it is started.</summary>
        private async Task ReadHiddenAsync()
        {
            while (started)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/activity/hidden");
                    using var response = await WebHelper.SendLongAsync(request, System.Threading.CancellationToken.None);
                    if (response.IsSuccessStatusCode)
                        ApplyHidden(System.Text.Json.JsonSerializer.Deserialize<List<PlayerActivity>>(await response.Content.ReadAsStringAsync()));
                }
                catch (Exception ex) { Log.Ignored(ex); }
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }

        /// <summary>Takes the hidden games as the service sends them (also used by the tests).</summary>
        internal void ApplyHidden(IEnumerable<PlayerActivity>? received)
        {
            var games = (received ?? Enumerable.Empty<PlayerActivity>()).ToDictionary(a => a.UserId);
            lock (activities)
                hidden = games;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }

        private async Task SendMineAsync()
        {
            _ = SendHiddenAsync();
            if (socket?.Connected != true)
                return;
            try
            {
                // Hidden: online, without the game
                bool share = ShareMine;
                await socket.EmitAsync("set-activity", new PlayerActivity
                {
                    State = share && playing != null ? "PLAYING" : "ONLINE",
                    GameId = share ? playing : null,
                });
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>Takes activities as the server sends them (also used by the tests).</summary>
        internal void Apply(IEnumerable<PlayerActivity>? received, bool replaceAll)
        {
            lock (activities)
            {
                if (replaceAll)
                    activities.Clear();
                foreach (PlayerActivity activity in received ?? Enumerable.Empty<PlayerActivity>())
                    activities[activity.UserId] = activity;
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }

        public async Task StopAsync()
        {
            started = false;
            try
            {
                if (socket != null)
                {
                    await socket.DisconnectAsync();
                    socket.Dispose();
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            socket = null;
            ApplyHidden(null);
            Apply(new List<PlayerActivity>(), replaceAll: true);
        }
    }
}
