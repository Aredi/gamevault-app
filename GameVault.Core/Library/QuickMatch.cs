using System.Globalization;
using System.Text;

namespace GameVault.Core.Library
{
    /// <summary>
    /// How well a typed text matches a name, for the quick search (Ctrl+K): case and accents are ignored, the letters
    /// may be spread ("rdr" finds "Red Dead Redemption"), whole words and beginnings count more.
    /// </summary>
    public static class QuickMatch
    {
        /// <summary>0 when the text does not match, otherwise higher for better matches.</summary>
        public static int Score(string? query, string? candidate)
        {
            string q = Normalize(query);
            string c = Normalize(candidate);
            if (q.Length == 0 || c.Length == 0)
                return 0;
            if (c == q)
                return 1000;
            if (c.StartsWith(q, StringComparison.Ordinal))
                return 800 - Math.Min(c.Length - q.Length, 100);
            int index = c.IndexOf(q, StringComparison.Ordinal);
            if (index > 0)
                return (IsWordStart(c, index) ? 600 : 400) - Math.Min(index, 100);

            // Letters in order, preferring word beginnings ("bg3" in "baldur's gate 3")
            int score = 0, position = 0, streak = 0;
            foreach (char letter in q)
            {
                if (letter == ' ')
                    continue;
                int found = c.IndexOf(letter, position);
                if (found < 0)
                    return 0;
                streak = found == position ? streak + 1 : 0;
                score += IsWordStart(c, found) ? 12 : 2 + streak * 3;
                position = found + 1;
            }
            return Math.Min(score, 300);
        }

        private static bool IsWordStart(string text, int index) => index == 0 || !char.IsLetterOrDigit(text[index - 1]);

        /// <summary>Lower case without accents: "Élan" and "elan" are the same.</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            var builder = new StringBuilder(text.Length);
            foreach (char ch in text.Trim().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(char.ToLowerInvariant(ch));
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
