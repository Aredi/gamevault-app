using Avalonia.Markup.Xaml;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace gamevault.Localization
{
    /// <summary>
    /// Translations keyed by the English text: Localization/&lt;language&gt;.json maps each English text to its
    /// translation. A missing translation shows the English text.
    /// </summary>
    public static class Loc
    {
        public static readonly string[] Languages = { "", "en", "fr" };
        public static readonly string[] LanguageNames = { "System", "English", "Français" };

        private static Dictionary<string, string> texts = new();

        /// <summary>The active language ("en", "fr").</summary>
        public static string Language { get; private set; } = "en";

        /// <summary>"" follows the system language. Called before the first window loads.</summary>
        public static void Initialize(string? setting)
        {
            string language = string.IsNullOrEmpty(setting) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : setting;
            Language = Array.IndexOf(Languages, language) > 0 ? language : "en";
            texts = Language == "en" ? new Dictionary<string, string>() : Load(Language);
        }

        public static Dictionary<string, string> Load(string language)
        {
            try
            {
                using Stream? stream = typeof(Loc).Assembly.GetManifestResourceStream($"gamevault.Localization.{language}.json");
                if (stream != null)
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
            }
            catch (Exception ex) { Log.Error(ex, $"Could not load the {language} translation"); }
            return new Dictionary<string, string>();
        }

        /// <summary>The translation of <paramref name="english"/>.</summary>
        public static string T(string english) =>
            texts.TryGetValue(english, out string? translated) && !string.IsNullOrEmpty(translated) ? translated : english;

        /// <summary>The translation of a format text ("'{0}' is installed") filled with <paramref name="values"/>.</summary>
        public static string F(string englishFormat, params object?[] values)
        {
            try { return string.Format(CultureInfo.CurrentCulture, T(englishFormat), values); }
            catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, englishFormat, values); }
        }
    }

    /// <summary>{l:T 'English text'} in XAML.</summary>
    public class TExtension : MarkupExtension
    {
        public TExtension() { }
        public TExtension(string text) { Text = text; }

        public string Text { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            string text = Loc.T(Text);
            // "{}" escapes a format text in XAML attributes; given through the extension it must not stay
            return text.StartsWith("{}") ? text[2..] : text;
        }
    }
}
