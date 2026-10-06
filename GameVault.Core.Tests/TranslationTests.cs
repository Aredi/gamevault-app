using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVault.Core.Tests
{
    /// <summary>Every text the interface marks as translatable has a French translation with the same placeholders.</summary>
    public class TranslationTests
    {
        private static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "gamevault.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("gamevault.sln not found");
        }

        private static string App => Path.Combine(RepositoryRoot(), "gamevault");

        private static IEnumerable<string> Files(string pattern) =>
            Directory.EnumerateFiles(App, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        private static string DecodeCSharp(string literal) => Regex.Replace(literal, @"\\(.)", m => m.Groups[1].Value switch
        {
            "n" => "\n", "r" => "\r", "t" => "\t", "0" => "\0", var other => other,
        });

        /// <summary>Texts marked in the code: {l:T '...'} in XAML, Loc.T("...") and Loc.F("...") in C#.</summary>
        private static HashSet<string> MarkedTexts()
        {
            var texts = new HashSet<string>();
            foreach (string file in Files("*.axaml"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{l:T '((?:[^'\\]|\\.)*)'\}"))
                    texts.Add(Regex.Replace(WebUtility.HtmlDecode(m.Groups[1].Value), @"\\(.)", "$1"));
            foreach (string file in Files("*.cs"))
            {
                string code = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(code, @"Loc\.[TF]\(""((?:[^""\\]|\\.)*)"""))
                    texts.Add(DecodeCSharp(m.Groups[1].Value));
                // Shown translated through LocConverter / the enum converters
                foreach (Match m in Regex.Matches(code, @"\bState = ""([^""]+)"""))
                    texts.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(code, @"\[Description\(""([^""]+)""\)\]"))
                    texts.Add(m.Groups[1].Value);
            }
            return texts;
        }

        private static Dictionary<string, string> French() =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(App, "Localization", "fr.json")))!;

        [Fact]
        public void EveryMarkedText_HasAFrenchTranslation()
        {
            var french = French();
            var missing = MarkedTexts().Where(text => !french.ContainsKey(text)).OrderBy(t => t).ToList();
            Assert.True(missing.Count == 0, "Missing in Localization/fr.json:\n" + string.Join("\n", missing.Select(t => JsonSerializer.Serialize(t))));
        }

        [Fact]
        public void Translations_KeepThePlaceholders()
        {
            static List<string> Placeholders(string text) => Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).OrderBy(p => p).ToList();
            var wrong = French().Where(pair => !Placeholders(pair.Key).SequenceEqual(Placeholders(pair.Value))).Select(pair => pair.Key).ToList();
            Assert.True(wrong.Count == 0, "Placeholders differ:\n" + string.Join("\n", wrong));
        }

        [Fact]
        public void TheListsShownInTheLibrary_AreTranslated()
        {
            var french = French();
            foreach (string text in new[] { "Title", "Size", "Date Added", "Release Date", "Rating", "Download Count", "Average Playtime", "Last Played", "My Playtime", "All games", "Played", "Never played", "System" })
                Assert.True(french.ContainsKey(text), text);
        }
    }
}
