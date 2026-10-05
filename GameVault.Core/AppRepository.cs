namespace GameVault.Core
{
    /// <summary>
    /// Where this build of the client is published. Update checks and "download" links point here,
    /// so that a fork never offers to replace itself with an upstream build.
    /// </summary>
    public static class AppRepository
    {
        public const string Owner = "Aredi";
        public const string Name = "gamevault-app";

        public const string UpstreamOwner = "Phalcode";

        public static string ReleasesPage => $"https://github.com/{Owner}/{Name}/releases";
        // The list endpoint returns [] while the repository has no release yet (".../releases/latest" answers 404).
        public static string ReleasesApi => $"https://api.github.com/repos/{Owner}/{Name}/releases?per_page=10";
    }
}
