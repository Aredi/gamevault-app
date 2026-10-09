namespace GameVault.Uploader
{
    public sealed class UploaderOptions
    {
        public static string Version => typeof(UploaderOptions).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        /// <summary>GAMEVAULT_URL: the GameVault server that says who is an administrator (e.g. http://gamevault:8080).</summary>
        public required string GameVaultUrl { get; init; }
        /// <summary>FILES_DIRECTORY: the folder mounted as /files in the GameVault server.</summary>
        public required string FilesDirectory { get; init; }
        /// <summary>PROFILES_DIRECTORY: where the players' profiles are kept (a volume, e.g. /data/profiles).</summary>
        public string ProfilesDirectory { get; init; } = "/data/profiles";

        public static UploaderOptions From(IConfiguration configuration)
        {
            string server = configuration["GAMEVAULT_URL"] ?? throw new InvalidOperationException("GAMEVAULT_URL is not set (the address of the GameVault server, e.g. http://gamevault-backend:8080).");
            string files = configuration["FILES_DIRECTORY"] ?? "/files";
            if (!Directory.Exists(files))
                throw new InvalidOperationException($"FILES_DIRECTORY '{files}' does not exist: mount the games folder of the GameVault server there.");
            return new UploaderOptions { GameVaultUrl = server, FilesDirectory = files, ProfilesDirectory = configuration["PROFILES_DIRECTORY"] ?? "/data/profiles" };
        }
    }
}
