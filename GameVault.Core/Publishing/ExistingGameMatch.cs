using GameVault.Core.Library;

namespace GameVault.Core.Publishing
{
    /// <summary>A game of the server as "Publish a Game" compares it: its title, version and year tag.</summary>
    public sealed record ServerGameTitle(string Title, string? Version, int? Year);

    /// <summary>
    /// How the GameVault server will index a published file: it adds the file as a new version of an existing game only
    /// when the title read in the file name is exactly that game's title (letter case included) and the year tags agree.
    /// Otherwise it creates a second game.
    /// </summary>
    public static class ExistingGameMatch
    {
        /// <summary>The title the server reads in the file name built by <see cref="GameFileName.Build"/>.</summary>
        public static string ServerTitle(string title)
        {
            string name = GameFileName.Build(title, null, PublishedGameType.WindowsPortable, null, false, "");
            return name[..name.LastIndexOf(" (", StringComparison.Ordinal)].Trim();
        }

        /// <summary>The file becomes a version of <paramref name="existing"/> (not a second game).</summary>
        public static bool MergesWith(string title, int? year, ServerGameTitle existing)
        {
            if (string.IsNullOrWhiteSpace(title) || ServerTitle(title) != existing.Title)
                return false;
            // Year-tagged files only join the same year; files without a year join any game of that title
            return year == null || year == existing.Year;
        }

        /// <summary>Same game written differently ("Escape from duckov" / "Escape from Duckov"): a second game would be created.</summary>
        public static bool SpelledDifferently(string title, ServerGameTitle existing) =>
            !string.IsNullOrWhiteSpace(title) && ServerTitle(title) != existing.Title
            && TitleMatching.Normalize(title) == TitleMatching.Normalize(existing.Title);

        /// <summary>Close enough to be offered as "a new version of this game?".</summary>
        public static bool IsCandidate(string title, ServerGameTitle existing)
        {
            string a = TitleMatching.Normalize(title), b = TitleMatching.Normalize(existing.Title);
            if (a.Length < 2 || b.Length == 0)
                return false;
            return a == b || b.StartsWith(a + " ", StringComparison.Ordinal) || TitleMatching.JaroWinkler(a, b) >= 0.9;
        }

        /// <summary>The version tag the file will have ("2.0" → "v2.0"), as the server reads it.</summary>
        public static string? VersionTag(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            return "v" + version.Trim().TrimStart('v', 'V').Replace('(', '[').Replace(')', ']');
        }
    }
}
