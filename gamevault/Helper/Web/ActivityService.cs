using GameVault.Core;
using gamevault.Models;
using gamevault.ViewModels;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// (socket.io, "set-activity" / "activities"). Says this client's own activity unless the player hides it.
    /// Servers with SERVER_ONLINE_ACTIVITIES_DISABLED, or a proxy without websockets, simply leave everyone offline.
    /// </summary>
    internal sealed class ActivityService
    {
        public static ActivityService Instance { get; } = new();

        private SocketIOClient.SocketIO? socket;
        private readonly Dictionary<int, PlayerActivity> activities = new();
        private int? playing;
        private bool started;

        /// <summary>Raised on the UI thread when someone's activity changed.</summary>
        public event Action? Changed;

        public bool IsConnected => socket?.Connected == true;

        public PlayerActivity? Of(int userId)
        {
            lock (activities)
                return activities.GetValueOrDefault(userId);
        }

        public IReadOnlyList<PlayerActivity> All
        {
            get
            {
                lock (activities)
                    return activities.Values.ToList();
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
                return;
            playing = gameId;
            _ = SendMineAsync();
        }

        private async Task SendMineAsync()
        {
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
            Apply(new List<PlayerActivity>(), replaceAll: true);
        }
    }
}
