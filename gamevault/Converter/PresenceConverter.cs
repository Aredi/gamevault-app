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

        private static readonly IBrush Online = new SolidColorBrush(Color.Parse("#22C55E"));

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.FirstOrDefault() is not User user)
                return null;
            PlayerActivity? activity = ActivityService.Instance.Of(user.ID);
            string? playing = activity?.IsPlaying == true
                ? Loc.F("Playing {0}", GameTitles.TryGetValue(activity.GameId!.Value, out string? title) ? title : "…")
                : null;
            switch (parameter as string)
            {
                case "playing":
                    return playing;
                case "isplaying":
                    return playing != null;
                case "hasdot":
                    return activity != null;
                case "dot":
                    if (activity == null)
                        return null;
                    return activity.IsPlaying && Avalonia.Application.Current!.TryGetResource("Brush.Accent", null, out object? accent) ? accent as IBrush : Online;
                default:
                    return playing ?? (activity != null ? Loc.T("Online") : new RoleNameConverter().Convert(user.Role, typeof(string), null, culture));
            }
        }
    }
}
