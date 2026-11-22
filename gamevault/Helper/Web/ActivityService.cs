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
    /// <summary>A retro game played now (the service says it: GameVault only knows its own games).</summary>
    public sealed class RetroActivity
    {
        [System.Text.Json.Serialization.JsonPropertyName("userId")] public int UserId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("platform")] public string Platform { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("romId")] public int RomId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";

        /// <summary>When the game started (an older service: none).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("since")] public DateTimeOffset? Since { get; set; }

        /// <summary>"The Legend of Zelda (NES)".</summary>
        public string Title => string.IsNullOrEmpty(PlatformName) ? Name : $"{Name} ({PlatformName})";
    }

    /// <summary>What a player shows of themselves in the chat (the SanctuaryVault service keeps it).</summary>
    public sealed class PlayerStatus
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        /// <summary>online, away, busy, or offline (an invisible player).</summary>
        [JsonPropertyName("state")] public string State { get; set; } = "online";
        /// <summary>The library's game they play, and since when.</summary>
        [JsonPropertyName("gameId")] public int? GameId { get; set; }
        [JsonPropertyName("gameSince")] public DateTimeOffset? GameSince { get; set; }
    }

    internal sealed class ActivityService
    {
        public static ActivityService Instance { get; } = new();

        private SocketIOClient.SocketIO? socket;
        private readonly Dictionary<int, PlayerActivity> activities = new();
        /// <summary>For an administrator: the games of the players who hide them, by player.</summary>
        private Dictionary<int, PlayerActivity> hidden = new();
        private int? playing;
        private bool started;
        private Dictionary<int, RetroActivity> retro = new();
        private Dictionary<int, PlayerStatus> statuses = new();
        private DateTimeOffset? playingSince;
        private string? stateSaid;
        private DateTime stateSaidAt;
        /// <summary>Nobody touched this PC for so long: "Away" (when the player chose "Online").</summary>
        public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(10);
        private RetroGame? myRetro;
        private System.Threading.CancellationTokenSource? retroBeat;

        /// <summary>The retro game a player plays now, null when none (or they are invisible).</summary>
        public RetroActivity? RetroOf(int userId)
        {
            lock (activities)
                return IsHidden(userId) ? null : retro.GetValueOrDefault(userId);
        }

        /// <summary>An invisible player: offline for the others, whatever GameVault says.</summary>
        private bool IsHidden(int userId) => statuses.GetValueOrDefault(userId)?.State == "offline";

        /// <summary>"away" or "busy" (do not disturb); null when online, offline, or not said.</summary>
        public string? StateOf(int userId)
        {
            lock (activities)
                return statuses.GetValueOrDefault(userId)?.State switch { "away" => "away", "busy" => "busy", _ => null };
        }

        /// <summary>Since when the player plays the game they play now; null when not known.</summary>
        public DateTimeOffset? PlayingSince(int userId)
        {
            PlayerActivity? activity = Of(userId);
            lock (activities)
            {
                if (activity is { IsPlaying: true } && statuses.GetValueOrDefault(userId) is { GameSince: DateTimeOffset since } status && status.GameId == activity.GameId)
                    return since;
                return activity is { IsPlaying: true } ? null : retro.GetValueOrDefault(userId)?.Since;
            }
        }

        /// <summary>The library's game played on this PC now.</summary>
        public int? PlayingHereId => playing;
        /// <summary>The retro game played on this PC now.</summary>
        public RetroGame? PlayingHereRetro => myRetro;

        /// <summary>A game runs on this PC (the library's or a retro one): the chat's notifications wait.</summary>
        public bool IsPlayingHere => playing != null || myRetro != null;

        /// <summary>The status the player chose in the chat: online, away, busy (do not disturb) or invisible.</summary>
        public static string ChosenState
        {
            get
            {
                try { return Preferences.Get(AppConfigKey.ChatStatus, LoginManager.Instance.GetUserProfile().UserConfigFile) switch { "away" => "away", "busy" => "busy", "invisible" => "invisible", _ => "online" }; }
                catch (Exception ex) { Log.Ignored(ex); return "online"; }
            }
            set
            {
                bool wasInvisible = ChosenState == "invisible";
                Preferences.Set(AppConfigKey.ChatStatus, value, LoginManager.Instance.GetUserProfile().UserConfigFile);
                // Invisible hides the game too (GameVault's activity, the retro one): said again either way
                if (wasInvisible != (value == "invisible"))
                    _ = Instance.SendMineAsync();
                _ = Instance.SendStatusAsync(force: true);
            }
        }

        public static bool IsInvisible => ChosenState == "invisible";

        /// <summary>What the others see of this PC's player now: away once nobody touched it for a while (not while playing).</summary>
        public async Task<string> EffectiveStateAsync()
        {
            string chosen = ChosenState;
            if (chosen != "online" || IsPlayingHere)
                return chosen;
            return await Platform.IdleTime.GetAsync() >= AwayAfter ? "away" : "online";
        }

        /// <summary>The status and since when the game runs: when it changed, and each minute (the service forgets it after 3).</summary>
        private async Task SendStatusAsync(bool force = false)
        {
            try
            {
                string state = await EffectiveStateAsync();
                if (!force && state == stateSaid && DateTime.UtcNow - stateSaidAt < TimeSpan.FromMinutes(1))
                    return;
                (stateSaid, stateSaidAt) = (state, DateTime.UtcNow);
                bool share = ShareMine && state != "invisible";
                object body = new { state, gameId = share ? playing : null, gameSince = share && playing != null ? playingSince : null };
                using var request = new HttpRequestMessage(HttpMethod.Put, $"{Service}/chat/presence/me")
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
                };
                using var _ = await WebHelper.SendLongAsync(request, System.Threading.CancellationToken.None);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The players' statuses (the chat's live link brings the changes sooner).</summary>
        public async Task ReadStatusesAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/chat/presence");
                using var response = await WebHelper.SendLongAsync(request, System.Threading.CancellationToken.None);
                if (response.IsSuccessStatusCode)
                    ApplyStatuses(System.Text.Json.JsonSerializer.Deserialize<List<PlayerStatus>>(await response.Content.ReadAsStringAsync()), replaceAll: true);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>Takes the statuses as the service says them: all of them, or one player's. Also used by the tests.</summary>
        internal void ApplyStatuses(IEnumerable<PlayerStatus>? received, bool replaceAll)
        {
            lock (activities)
            {
                var next = replaceAll ? new Dictionary<int, PlayerStatus>() : new Dictionary<int, PlayerStatus>(statuses);
                foreach (PlayerStatus status in received ?? Enumerable.Empty<PlayerStatus>())
                    next[status.UserId] = status;
                statuses = next;
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }

        /// <summary>Every 15 s: is the player away; each minute: the status said, the others' read.</summary>
        private async Task StatusBeatAsync()
        {
            int beats = 0;
            while (started)
            {
                await SendStatusAsync();
                if (beats++ % 4 == 0)
                    await ReadStatusesAsync();
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }

        /// <summary>The retro game played on this PC (null when it stopped): said now, then each minute while it runs.</summary>
        public void SetRetroPlaying(RetroGame? game)
        {
            myRetro = game;
            retroBeat?.Cancel();
            retroBeat = null;
            _ = SendRetroAsync();
            if (game == null)
                return;
            var beat = retroBeat = new System.Threading.CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!beat.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(1), beat.Token); } catch (OperationCanceledException) { return; }
                    await SendRetroAsync();
                }
            });
        }

        private async Task SendRetroAsync()
        {
            try
            {
                // Hidden (Community page) or invisible (the chat): nothing is said, as for the library's games
                RetroGame? game = ShareMine && !IsInvisible ? myRetro : null;
                object body = game == null ? new { name = "" } : new { platform = game.Platform.Slug, romId = game.Id, name = game.Name, platformName = game.Platform.Name };
                using var request = new HttpRequestMessage(HttpMethod.Put, $"{Service}/activity/retro/me")
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
                };
                using var _ = await WebHelper.SendLongAsync(request, System.Threading.CancellationToken.None);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The retro games played, read each minute (the live link of the chat brings the changes sooner).</summary>
        private async Task ReadRetroAsync()
        {
            while (started)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/activity/retro");
                    using var response = await WebHelper.SendLongAsync(request, System.Threading.CancellationToken.None);
                    if (response.IsSuccessStatusCode)
                        ApplyRetro(System.Text.Json.JsonSerializer.Deserialize<List<RetroActivity>>(await response.Content.ReadAsStringAsync()), replaceAll: true);
                }
                catch (Exception ex) { Log.Ignored(ex); }
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }

        /// <summary>Takes the retro games as the service says them: all of them, or one player's (null: stopped). Also used by the tests.</summary>
        internal void ApplyRetro(IEnumerable<RetroActivity>? received, bool replaceAll, int? stoppedBy = null)
        {
            lock (activities)
            {
                if (replaceAll)
                    retro = new();
                if (stoppedBy is int stopped)
                    retro.Remove(stopped);
                foreach (RetroActivity activity in received ?? Enumerable.Empty<RetroActivity>())
                    retro[activity.UserId] = activity;
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }

        /// <summary>Raised on the UI thread when someone's activity changed.</summary>
        public event Action? Changed;

        public bool IsConnected => socket?.Connected == true;

        public PlayerActivity? Of(int userId)
        {
            lock (activities)
            {
                if (IsHidden(userId))
                    return null;
                return activities.GetValueOrDefault(userId) is { IsPlaying: true } shared ? shared : hidden.GetValueOrDefault(userId) ?? activities.GetValueOrDefault(userId);
            }
        }

        public IReadOnlyList<PlayerActivity> All
        {
            get
            {
                lock (activities)
                    return activities.Keys.Union(hidden.Keys).Where(id => !IsHidden(id)).Select(id => Of(id)!).ToList();
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
            _ = ReadRetroAsync();
            _ = StatusBeatAsync();
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
            playingSince = gameId == null ? null : DateTimeOffset.UtcNow;
            _ = SendMineAsync();
            _ = SendStatusAsync(force: true);
        }

        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>The game of a player who hides it, for the administrators; none when shown to all.</summary>
        private async Task SendHiddenAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"{Service}/activity/me")
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { gameId = ShareMine || IsInvisible ? null : playing }), System.Text.Encoding.UTF8, "application/json"),
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
            _ = SendRetroAsync();
            if (socket?.Connected != true)
                return;
            try
            {
                // Hidden or invisible: online, without the game (the chat shows an invisible player offline)
                bool share = ShareMine && !IsInvisible;
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
            ApplyRetro(null, replaceAll: true);
            ApplyStatuses(null, replaceAll: true);
            Apply(new List<PlayerActivity>(), replaceAll: true);
        }
    }
}
