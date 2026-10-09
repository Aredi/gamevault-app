using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GameVault.Core.Library
{
    /// <summary>A search result as the matching needs it.</summary>
    public sealed record TitleCandidate(string Title, DateTime? ReleaseDate);

    /// <summary>How well a candidate matches a game file: score from 0 to 1, and whether it is safe to take automatically.</summary>
    public sealed record TitleMatch(int Index, double Score, bool IsConfident);

    /// <summary>
    /// Finds the metadata entry of a game from its file name, the way RoMM matches ROMs (rules written again here,
    /// RoMM is AGPL): the title is the file name without its extension and its trailing tags, compared to each
    /// candidate after normalization (lower case, no articles, no punctuation, no accents) with the Jaro-Winkler
    /// similarity. A match needs 0.75, and a title that is only the start of the other (Portable Ops / Portable Ops
    /// Plus) is never taken automatically.
    /// </summary>
    public static class TitleMatching
    {
        public const double MinimumScore = 0.75;

        private static readonly Regex Extension = new(@"\.(?:tar\.(?:gz|bz2|xz)|[a-z0-9]{1,5})$", RegexOptions.IgnoreCase);
        private static readonly Regex TrailingTags = new(@"(?:\s*(?:\([^)]*\)|\[[^\]]*\]))+\s*$");
        private static readonly Regex SceneGroup = new(@"-[A-Za-z0-9]{2,12}$");
        private static readonly Regex TrailingVersion = new(@"\s+v\d+(?:[ .]\d+)*[a-z]?$", RegexOptions.IgnoreCase);
        private static readonly Regex YearTag = new(@"\((19[5-9]\d|20\d\d)\)");
        private static readonly Regex LeadingArticle = new(@"^(a|an|the)\b", RegexOptions.IgnoreCase);
        private static readonly Regex CommaArticle = new(@",\s(a|an|the)\b(?=\s*[^\w\s]|$)", RegexOptions.IgnoreCase);
        private static readonly Regex NonWord = new(@"[^\w\s]");
        private static readonly Regex Spaces = new(@"\s+");

        /// <summary>"Hades (v1.38) (W_P).zip" → "Hades": what to search the providers for.</summary>
        public static string SearchTerm(string? fileName)
        {
            string name = Path.GetFileName(fileName ?? "").Trim();
            name = Extension.Replace(name, "");
            name = TrailingTags.Replace(name, "").Trim();
            // Scene-style names: dots and underscores between words, the group at the end ("Game.Name-GROUP")
            if (!name.Contains(' ') && (name.Count(c => c == '.') >= 2 || name.Contains('_')))
            {
                name = SceneGroup.Replace(name, "");
                name = name.Replace('.', ' ').Replace('_', ' ');
            }
            name = Spaces.Replace(name.Replace('_', ' '), " ").Trim();
            // A version written into the name: "Hollow Knight v1.5"
            return TrailingVersion.Replace(name, "").Trim();
        }

        /// <summary>The release year GameVault reads from a "(2018)" tag, if any.</summary>
        public static int? Year(string? fileName)
        {
            Match match = YearTag.Match(Path.GetFileName(fileName ?? ""));
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>A provider id pinned in the file name, like "(igdb-1234)".</summary>
        public static string? PinnedId(string? fileName, string providerSlug)
        {
            Match match = Regex.Match(Path.GetFileName(fileName ?? ""), $@"\({Regex.Escape(providerSlug)}-(\d+)\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Lower case, without leading / trailing articles, punctuation and accents.</summary>
        public static string Normalize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";
            name = name.ToLowerInvariant().Replace('_', ' ');
            name = LeadingArticle.Replace(name, "");
            name = CommaArticle.Replace(name, "");
            name = NonWord.Replace(name, " ");
            name = Spaces.Replace(name, " ");
            var builder = new StringBuilder(name.Length);
            foreach (char c in name.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }
            return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
        }

        /// <summary>Jaro-Winkler similarity, 0 (nothing in common) to 1 (same).</summary>
        public static double JaroWinkler(string a, string b)
        {
            if (a == b)
                return 1;
            if (a.Length == 0 || b.Length == 0)
                return 0;
            int range = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);
            var aMatched = new bool[a.Length];
            var bMatched = new bool[b.Length];
            int matches = 0;
            for (int i = 0; i < a.Length; i++)
            {
                for (int j = Math.Max(0, i - range); j < Math.Min(b.Length, i + range + 1); j++)
                {
                    if (bMatched[j] || a[i] != b[j])
                        continue;
                    aMatched[i] = bMatched[j] = true;
                    matches++;
                    break;
                }
            }
            if (matches == 0)
                return 0;
            int transpositions = 0;
            for (int i = 0, k = 0; i < a.Length; i++)
            {
                if (!aMatched[i])
                    continue;
                while (!bMatched[k])
                    k++;
                if (a[i] != b[k])
                    transpositions++;
                k++;
            }
            double m = matches;
            double jaro = (m / a.Length + m / b.Length + (m - transpositions / 2.0) / m) / 3;
            int prefix = 0;
            while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix])
                prefix++;
            return jaro + prefix * 0.1 * (1 - jaro);
        }

        /// <summary>How well a candidate title matches the game file (the release year, when the file has one, counts a little).</summary>
        public static double Score(string fileName, TitleCandidate candidate)
        {
            double score = JaroWinkler(Normalize(SearchTerm(fileName)), Normalize(candidate.Title));
            int? year = Year(fileName);
            if (year != null && candidate.ReleaseDate != null)
                score += candidate.ReleaseDate.Value.Year == year ? 0.03 : -0.05;
            return Math.Clamp(score, 0, 1);
        }

        /// <summary>The best candidate, null when none reaches <see cref="MinimumScore"/>.</summary>
        public static TitleMatch? Best(string fileName, IReadOnlyList<TitleCandidate> candidates)
        {
            int best = -1;
            double bestScore = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                double score = Score(fileName, candidates[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best < 0 || bestScore < MinimumScore)
                return null;
            string[] term = Normalize(SearchTerm(fileName)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] title = Normalize(candidates[best].Title).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // "Portable Ops" for "Portable Ops Plus" (or the reverse) is likely another edition: a person decides
            bool onlyAPrefix = term.Length != title.Length
                && (term.Length < title.Length ? title.Take(term.Length).SequenceEqual(term) : term.Take(title.Length).SequenceEqual(title));
            bool exact = Normalize(SearchTerm(fileName)) == Normalize(candidates[best].Title);
            return new TitleMatch(best, bestScore, !onlyAPrefix && (exact || bestScore >= 0.9));
        }
    }
}
