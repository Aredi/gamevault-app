using Avalonia.Data.Converters;
using Avalonia.Media;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace gamevault.Converter
{
    /// <summary>A game color (null: the theme accent) as a brush. Parameter: opacity, e.g. "0.35".</summary>
    internal class AccentBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Color color = value as Color? ?? CoverColors.ThemeAccent;
            double opacity = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double o) ? o : 1;
            return new SolidColorBrush(color, opacity);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }

    /// <summary>A glow in the game color. Parameter: "offsetY blur spread opacity".</summary>
    internal class AccentShadowConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Color color = value as Color? ?? CoverColors.ThemeAccent;
            double[] p = (parameter as string ?? "14 32 -10 0.7").Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            return new BoxShadows(new BoxShadow
            {
                OffsetY = p[0],
                Blur = p[1],
                Spread = p[2],
                Color = Color.FromArgb((byte)(p[3] * 255), color.R, color.G, color.B),
            });
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }

    /// <summary>The game is installed on this computer. Bind the installed games count second so it refreshes.</summary>
    internal class IsGameInstalledConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            int? id = values.FirstOrDefault() switch { Game game => game.ID, int i => i, _ => null };
            bool installed = id != null && InstallViewModel.Instance.InstalledGames.Any(g => g.Key?.ID == id);
            return parameter as string == "invert" ? !installed : installed;
        }
    }

    /// <summary><see cref="GameUpdateAvailableConverter"/> that refreshes with the installed games count (second value).</summary>
    internal class GameUpdateAvailableMultiConverter : IMultiValueConverter
    {
        private static readonly GameUpdateAvailableConverter single = new();
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            single.Convert(values.FirstOrDefault(), targetType, parameter, culture);
    }

    /// <summary>The two values are the same game (shelf selection).</summary>
    internal class SameGameConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count == 2 && values[0] is Game a && values[1] is Game b && a.ID == b.ID;
    }

    /// <summary>"Action · 12.6 GB": the first genre and the size, for the line under a cover.</summary>
    internal class GameCardInfoConverter : IValueConverter
    {
        private static readonly GameSizeConverter size = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Game game)
                return "";
            var parts = new List<string>();
            string? genre = game.Metadata?.Genres?.FirstOrDefault()?.Name;
            if (!string.IsNullOrEmpty(genre))
                parts.Add(genre);
            if (game.Metadata?.ReleaseDate is DateTime release && parameter as string == "year")
                parts.Add(release.Year.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(game.Size))
                parts.Add((string)size.Convert(game.Size, typeof(string), null, culture)!);
            return string.Join(" · ", parts);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }

    /// <summary>"34 h · yesterday": the current user's play time and last session of a game (values: game, refresh counter).</summary>
    internal class PlayInfoConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.FirstOrDefault() is not Game game)
                return "";
            bool timeOnly = parameter as string == "time";
            if (!InstallViewModel.Instance.PlayRecords.TryGetValue(game.ID, out PlayRecord? record) || record.MinutesPlayed == 0 && record.LastPlayedAt == null)
                return timeOnly ? "—" : Loc.T("Not played yet");
            string time = (string)new GameTimeConverter().Convert(record.MinutesPlayed, typeof(string), null, culture)!;
            if (timeOnly)
                return time;
            return record.LastPlayedAt == null ? time : $"{time} · {RelativeDate.Format(record.LastPlayedAt.Value)}";
        }
    }

    internal static class RelativeDate
    {
        /// <summary>"today", "yesterday", "3 days ago", ... then the date.</summary>
        public static string Format(DateTime value)
        {
            DateTime local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            int days = (DateTime.Now.Date - local.Date).Days;
            return days switch
            {
                <= 0 => Loc.T("today"),
                1 => Loc.T("yesterday"),
                < 7 => Loc.F("{0} days ago", days),
                < 31 => days / 7 == 1 ? Loc.T("last week") : Loc.F("{0} weeks ago", days / 7),
                _ => local.ToString("d", CultureInfo.CurrentCulture),
            };
        }
    }

    internal class RelativeDateConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is DateTime date ? RelativeDate.Format(date) : Loc.T("never");

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }

    /// <summary>Multiplies a number by the parameter, plus an optional "+n" (cover height from its width).</summary>
    internal class ScaleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not double number)
                return 0d;
            string[] p = (parameter as string ?? "1").Split('+');
            double result = number * double.Parse(p[0], CultureInfo.InvariantCulture);
            if (p.Length > 1)
                result += double.Parse(p[1], CultureInfo.InvariantCulture);
            return result;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }

    /// <summary>The plain text of a description in markdown (the shelf shows a short summary).</summary>
    internal class PlainTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text))
                return Loc.T("No description");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>|[#*_`>]|\[([^\]]*)\]\([^)]*\)", m => m.Groups[1].Success ? m.Groups[1].Value : "");
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s*\n\s*\n\s*", "\n\n").Trim();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}

namespace gamevault.Converter
{
    /// <summary>A blur effect when true (a cover stretched as a background looks better blurred). Parameter: radius.</summary>
    internal class BlurWhenConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? new Avalonia.Media.BlurEffect { Radius = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 30 } : null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}

namespace gamevault.Converter
{
    internal static class LibraryStatic
    {
        /// <summary>Turns a chevron upside down while its panel is open.</summary>
        public static readonly IValueConverter FlipWhenTrue = new FuncValueConverter<bool, Avalonia.Media.ITransform?>(open => open ? new Avalonia.Media.RotateTransform(180) : null);
        public static readonly IValueConverter IsPositive = new FuncValueConverter<object?, bool>(v => v is int i && i > 0);
        public static readonly IValueConverter FlipWhenFalse = new FuncValueConverter<bool?, Avalonia.Media.ITransform?>(open => open == true ? null : new Avalonia.Media.RotateTransform(-90));
    }
}

namespace gamevault.Converter
{
    /// <summary>The game is in early access (its own flag or the metadata's). An optional second value hides it (false).</summary>
    internal class EarlyAccessConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count > 1 && values[1] is false)
                return false;
            return values.FirstOrDefault() is Game game && (game.EarlyAccess == true || game.Metadata?.EarlyAccess == true);
        }
    }
}

namespace gamevault.Converter
{
    /// <summary>The platform icon of a game type (Windows / Linux), null when unknown.</summary>
    internal class GameTypeIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? key = value switch
            {
                GameType.WINDOWS_SETUP or GameType.WINDOWS_PORTABLE => "IconPlatformWindows",
                GameType.LINUX_PORTABLE => "IconPlatformLinux",
                _ => null,
            };
            return key != null && Avalonia.Application.Current!.TryGetResource(key, null, out object? geometry) ? geometry : null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}

namespace gamevault.Converter
{
    /// <summary>"1 game" / "3 games": parameter "singular|plural", both English texts with {0}, translated.</summary>
    internal class CountConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            int count = value is int i ? i : 0;
            string text = parameter as string ?? "{0}|{0}";
            // "{}" escapes the braces in XAML
            string[] forms = (text.StartsWith("{}") ? text[2..] : text).Split('|');
            return Loc.F(count == 1 || count == 0 && culture.TwoLetterISOLanguageName == "fr" ? forms[0] : forms[^1], count);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}
