using gamevault.Localization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// Theme files are WPF resource dictionaries (built-in ones and the community themes from
    /// Phalcode/gamevault-community-themes). Instead of loading them as XAML, their Color and String
    /// entries are read and mapped onto the Avalonia resources used by the app.
    /// </summary>
    internal static class ThemeManager
    {
        public const string BuiltInThemeBase = "avares://gamevault/Resources/Assets/Themes/";
        public const string DefaultTheme = BuiltInThemeBase + "ThemeDefaultDark.xaml";

        public static readonly string[] BuiltInThemes =
        {
            "ThemeDefaultDark.xaml",
            "ThemeDefaultLight.xaml",
            "ThemeClassicDark.xaml",
            "ThemePhalcodeDark.xaml",
            "ThemePhalcodeLight.xaml",
            "ThemeHalloweenDark.xaml",
            "ThemeChristmasDark.xaml",
        };

        public static string? CurrentThemePath { get; private set; }

        internal class ThemeDefinition
        {
            public Dictionary<string, Color> Colors { get; } = new Dictionary<string, Color>();
            public Dictionary<string, string> Strings { get; } = new Dictionary<string, string>();

            public string DisplayName => Strings.GetValueOrDefault("Theme.DisplayName", "Unnamed Theme");
            public string Description => Strings.GetValueOrDefault("Theme.Description", "");
            public string Author => Strings.GetValueOrDefault("Theme.Author", "");

            public Color Get(string key, Color fallback) => Colors.TryGetValue(key, out Color c) ? c : fallback;
        }

        /// <summary>
        /// Maps the paths stored by the WPF client (pack://application:,,,/gamevault;component/...) to Avalonia assets.
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                return BuiltInThemeBase + Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
            }
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(path).LocalPath;
            }
            return path;
        }

        public static bool IsBuiltIn(string path) => NormalizePath(path).StartsWith("avares://", StringComparison.OrdinalIgnoreCase);

        public static ThemeDefinition Load(string path)
        {
            path = NormalizePath(path);
            using Stream stream = path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? OpenBuiltIn(path)
                : File.OpenRead(path);
            return Parse(stream);
        }

        // Built-in themes are embedded resources (as .xaml files Avalonia would try to compile them)
        private static Stream OpenBuiltIn(string path)
        {
            string name = "Themes/" + Path.GetFileName(new Uri(path).AbsolutePath);
            return typeof(ThemeManager).Assembly.GetManifestResourceStream(name)
                ?? throw new FileNotFoundException(Loc.F("Built-in theme '{0}' not found", name));
        }

        public static ThemeDefinition Parse(Stream stream)
        {
            var definition = new ThemeDefinition();
            XDocument doc = XDocument.Load(stream);
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (XElement element in doc.Root!.Elements())
            {
                string? key = element.Attribute(xaml + "Key")?.Value;
                if (key == null)
                    continue;

                string value = element.Value.Trim();
                if (element.Name.LocalName == "Color")
                {
                    if (TryParseColor(value, out Color color))
                        definition.Colors[key] = color;
                }
                else if (element.Name.LocalName is "String" or "Int32")
                {
                    definition.Strings[key] = value;
                }
            }
            return definition;
        }

        private static Color Mix(Color a, Color b, double amount) => Color.FromArgb(
            a.A,
            (byte)(a.R + (b.R - a.R) * amount),
            (byte)(a.G + (b.G - a.G) * amount),
            (byte)(a.B + (b.B - a.B) * amount));

        private static bool TryParseColor(string value, out Color color)
        {
            if (Color.TryParse(value, out color))
                return true;
            // WPF accepts names in any casing ("black"), Avalonia's parser is case sensitive for names.
            var known = typeof(Colors).GetProperties().FirstOrDefault(p => p.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (known != null)
            {
                color = (Color)known.GetValue(null)!;
                return true;
            }
            return false;
        }

        public static void ApplyDefault() => Apply(DefaultTheme);

        public static void Apply(string path)
        {
            ThemeDefinition theme;
            try
            {
                theme = Load(path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to load theme {path}");
                if (path == DefaultTheme)
                    throw;
                theme = Load(DefaultTheme);
                path = DefaultTheme;
            }
            Apply(theme);
            CurrentThemePath = NormalizePath(path);
        }

        public static void Apply(ThemeDefinition theme)
        {
            Application app = Application.Current!;
            var res = app.Resources;

            Color foreground = theme.Get("GameVault.Colors.Foreground", Color.Parse("#FFF6F5FA"));
            Color foreground2 = theme.Get("GameVault.Colors.Foreground2", foreground);
            Color background = theme.Get("GameVault.Colors.Background", Color.Parse("#FF11101E"));
            Color background2 = theme.Get("GameVault.Colors.Background2", Color.Parse("#FF171528"));
            Color accent = theme.Get("GameVault.Colors.Accent", Color.Parse("#FF4F46AF"));
            Color accent2 = theme.Get("GameVault.Colors.Accent2", WithAlpha(accent, 0x99));
            Color accent3 = theme.Get("GameVault.Colors.Accent3", WithAlpha(accent, 0x66));
            Color accent4 = theme.Get("GameVault.Colors.Accent4", WithAlpha(accent, 0x33));
            Color border = theme.Get("GameVault.Colors.Border", accent);
            Color blur = theme.Get("GameVault.Colors.Blur", Colors.Black);

            res["GameVault.Colors.Foreground"] = foreground;
            res["GameVault.Colors.Foreground2"] = foreground2;
            res["GameVault.Colors.Background"] = background;
            res["GameVault.Colors.Background2"] = background2;
            res["GameVault.Colors.Accent"] = accent;
            res["GameVault.Colors.Accent2"] = accent2;
            res["GameVault.Colors.Accent3"] = accent3;
            res["GameVault.Colors.Accent4"] = accent4;
            res["GameVault.Colors.Border"] = border;
            res["GameVault.Colors.Blur"] = blur;

            res["Brush.Foreground"] = new SolidColorBrush(foreground);
            res["Brush.Foreground2"] = new SolidColorBrush(foreground2);
            res["Brush.Background"] = new SolidColorBrush(background);
            res["Brush.Background2"] = new SolidColorBrush(background2);
            res["Brush.Accent"] = new SolidColorBrush(accent);
            res["Brush.Accent2"] = new SolidColorBrush(accent2);
            res["Brush.Accent3"] = new SolidColorBrush(accent3);
            res["Brush.Accent4"] = new SolidColorBrush(accent4);
            res["Brush.Border"] = new SolidColorBrush(border);
            res["Brush.Blur"] = new SolidColorBrush(blur, 0.7);
            res["Brush.Muted"] = new SolidColorBrush(WithAlpha(foreground, 0x99));

            res["GameVault.Brushes.Button"] = new SolidColorBrush(accent);
            res["GameVault.Brushes.Button.MouseOver"] = new SolidColorBrush(accent2);
            res["GameVault.Brushes.Button.Focus"] = new SolidColorBrush(accent3);
            res["GameVault.Brushes.Button.Disabled"] = new SolidColorBrush(accent4);
            res["GameVault.Brushes.Blur"] = new SolidColorBrush(blur, 0.7);

            bool isLight = Luminance(background) > 0.5;
            // Library and game pages: gradients that fade artwork into the page, hairlines, state colors
            res["GameVault.Colors.BackgroundClear"] = WithAlpha(background, 0x00);
            res["GameVault.Colors.BackgroundSoft"] = WithAlpha(background, 0x99);
            res["GameVault.Colors.BackgroundStrong"] = WithAlpha(background, 0xE6);
            res["Brush.Surface"] = new SolidColorBrush(background2);
            res["Brush.Surface3"] = new SolidColorBrush(Mix(background2, foreground, isLight ? 0.06 : 0.07));
            res["Brush.Line"] = new SolidColorBrush(WithAlpha(foreground, 0x1F));
            res["Brush.Glass"] = new SolidColorBrush(isLight ? Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x8C, 0x0B, 0x0A, 0x15));
            res["Brush.Success"] = new SolidColorBrush(isLight ? Color.Parse("#1E8A55") : Color.Parse("#5FD39A"));
            res["Brush.Warning"] = new SolidColorBrush(isLight ? Color.Parse("#B85E12") : Color.Parse("#F2A65A"));
            res["Brush.Special"] = new SolidColorBrush(isLight ? Color.Parse("#7A3FC2") : Color.Parse("#D6B4FF"));
            app.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;
            ApplyFluentPalette(app, isLight ? ThemeVariant.Light : ThemeVariant.Dark, foreground, background, background2, accent, border);
        }

        /// <summary>
        /// Makes the standard Fluent controls (TextBox, ComboBox, CheckBox, ...) follow the GameVault theme,
        /// like Base.xaml did for the MahApps controls.
        /// </summary>
        private static void ApplyFluentPalette(Application app, ThemeVariant variant, Color foreground, Color background, Color background2, Color accent, Color border)
        {
            FluentTheme? fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
            if (fluent == null)
                return;

            var palette = new ColorPaletteResources
            {
                Accent = accent,
                RegionColor = background,
                AltHigh = background,
                AltLow = WithAlpha(background, 0x33),
                AltMedium = WithAlpha(background, 0x99),
                AltMediumHigh = WithAlpha(background, 0xCC),
                AltMediumLow = WithAlpha(background, 0x66),
                BaseHigh = foreground,
                BaseLow = WithAlpha(foreground, 0x33),
                BaseMedium = WithAlpha(foreground, 0x99),
                BaseMediumHigh = WithAlpha(foreground, 0xCC),
                BaseMediumLow = WithAlpha(foreground, 0x66),
                ChromeAltLow = WithAlpha(foreground, 0xCC),
                ChromeBlackHigh = Colors.Black,
                ChromeBlackLow = WithAlpha(Colors.Black, 0x33),
                ChromeBlackMedium = WithAlpha(Colors.Black, 0xCC),
                ChromeBlackMediumLow = WithAlpha(Colors.Black, 0x66),
                ChromeDisabledHigh = WithAlpha(foreground, 0x33),
                ChromeDisabledLow = WithAlpha(foreground, 0x99),
                ChromeGray = border,
                ChromeHigh = border,
                ChromeLow = background2,
                ChromeMedium = background2,
                ChromeMediumLow = background,
                ChromeWhite = foreground,
                ListLow = WithAlpha(foreground, 0x19),
                ListMedium = WithAlpha(foreground, 0x33),
                ErrorText = Colors.IndianRed,
            };
            fluent.Palettes[variant] = palette;
        }

        public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

        private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255d;
    }
}
