using System;

namespace gamevault.Helper
{
    /// <summary>
    /// The SanctuaryVault service next to the server: it receives the games published from the client and keeps the
    /// players' profiles. Its address is fixed, so nobody has to enter it.
    /// </summary>
    internal static class SanctuaryService
    {
        public const string Url = "https://gamevaultupload.alexisdominguez.fr";

        /// <summary>The GameVault server, filled in on the sign-in page: a new player only types their account.</summary>
        public const string GameVaultUrl = "https://gamevault.alexisdominguez.fr";

        /// <summary>The page to send to a friend: download links and how to sign in.</summary>
        public const string InviteUrl = Url + "/invite";

        /// <summary>
        /// The address used: <see cref="Url"/>, except in the tests (their fake server) or when SANCTUARYVAULT_SERVICE_URL
        /// names a test service (a branch tried before its release).
        /// </summary>
        internal static string Current { get; set; } = Environment.GetEnvironmentVariable("SANCTUARYVAULT_SERVICE_URL") is { Length: > 0 } test ? test.TrimEnd('/') : Url;
    }
}
