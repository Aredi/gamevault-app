using gamevault.Converter;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.News;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>A line of the news, ready to show.</summary>
    internal sealed class NewsItem
    {
        /// <summary>A game of the server, or <see cref="Retro"/>.</summary>
        public Game? Game { get; init; }
        /// <summary>A retro game added to RomM.</summary>
        public RetroGame? Retro { get; init; }
        public bool IsUpdate { get; init; }
        /// <summary>"New" or "Update · v1.40"</summary>
        public string KindText { get; init; } = "";
        /// <summary>"Action · 12.6 GB"</summary>
        public string Info { get; init; } = "";
        public string DateText { get; init; } = "";
        public string Title { get; init; } = "";
    }

    internal sealed class NewsGroup
    {
        public string Title { get; init; } = "";
        public List<NewsItem> Items { get; init; } = new();
    }

    internal sealed record ServerNewsData(string? Announcement, List<NewsGroup> Groups, int Unread);

    /// <summary>
    /// The server's news made from its games (<see cref="ServerNews"/>): the games added lately, and the new builds
    /// found by comparing the files with the ones seen before (kept in the profile), plus the administrator's message.
    /// </summary>
    internal static class ServerNewsService
    {
        private static readonly SemaphoreSlim gate = new(1, 1);

        private static string SnapshotFile => Path.Combine(Path.GetDirectoryName(LoginManager.Instance.GetUserProfile().CollectionsFile)!, "server-news.json");
        private static string ConfigFile => LoginManager.Instance.GetUserProfile().UserConfigFile;

        private static NewsGame ToNews(Game game) => new(game.ID, game.CreatedAt, game.Path, game.Size, game.Version);

        public static async Task<ServerNewsData> LoadAsync()
        {
            if (!LoginManager.Instance.IsLoggedIn())
                return new ServerNewsData(null, new(), 0);
            await gate.WaitAsync();
            try
            {
                string server = SettingsViewModel.Instance.ServerUrl;
                Game[] added = await Games($"{server}/api/games?sortBy=created_at:DESC&limit=40");
                Game[] changed = await Games($"{server}/api/games?sortBy=updated_at:DESC&limit=100");

                NewsSnapshot snapshot = NewsSnapshot.Load(SnapshotFile);
                ServerNews.Track(snapshot, changed.Where(g => g.DeletedAt == null).Select(ToNews), DateTime.UtcNow);
                try { snapshot.Save(SnapshotFile); }
                catch (Exception ex) { Log.Ignored(ex); }

                var feed = ServerNews.Feed(added.Where(g => g.DeletedAt == null).Select(ToNews), snapshot, DateTime.UtcNow);
                var games = added.Concat(changed).Where(g => g.DeletedAt == null).GroupBy(g => g.ID).ToDictionary(g => g.Key, g => g.First());
                // Updates of games that are no longer among the last changed ones
                var missing = feed.Select(e => e.GameId).Where(id => !games.ContainsKey(id)).Distinct().ToList();
                if (missing.Count > 0)
                {
                    foreach (Game game in await Games($"{server}/api/games?filter.id=$in:{string.Join(',', missing)}&limit=-1"))
                        if (game.DeletedAt == null)
                            games[game.ID] = game;
                }
                feed = feed.Where(e => games.ContainsKey(e.GameId)).ToList();

                string? announcement = null;
                try
                {
                    announcement = (await WebHelper.GetAsync($"{server}/api/config/news"))?.Trim();
                    // A JSON answer (an error, or a server without the news file) is not a message
                    if (announcement != null && (announcement.StartsWith('{') || announcement.StartsWith('[')))
                        announcement = null;
                }
                catch (Exception ex) { Log.Ignored(ex); }

                // The retro games added to RomM lately, among the server's by date
                var retro = new List<RetroService.LatestGame>();
                try { retro = (await RetroService.LatestAsync()).Where(g => g.AddedAt is DateTime added && DateTime.UtcNow - added.ToUniversalTime() <= ServerNews.Window).ToList(); }
                catch (Exception ex) { Log.Ignored(ex); }

                DateTime seen = LastSeen() ?? DateTime.UtcNow.AddDays(-7);
                return new ServerNewsData(string.IsNullOrWhiteSpace(announcement) ? null : announcement, Group(feed, games, retro),
                    ServerNews.Unread(feed, seen) + retro.Count(g => g.AddedAt!.Value.ToUniversalTime() > seen));
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task<Game[]> Games(string url)
        {
            try
            {
                return JsonSerializer.Deserialize<PaginatedData<Game>>(await WebHelper.GetAsync(url))?.Data ?? Array.Empty<Game>();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return Array.Empty<Game>();
            }
        }

        private static List<NewsGroup> Group(List<NewsEntry> feed, Dictionary<int, Game> games, List<RetroService.LatestGame> retro)
        {
            DateTime now = DateTime.Now;
            var size = new GameSizeConverter();
            var items = feed.Select(e => (Date: e.DateUtc, Item: ServerItem(e)))
                .Concat(retro.Select(g => (Date: g.AddedAt!.Value.ToUniversalTime(), Item: new NewsItem
                {
                    Retro = RetroLibrary.GameOf(g.Id, g.Platform, g.PlatformName ?? g.Platform, g.Name, g.Cover),
                    Title = g.Name,
                    KindText = Loc.T("New"),
                    Info = g.PlatformName ?? g.Platform,
                    DateText = RelativeDate.Format(g.AddedAt.Value),
                })))
                .OrderByDescending(i => i.Date);
            return items
                .GroupBy(i => ServerNews.PeriodOf(i.Date.ToLocalTime(), now))
                .OrderBy(g => g.Key)
                .Select(g => new NewsGroup
                {
                    Title = g.Key switch
                    {
                        ServerNews.Period.ThisWeek => Loc.T("This week"),
                        ServerNews.Period.LastWeek => Loc.T("Last week"),
                        ServerNews.Period.ThisMonth => Loc.T("Earlier this month"),
                        _ => Loc.T("Before"),
                    },
                    Items = g.Select(i => i.Item).ToList(),
                })
                .ToList();

            NewsItem ServerItem(NewsEntry e)
            {
                Game game = games[e.GameId];
                bool update = e.Kind == NewsKind.Updated;
                var info = new[]
                {
                    game.Metadata?.Genres?.FirstOrDefault()?.Name,
                    game.Metadata?.ReleaseDate?.Year.ToString(CultureInfo.InvariantCulture),
                    string.IsNullOrEmpty(game.Size) ? null : (string)size.Convert(game.Size, typeof(string), null, CultureInfo.CurrentCulture)!,
                };
                return new NewsItem
                {
                    Game = game,
                    IsUpdate = update,
                    Title = game.Metadata?.Title is { Length: > 0 } title && SettingsViewModel.Instance.ShowMappedTitle ? title : game.Title,
                    KindText = update
                        ? (string.IsNullOrEmpty(e.Version) ? Loc.T("New version") : Loc.F("Update · {0}", e.Version))
                        : Loc.T("New"),
                    Info = string.Join(" · ", info.Where(p => !string.IsNullOrEmpty(p))),
                    DateText = RelativeDate.Format(e.DateUtc),
                };
            }
        }

        public static DateTime? LastSeen()
        {
            try
            {
                return DateTime.TryParse(Preferences.Get(AppConfigKey.NewsLastSeen, ConfigFile), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime seen) ? seen : null;
            }
            catch { return null; }
        }

        /// <summary>The news were opened: they are no longer counted as new.</summary>
        public static void MarkSeen()
        {
            try { Preferences.Set(AppConfigKey.NewsLastSeen, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ConfigFile); }
            catch (Exception ex) { Log.Ignored(ex); }
        }
    }
}
