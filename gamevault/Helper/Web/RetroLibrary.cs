using GameVault.Core;
using GameVault.Core.Library;
using GameVault.Core.Retro;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The player's side of the retro games, as the library has it for the server's games: the games installed on this
    /// PC (found on disk, so they are there offline too), the time played on each (kept by the SanctuaryVault service,
    /// sent later when it can not be reached), the favorites (kept by the service, so they follow the player) and the
    /// collections (on this PC, as the library's).
    /// </summary>
    internal static class RetroLibrary
    {
        public static string Key(RetroGame game) => $"{game.Platform.Slug}:{game.Id}";

        #region Installed games
        /// <summary>The retro games installed in the library folders, from the description written beside each.</summary>
        public static List<RetroGame> InstalledGames() => RetroService.SavedGames(downloading: false).Select(s => s.Game)
            .Where(g => g.Installed).DistinctBy(Key).ToList();

        /// <summary>Games installed before the description existed get one, so they are found offline too.</summary>
        public static void Describe(IEnumerable<RetroGame> games)
        {
            foreach (RetroGame game in games)
            {
                try
                {
                    if (RetroService.InstalledFolder(game) is string folder && !File.Exists(Path.Combine(folder, RetroDownload.InfoFile)))
                        File.WriteAllText(Path.Combine(folder, RetroDownload.InfoFile), JsonSerializer.Serialize(new RetroDownload.SavedGame(game, game.Platform)));
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }
        #endregion

        #region Time played and favorites
        internal sealed record Play
        {
            [JsonPropertyName("romId")] public int RomId { get; set; }
            [JsonPropertyName("platform")] public string Platform { get; set; } = "";
            [JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("cover")] public string? Cover { get; set; }
            [JsonPropertyName("minutes")] public int Minutes { get; set; }
            [JsonPropertyName("lastPlayed")] public DateTime LastPlayed { get; set; }
            public string Key => $"{Platform}:{RomId}";
        }

        internal sealed class Unlock
        {
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("badge")] public string? Badge { get; set; }
            [JsonPropertyName("date")] public DateTime Date { get; set; }
        }

        /// <summary>A game's achievements of a player: how many earned, the last ones.</summary>
        internal sealed class GameAchievements
        {
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";
            [JsonPropertyName("cover")] public string? Cover { get; set; }
            [JsonPropertyName("earned")] public int Earned { get; set; }
            [JsonPropertyName("total")] public int Total { get; set; }
            [JsonPropertyName("unlocks")] public List<Unlock> Unlocks { get; set; } = new();
        }

        internal sealed class Player
        {
            [JsonPropertyName("plays")] public List<Play> Plays { get; set; } = new();
            [JsonPropertyName("favorites")] public List<string> Favorites { get; set; } = new();
            /// <summary>By game ("psx:2161").</summary>
            [JsonPropertyName("achievements")] public Dictionary<string, GameAchievements> Achievements { get; set; } = new();
        }

        private static Dictionary<string, Play> plays = new();
        private static HashSet<string> favorites = new();
        private static readonly SemaphoreSlim loading = new(1, 1);

        /// <summary>Not sent yet (the service could not be reached): sent with the next load.</summary>
        private static string PendingFile => Path.Combine(Path.GetDirectoryName(LoginManager.Instance.GetUserProfile().CollectionsFile)!, "retro-pending-plays.json");

        /// <summary>Fired when the times or the favorites changed (the pages show them again).</summary>
        public static event Action? Changed;

        /// <summary>The player's times and favorites from the service, after sending what waited.</summary>
        public static async Task LoadAsync()
        {
            if (!await loading.WaitAsync(0))
                return;
            try
            {
                await SendPendingAsync();
                if (await RetroService.TryGetAsync<Player>("/retro/players/me") is not Player player)
                    return;
                plays = player.Plays.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
                favorites = player.Favorites.ToHashSet();
                Changed?.Invoke();
            }
            catch (Exception ex) { Log.Ignored(ex); }
            finally { loading.Release(); }
        }

        /// <summary>The games played last (all consoles the client plays), for "Continue playing".</summary>
        public static List<RetroGame> Recent(int count) => plays.Values
            .Where(p => RetroSystems.All.ContainsKey(p.Platform))
            .OrderByDescending(p => p.LastPlayed).Take(count)
            .Select(p => GameOf(p.RomId, p.Platform, p.PlatformName, p.Name, p.Cover)).ToList();

        public static int MinutesOf(RetroGame game) => plays.TryGetValue(Key(game), out Play? play) ? play.Minutes : 0;
        public static DateTime? LastPlayedOf(RetroGame game) => plays.TryGetValue(Key(game), out Play? play) ? play.LastPlayed : null;

        /// <summary>A game was played: counted here at once, and on the service (the profile, the activity).</summary>
        public static async Task RecordPlayAsync(RetroGame game, int minutes)
        {
            if (minutes < 1)
                return;
            Play Session() => new() { RomId = game.Id, Platform = game.Platform.Slug, PlatformName = game.Platform.Name, Name = game.Name, Cover = game.Cover, Minutes = minutes };
            Play play = Session();
            // The total here, the session for the service
            Play total = plays.TryGetValue(play.Key, out Play? known) ? known : plays[play.Key] = Session() with { Minutes = 0 };
            total.Minutes += minutes;
            total.LastPlayed = DateTime.UtcNow;
            Changed?.Invoke();
            if (!await SendAsync(play))
            {
                // Kept for later: the time is not lost
                try
                {
                    var pending = ReadPending();
                    pending.Add(play);
                    File.WriteAllText(PendingFile, JsonSerializer.Serialize(pending));
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }

        private static List<Play> ReadPending()
        {
            try { return File.Exists(PendingFile) ? JsonSerializer.Deserialize<List<Play>>(File.ReadAllText(PendingFile)) ?? new() : new(); }
            catch (Exception ex) { Log.Ignored(ex); return new(); }
        }

        private static async Task SendPendingAsync()
        {
            var pending = ReadPending();
            if (pending.Count == 0)
                return;
            var left = new List<Play>();
            foreach (Play play in pending)
                if (!await SendAsync(play))
                    left.Add(play);
            if (left.Count == 0)
                File.Delete(PendingFile);
            else
                File.WriteAllText(PendingFile, JsonSerializer.Serialize(left));
        }

        private static async Task<bool> SendAsync(Play play)
        {
            try
            {
                // At most a day per call: a longer session is sent in parts
                for (int left = play.Minutes; left > 0; left -= 1440)
                {
                    var part = new { romId = play.RomId, platform = play.Platform, platformName = play.PlatformName, name = play.Name, cover = play.Cover, minutes = Math.Min(left, 1440) };
                    using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Post, "/retro/players/me/plays", RetroService.Json(part));
                    if (!response.IsSuccessStatusCode)
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return false;
            }
        }

        public static bool IsFavorite(RetroGame game) => favorites.Contains(Key(game));

        public static async Task SetFavoriteAsync(RetroGame game, bool favorite)
        {
            if (favorite)
                favorites.Add(Key(game));
            else
                favorites.Remove(Key(game));
            Changed?.Invoke();
            try
            {
                using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Put, "/retro/players/me/favorites", RetroService.Json(favorites));
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }

        /// <summary>Another player's times and achievements, for their profile.</summary>
        public static async Task<Player> PlayerOfAsync(int userId) => await RetroService.TryGetAsync<Player>($"/retro/players/{userId}") ?? new();

        /// <summary>A game's achievements of the player, for their profile and the activity of all; nothing when none is earned.</summary>
        public static async Task ShareAchievementsAsync(RetroGame game, IReadOnlyList<RommProgress.Achievement> achievements)
        {
            int earned = achievements.Count(a => a.Earned);
            if (earned == 0)
                return;
            var shared = new GameAchievements
            {
                Name = game.Name,
                PlatformName = game.Platform.Name,
                Cover = game.Cover,
                Earned = earned,
                Total = achievements.Count,
                Unlocks = achievements.Where(a => a.EarnedAt != null).OrderByDescending(a => a.EarnedAt).Take(20)
                    .Select(a => new Unlock { Title = a.Title, Badge = a.Badge, Date = a.EarnedAt!.Value }).ToList(),
            };
            try
            {
                using HttpResponseMessage response = await RetroService.SendAsync(HttpMethod.Put, $"/retro/players/me/achievements/{game.Platform.Slug}/{game.Id}", RetroService.Json(shared));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        internal sealed class UnlockEntry
        {
            [JsonPropertyName("userId")] public int UserId { get; set; }
            [JsonPropertyName("username")] public string Username { get; set; } = "";
            [JsonPropertyName("romId")] public int RomId { get; set; }
            [JsonPropertyName("platform")] public string Platform { get; set; } = "";
            [JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("cover")] public string? Cover { get; set; }
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("badge")] public string? Badge { get; set; }
            [JsonPropertyName("date")] public DateTime Date { get; set; }
        }

        /// <summary>The achievements all the players earned lately; none from an older service.</summary>
        public static async Task<List<UnlockEntry>> UnlocksAsync() => await RetroService.TryGetAsync<List<UnlockEntry>>("/retro/activity/achievements") ?? new();

        internal sealed class ActivityEntry
        {
            [JsonPropertyName("userId")] public int UserId { get; set; }
            [JsonPropertyName("username")] public string Username { get; set; } = "";
            [JsonPropertyName("romId")] public int RomId { get; set; }
            [JsonPropertyName("platform")] public string Platform { get; set; } = "";
            [JsonPropertyName("platformName")] public string PlatformName { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("cover")] public string? Cover { get; set; }
            [JsonPropertyName("minutes")] public int Minutes { get; set; }
            [JsonPropertyName("lastPlayed")] public DateTime LastPlayed { get; set; }
        }

        internal sealed class GamePlayer
        {
            [JsonPropertyName("userId")] public int UserId { get; set; }
            [JsonPropertyName("username")] public string Username { get; set; } = "";
            [JsonPropertyName("minutes")] public int Minutes { get; set; }
            [JsonPropertyName("lastPlayed")] public DateTime LastPlayed { get; set; }
        }

        /// <summary>The players who played a game, the last one first.</summary>
        public static async Task<List<GamePlayer>> PlayersOfAsync(RetroGame game) =>
            await RetroService.TryGetAsync<List<GamePlayer>>($"/retro/games/{game.Platform.Slug}/{game.Id}/players") ?? new();

        /// <summary>The retro games all the players played lately.</summary>
        public static async Task<List<ActivityEntry>> ActivityAsync() => await RetroService.TryGetAsync<List<ActivityEntry>>("/retro/activity") ?? new();

        /// <summary>A game known by its key only (a play, an activity): enough to show its name and cover.</summary>
        public static RetroGame GameOf(int romId, string platform, string platformName, string name, string? cover) => new()
        {
            Id = romId,
            Name = name,
            Cover = cover,
            Platform = new RetroPlatform { Slug = platform, Name = platformName },
        };
        #endregion

        /// <summary>The retro collections of the profile, beside the library's (RomM ids are unique across platforms).</summary>
        public static GameCollectionStore Collections =>
            new(Path.Combine(Path.GetDirectoryName(LoginManager.Instance.GetUserProfile().CollectionsFile)!, "retro-collections.json"));
    }
}
