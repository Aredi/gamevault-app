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

        /// <summary>"Playing Hades", "Playing The Legend of Zelda (NES)": a library game first, else a retro game; null when none.</summary>
        public static string? PlayingText(int userId)
        {
            if (ActivityService.Instance.Of(userId) is { IsPlaying: true } activity)
                return Loc.F("Playing {0}", GameTitles.TryGetValue(activity.GameId!.Value, out string? title) ? title : "…");
            return ActivityService.Instance.RetroOf(userId) is RetroActivity retro ? Loc.F("Playing {0}", retro.Title) : null;
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
            PlayerActivity? activity = ActivityService.Instance.Of(user.ID);
            string? playing = PlayingText(user.ID);
            switch (parameter as string)
            {
                case "playing":
                    return playing;
                case "isplaying":
                    return playing != null;
                case "hasdot":
                    return activity != null || playing != null;
                case "dot":
                    if (activity == null && playing == null)
                        return null;
                    return playing != null && Avalonia.Application.Current!.TryGetResource("Brush.Accent", null, out object? accent) ? accent as IBrush : Online;
                default:
                    return playing ?? (activity != null ? Loc.T("Online") : new RoleNameConverter().Convert(user.Role, typeof(string), null, culture));
            }
        }
    }
}
