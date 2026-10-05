namespace GameVault.Core
{
    public static class VersionHelper
    {
        /// <summary>
        /// Parses tags like "1.17.2", "v1.17.2.0" or "15.0.0-beta". Returns null if the string is not a version.
        /// </summary>
        public static Version? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string text = value.Trim().TrimStart('v', 'V');
            int suffix = text.IndexOfAny(new[] { '-', '+', ' ' });
            if (suffix >= 0)
                text = text.Substring(0, suffix);
            if (!text.Contains('.'))
                text += ".0";

            return Version.TryParse(text, out Version? version) ? Normalize(version) : null;
        }

        /// <summary>
        /// True when <paramref name="candidate"/> is strictly newer than <paramref name="current"/>.
        /// Unparseable input is never considered newer.
        /// </summary>
        public static bool IsNewer(string? candidate, string? current)
        {
            Version? a = Parse(candidate);
            Version? b = Parse(current);
            return a != null && b != null && a > b;
        }

        // "1.2" and "1.2.0.0" must compare equal; System.Version treats missing parts as -1.
        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }
    }
}
