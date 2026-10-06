using System.Text.RegularExpressions;

namespace GameVault.Core.Diagnostics
{
    /// <summary>
    /// Removes what must never leave the computer in a problem report: passwords, tokens, API keys and credentials
    /// in URLs. Applied to every text of the report, logs included.
    /// </summary>
    public static class Redactor
    {
        public const string Hidden = "[hidden]";

        private static readonly (Regex Pattern, string Replacement)[] Rules =
        {
            // Authorization headers
            (new Regex(@"\b(Bearer|Basic)\s+[A-Za-z0-9\-._~+/=]{8,}", RegexOptions.IgnoreCase), "$1 " + Hidden),
            // JSON web tokens anywhere (header.payload.signature)
            (new Regex(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}"), Hidden),
            // "password": "...", "access_token": "..." in JSON
            (new Regex(@"(""(?:[a-z_]*password|access_token|refresh_token|api_key|apikey|token|secret)""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase), "$1\"" + Hidden + "\""),
            // key=value lines and query parameters
            (new Regex(@"\b((?:[A-Za-z]*Password|SessionToken|ApiKey|api_key|access_token|refresh_token|token|secret)\s*[=:]\s*)[^\s&;,""]+", RegexOptions.IgnoreCase), "$1" + Hidden),
            // https://user:password@host
            (new Regex(@"(\b[a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase), "$1" + Hidden + "@"),
            // X-Otp and similar one-time values in logged headers
            (new Regex(@"\b(X-Otp\s*[:=]\s*)[A-Fa-f0-9]{16,}", RegexOptions.IgnoreCase), "$1" + Hidden),
        };

        public static string Redact(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            foreach (var (pattern, replacement) in Rules)
                text = pattern.Replace(text, replacement);
            return text;
        }

        /// <summary>The end of a log: the last <paramref name="maxBytes"/> bytes, starting at a whole line.</summary>
        public static string Tail(string file, int maxBytes)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > maxBytes)
                stream.Seek(-maxBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            if (stream.Length > maxBytes && text.IndexOf('\n') is int newline and >= 0)
                text = text[(newline + 1)..];
            return text;
        }
    }
}
