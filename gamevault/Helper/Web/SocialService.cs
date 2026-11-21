using GameVault.Core;
using gamevault.Helper.Integrations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    public sealed class SocialReaction
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("emoji")] public string Emoji { get; set; } = "";
    }

    public sealed class SocialRecommendation
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("from")] public int From { get; set; }
        [JsonPropertyName("fromName")] public string FromName { get; set; } = "";
        [JsonPropertyName("to")] public int To { get; set; }
        [JsonPropertyName("game")] public ChatGame Game { get; set; } = new();
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("at")] public DateTime At { get; set; }
        /// <summary>"Léa recommends Hades to you".</summary>
        [JsonIgnore] public string Text => Localization.Loc.F("{0} recommends {1} to you", FromName, Game.Title);
    }

    public sealed class SocialReview
    {
        [JsonPropertyName("game")] public string Game { get; set; } = "";
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("stars")] public int Stars { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("at")] public DateTime At { get; set; }
    }

    public sealed class SocialRating
    {
        [JsonPropertyName("stars")] public double Stars { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
    }

    /// <summary>
    /// What the players say to each other about their games, kept by the SanctuaryVault service: reactions on the
    /// activity, games recommended to a friend, reviews on a game's page.
    /// </summary>
    internal static class SocialService
    {
        /// <summary>The reactions offered, as the service takes them.</summary>
        public static readonly IReadOnlyList<string> Emojis = new[] { "GG", "👏", "🔥", "😂", "😮", "❤️" };

        /// <summary>On the UI thread: someone reacted on the player's activity (who, the emoji, the line).</summary>
        public static event Action<string, string, string>? ReactionReceived;
        /// <summary>On the UI thread: a friend recommended a game to the player.</summary>
        public static event Action<SocialRecommendation>? RecommendationReceived;

        /// <summary>Told by the chat's live link; the player is told by a notification too.</summary>
        internal static void RaiseReaction(string userName, string emoji, string item)
        {
            ToastMessageHelper.ShowAsIs(Localization.Loc.F("{0} reacted {1}", userName, emoji), Localization.Loc.T("On your activity"));
            ReactionReceived?.Invoke(userName, emoji, item);
        }

        internal static void RaiseRecommendation(SocialRecommendation recommendation)
        {
            ToastMessageHelper.ShowAsIs(Localization.Loc.F("{0} recommends {1} to you", recommendation.FromName, recommendation.Game.Title), recommendation.Note ?? "");
            RecommendationReceived?.Invoke(recommendation);
        }

        /// <summary>A line of the activity, the same on every PC: its kind, its player, its game, its day (UTC).</summary>
        public static string ItemKey(string kind, int userId, string game, DateTime at) =>
            $"{kind}:{userId}:{game.ToLowerInvariant()}:{at.ToUniversalTime():yyyy-MM-dd}";

        public static string GameKey(Models.Game game) => $"pc:{game.ID}";
        public static string GameKey(RetroGame game) => "retro:" + RetroLibrary.Key(game);
        public static string GameKey(ChatGame game) => game.IsRetro ? $"retro:{game.Platform}:{game.Id}" : $"pc:{game.Id}";

        private static string Escaped(string key) => Uri.EscapeDataString(key);

        /// <summary>The reactions on these lines; none when the service does not keep them (an older one, offline).</summary>
        public static async Task<Dictionary<string, List<SocialReaction>>> ReactionsAsync(IEnumerable<string> items)
        {
            string list = string.Join(",", items.Distinct());
            if (list.Length == 0)
                return new();
            try { return await RetroService.TryGetAsync<Dictionary<string, List<SocialReaction>>>($"/social/reactions?items={Escaped(list)}") ?? new(); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return new();
            }
        }

        /// <summary>Puts the player's reaction, or takes it away (null); the line's reactions as they are now.</summary>
        public static async Task<List<SocialReaction>> ReactAsync(string item, string? emoji)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Put, "/social/reactions", RetroService.Json(new { item, emoji }));
            return JsonSerializer.Deserialize<List<SocialReaction>>(await Checked(response)) ?? new();
        }

        public static async Task RecommendAsync(int to, ChatGame game, string? note)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Post, "/social/recommendations", RetroService.Json(new { to, game, note }));
            await Checked(response);
        }

        public static async Task<List<SocialRecommendation>> MyRecommendationsAsync()
        {
            try { return await RetroService.TryGetAsync<List<SocialRecommendation>>("/social/recommendations/me") ?? new(); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return new();
            }
        }

        public static async Task DismissAsync(SocialRecommendation recommendation)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Delete, $"/social/recommendations/{Escaped(recommendation.Id)}");
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                await Checked(response);
        }

        /// <summary>Null when the service keeps no reviews (the page then shows none).</summary>
        public static async Task<List<SocialReview>?> ReviewsAsync(string game)
        {
            try { return await RetroService.TryGetAsync<List<SocialReview>>($"/social/reviews/{Escaped(game)}"); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>Every reviewed game's average ("pc:793" → 4.5 stars, 2 reviews); none when the service keeps no reviews.</summary>
        public static async Task<Dictionary<string, SocialRating>> RatingsAsync()
        {
            try { return await RetroService.TryGetAsync<Dictionary<string, SocialRating>>("/social/reviews") ?? new(); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return new();
            }
        }

        public static async Task SetReviewAsync(string game, int stars, string? text)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Put, $"/social/reviews/{Escaped(game)}/me", RetroService.Json(new { stars, text }));
            await Checked(response);
        }

        /// <param name="userId">Another player's (an administrator), null for the player's own.</param>
        public static async Task RemoveReviewAsync(string game, int? userId = null)
        {
            using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Delete, $"/social/reviews/{Escaped(game)}/{(userId is int id ? id.ToString() : "me")}");
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                await Checked(response);
        }

        /// <summary>The body, or the service's reason as the exception's message.</summary>
        private static async Task<string> Checked(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return body;
            string? error = null;
            try { error = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString(); }
            catch (Exception) { }
            throw new HttpRequestException(error ?? response.ReasonPhrase, null, response.StatusCode);
        }
    }
}
