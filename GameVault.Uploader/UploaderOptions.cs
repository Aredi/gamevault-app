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
        /// <summary>SAVEFILES_DIRECTORY: the savefiles volume of the GameVault server, mounted read-only (save history).</summary>
        public string SavefilesDirectory { get; init; } = "/savefiles";
        /// <summary>REMOTE_INSTALLS_DIRECTORY: the games each player asked to install on their PC (a volume, e.g. /data/remote).</summary>
        public string RemoteInstallsDirectory { get; init; } = "/data/remote";
        /// <summary>ROMM_COMM_URL: the requests API of the RomM-ComM Discord bot (e.g. http://romm-sync:8095); game requests are off without it.</summary>
        public string? RommCommUrl { get; init; }
        /// <summary>ROMM_COMM_API_KEY: the bot's REQUESTS_API_KEY.</summary>
        public string? RommCommApiKey { get; init; }

        public static UploaderOptions From(IConfiguration configuration)
        {
            string server = configuration["GAMEVAULT_URL"] ?? throw new InvalidOperationException("GAMEVAULT_URL is not set (the address of the GameVault server, e.g. http://gamevault-backend:8080).");
            string files = configuration["FILES_DIRECTORY"] ?? "/files";
            if (!Directory.Exists(files))
                throw new InvalidOperationException($"FILES_DIRECTORY '{files}' does not exist: mount the games folder of the GameVault server there.");
            return new UploaderOptions { GameVaultUrl = server, FilesDirectory = files, ProfilesDirectory = configuration["PROFILES_DIRECTORY"] ?? "/data/profiles", SavefilesDirectory = configuration["SAVEFILES_DIRECTORY"] ?? "/savefiles", RemoteInstallsDirectory = configuration["REMOTE_INSTALLS_DIRECTORY"] ?? "/data/remote", RommCommUrl = configuration["ROMM_COMM_URL"], RommCommApiKey = configuration["ROMM_COMM_API_KEY"] };
        }
    }
}
