namespace GameVault.Core.Integrations
{
    /// <summary>What Discord shows under "Playing SanctuaryVault": the game, its total play time, the session's timer.</summary>
    public sealed record DiscordActivity(string Details, string? State, DateTime SessionStartUtc, string LargeImage, string LargeImageText)
    {
        /// <summary>Asset uploaded to the Discord application, used when the game has no public cover.</summary>
        public const string LogoAsset = "logo";
        public const int MaxText = 128;
        public const int MaxImageUrl = 256;

        /// <param name="minutesBefore">Play time of the game before this session.</param>
        /// <param name="coverSource">Where the cover comes from (IGDB, ...): shown when it is a public https address.</param>
        /// <param name="playTime">Turns the total minutes into the state line ("34 h played").</param>
        public static DiscordActivity Create(string title, int minutesBefore, DateTime sessionStartUtc, DateTime nowUtc, string? coverSource, Func<int, string> playTime)
        {
            int total = Math.Max(0, minutesBefore) + Math.Max(0, (int)(nowUtc - sessionStartUtc).TotalMinutes);
            string details = Fit(string.IsNullOrWhiteSpace(title) ? "?" : title.Trim());
            string? state = total > 0 ? Fit(playTime(total)) : null;
            string image = IsPublicImage(coverSource) ? coverSource! : LogoAsset;
            return new DiscordActivity(details, state, sessionStartUtc, image, details);
        }

        /// <summary>Discord refuses texts under 2 or over 128 characters.</summary>
        public static string Fit(string text)
        {
            if (text.Length > MaxText)
                text = text[..(MaxText - 1)] + "…";
            return text.Length < 2 ? text.PadRight(2) : text;
        }

        public static bool IsPublicImage(string? url) =>
            !string.IsNullOrEmpty(url) && url.Length <= MaxImageUrl
            && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
            && !uri.IsLoopback && uri.HostNameType == UriHostNameType.Dns;
    }
}
