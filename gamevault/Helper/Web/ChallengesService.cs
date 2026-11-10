using GameVault.Core;
using gamevault.Helper.Integrations;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>The challenge of a week, as the SanctuaryVault service gives it.</summary>
    public sealed class ChallengeInfo
    {
        public const string Hours = "hours", Games = "games", Discovery = "discovery", Retro = "retro", Achievements = "achievements", Game = "game";

        [JsonPropertyName("kind")] public string Kind { get; set; } = Hours;
        /// <summary>Minutes, or a number of games or achievements (<see cref="IsCount"/>).</summary>
        [JsonPropertyName("goal")] public int Goal { get; set; }
        [JsonPropertyName("game")] public ChatGame? TheGame { get; set; }
        /// <summary>The administrator who chose it; null when the service did.</summary>
        [JsonPropertyName("setBy")] public string? SetBy { get; set; }

        [JsonIgnore] public bool IsCount => Kind is Games or Achievements;

        /// <summary>"2 h 30" or "3": what a player did, or the goal.</summary>
        public string Amount(int value) => IsCount ? value.ToString() : value <= 0 ? "0 min" : Converter.PresenceConverter.Duration(TimeSpan.FromMinutes(value));

        /// <summary>"Jouez 5 h cette semaine".</summary>
        [JsonIgnore]
        public string Text => Kind switch
        {
            Hours => Loc.F("Play {0} this week", Amount(Goal)),
            Games => Goal == 1 ? Loc.T("Play a game this week") : Loc.F("Play {0} different games this week", Goal),
            Discovery => Loc.F("Play {0} of a game you never launched", Amount(Goal)),
            Retro => Loc.F("Play {0} of retro games", Amount(Goal)),
            Achievements when TheGame != null => Goal == 1 ? Loc.F("Earn a retro achievement in {0}", TheGame.Title) : Loc.F("Earn {0} retro achievements in {1}", Goal, TheGame.Title),
            Achievements => Goal == 1 ? Loc.T("Earn a retro achievement") : Loc.F("Earn {0} retro achievements", Goal),
            Game => Loc.F("Play {1} for {0}", Amount(Goal), TheGame?.Title ?? "?"),
            _ => Kind,
        };

        /// <summary>What counts for it, under its text.</summary>
        [JsonIgnore]
        public string Hint => Kind switch
        {
            Hours => Loc.T("Every game counts: the library, the consoles and retro."),
            Games => Loc.T("A game counts from 15 minutes played."),
            Discovery => Loc.T("A game of the library or a retro game you had no play time on before this week."),
            Retro => Loc.T("The time played on the consoles and retro games."),
            Achievements => Loc.T("The RetroAchievements earned since Monday."),
            _ => "",
        };
    }

    public sealed class ChallengeRank
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("value")] public int Value { get; set; }
        /// <summary>When the player reached the goal; null while they did not.</summary>
        [JsonPropertyName("doneAt")] public DateTime? DoneAt { get; set; }
    }

    /// <summary>The challenges a player completed since the challenges exist.</summary>
    public sealed class ChallengeHonor
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("completed")] public int Completed { get; set; }
        /// <summary>The weeks they were the first to complete it.</summary>
        [JsonPropertyName("first")] public int First { get; set; }
    }

    public sealed class ChallengePast
    {
        [JsonPropertyName("week")] public string Week { get; set; } = "";
        [JsonPropertyName("startsAt")] public DateTime StartsAt { get; set; }
        [JsonPropertyName("challenge")] public ChallengeInfo Challenge { get; set; } = new();
        [JsonPropertyName("winner")] public ChallengeRank? Winner { get; set; }
        [JsonPropertyName("completed")] public int Completed { get; set; }
    }

    public sealed class ChallengeState
    {
        [JsonPropertyName("week")] public string Week { get; set; } = "";
        [JsonPropertyName("startsAt")] public DateTime StartsAt { get; set; }
        [JsonPropertyName("endsAt")] public DateTime EndsAt { get; set; }
        [JsonPropertyName("challenge")] public ChallengeInfo Challenge { get; set; } = new();
        /// <summary>The players who started it, the ones who completed it first.</summary>
        [JsonPropertyName("board")] public List<ChallengeRank> Board { get; set; } = new();
        [JsonPropertyName("allTime")] public List<ChallengeHonor> AllTime { get; set; } = new();
        [JsonPropertyName("past")] public List<ChallengePast> Past { get; set; } = new();
    }

    /// <summary>The challenge of the week and its rankings, kept by the SanctuaryVault service.</summary>
    internal static class ChallengesService
    {
        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>The service keeps no challenges (or is an older one): the minutes played are not sent to it for a while.</summary>
        internal static DateTime OffUntil { get; set; }

        /// <summary>The player just completed the challenge of the week (the page shows it at once).</summary>
        public static event Action? Completed;

        private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancel = default)
        {
            using var request = new HttpRequestMessage(method, Service + path) { Content = body == null ? null : RetroService.Json(body) };
            return await WebHelper.SendLongAsync(request, cancel);
        }

        /// <summary>This week's challenge with its rankings; null when the server keeps no challenges.</summary>
        public static async Task<ChallengeState?> GetAsync()
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/challenges", null);
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<ChallengeState>(await response.Content.ReadAsStringAsync());
        }

        /// <summary>An administrator chooses this week's challenge; a refusal throws with its reason.</summary>
        public static async Task SetAsync(string kind, int goal, ChatGame? game)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Put, "/challenges/week", new { kind, goal, game });
            if (response.IsSuccessStatusCode)
                return;
            string? error = null;
            try
            {
                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                error = json.RootElement.TryGetProperty("error", out JsonElement reason) ? WebExceptionHelper.Translate(reason.GetString() ?? "") : null;
            }
            catch (JsonException) { }
            throw new InvalidOperationException(error ?? Loc.T("The SanctuaryVault service could not be reached."));
        }

        /// <summary>
        /// A minute played on these games of the library. Said before the GameVault server counts it: the service asks
        /// that server which games the player knew, and a game's very first minute must not be among them.
        /// </summary>
        public static async Task PlayedAsync(IReadOnlyCollection<int> gameIds)
        {
            if (gameIds.Count == 0 || DateTime.UtcNow < OffUntil)
                return;
            try
            {
                // Short: the play time itself is counted after this
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/challenges/me/plays", new { gameIds = gameIds.Take(8) }, cancel.Token);
                if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound)
                    OffUntil = DateTime.UtcNow.AddMinutes(30);
                else
                    await ToldAsync(response);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The service answers {"challengeDone": true} to what completes the challenge: the player is told, once.</summary>
        public static async Task ToldAsync(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
                return;
            try
            {
                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("challengeDone", out JsonElement done) || done.ValueKind != JsonValueKind.True)
                    return;
            }
            catch (JsonException) { return; }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("Challenge of the week completed!");
                Completed?.Invoke();
            });
            ToastMessageHelper.CreateToastMessage("Challenge of the week completed!", "See the ranking on the Challenges page.");
        }
    }
}
