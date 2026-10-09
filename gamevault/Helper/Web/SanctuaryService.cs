namespace gamevault.Helper
{
    /// <summary>
    /// The SanctuaryVault service next to the server: it receives the games published from the client and keeps the
    /// players' profiles. Its address is fixed, so nobody has to enter it.
    /// </summary>
    internal static class SanctuaryService
    {
        public const string Url = "https://gamevaultupload.alexisdominguez.fr";

        /// <summary>The address used: <see cref="Url"/>, except in the tests (their fake server).</summary>
        internal static string Current { get; set; } = Url;
    }
}
