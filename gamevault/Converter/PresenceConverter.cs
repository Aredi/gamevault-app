using Avalonia.Data.Converters;
using Avalonia.Media;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace gamevault.Converter
{
    /// <summary>
    /// A player's activity (values: user, activity version). Parameter "text": "Playing Hades" / "Online" / the role;
    /// "playing": "Playing Hades" or null; "dot": the color of the dot on the avatar (null when offline);
    /// "isplaying" / "hasdot": whether to show them.
    /// </summary>
    internal class PresenceConverter : IMultiValueConverter
    {
        /// <summary>Titles of the games being played, filled by the community page.</summary>
        public static readonly Dictionary<int, string> GameTitles = new();

        /// <summary>
        /// "Playing Hades", "Playing The Legend of Zelda (NES) for 25 min": a library game first, else a retro game;
        /// null when none. How long, when the player's client said when it started.
        /// </summary>
        public static string? PlayingText(int userId)
        {
            string? title = null;
            if (ActivityService.Instance.Of(userId) is { IsPlaying: true } activity)
                title = GameTitles.TryGetValue(activity.GameId!.Value, out string? known) ? known : "…";
            else if (ActivityService.Instance.RetroOf(userId) is RetroActivity retro)
                title = retro.Title;
            if (title == null)
                return null;
            return ActivityService.Instance.PlayingSince(userId) is DateTimeOffset since
                ? Loc.F("Playing {0} for {1}", title, Duration(DateTimeOffset.UtcNow - since))
                : Loc.F("Playing {0}", title);
        }

        /// <summary>"1 min", "25 min", "2 h", "1 h 05".</summary>
        public static string Duration(TimeSpan played)
        {
            int minutes = Math.Max(1, (int)played.TotalMinutes);
            return minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00}";
        }

        public static readonly IBrush Away = new SolidColorBrush(Color.Parse("#F59E0B"));
        public static readonly IBrush Busy = new SolidColorBrush(Color.Parse("#EF4444"));

        /// <summary>
        /// What a player is shown doing: the game played (or their status), the dot's color (null: offline), and their
        /// rank in a list (playing, then online, then away, then offline).
        /// </summary>
        public static (string Text, IBrush? Dot, int Rank) Describe(int userId)
        {
            PlayerActivity? activity = ActivityService.Instance.Of(userId);
            string? playing = PlayingText(userId);
            string? state = ActivityService.Instance.StateOf(userId);
            if (playing != null)
                return (playing, state == "busy" ? Busy : Avalonia.Application.Current!.TryGetResource("Brush.Accent", null, out object? accent) ? accent as IBrush : Online, 0);
            if (activity == null)
                return (Loc.T("Offline"), null, 3);
            return state switch
            {
                "away" => (Loc.T("Away"), Away, 2),
                "busy" => (Loc.T("Do not disturb"), Busy, 1),
                _ => (Loc.T("Online"), Online, 1),
            };
        }

        /// <summary>The titles of the games being played that are not known yet (the community page, the chat).</summary>
        public static async System.Threading.Tasks.Task LoadTitlesAsync()
        {
            foreach (int gameId in ActivityService.Instance.All.Where(a => a.IsPlaying).Select(a => a.GameId!.Value).Distinct().ToList())
            {
                if (GameTitles.ContainsKey(gameId))
                    continue;
                Game? known = ViewModels.MainWindowViewModel.Instance.Library?.Model.GameCards.FirstOrDefault(g => g.ID == gameId)
                    ?? ViewModels.InstallViewModel.Instance.InstalledGames.Select(g => g.Key).FirstOrDefault(g => g?.ID == gameId);
                try
                {
                    known ??= System.Text.Json.JsonSerializer.Deserialize<Game>(await WebHelper.GetAsync($"{ViewModels.SettingsViewModel.Instance.ServerUrl}/api/games/{gameId}"));
                }
                catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                // An unknown game (or one not found yet) is asked again next time, not kept without a title
                if ((known?.Metadata?.Title ?? known?.Title) is { Length: > 0 } title)
                    GameTitles[gameId] = title;
            }
        }

        public static readonly IBrush Online = new SolidColorBrush(Color.Parse("#22C55E"));

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.FirstOrDefault() is not User user)
                return null;
            string? playing = PlayingText(user.ID);
            var (text, dot, _) = Describe(user.ID);
            switch (parameter as string)
            {
                case "playing":
                    return playing;
                case "isplaying":
                    return playing != null;
                case "hasdot":
                    return dot != null;
                case "dot":
                    return dot;
                default:
                    return dot != null ? text : new RoleNameConverter().Convert(user.Role, typeof(string), null, culture);
            }
        }
    }
}
