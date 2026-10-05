using GameVault.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace gamevault.Helper
{
    /// <summary>
    /// Updates of installed builds (Windows Setup.exe / Linux AppImage made with Velopack): the new version is
    /// downloaded from the fork's GitHub releases (only the changed parts when possible) and applied on restart.
    /// Portable builds (zip/tar.gz, development) keep the "open the release page" update check.
    /// </summary>
    internal static class AppUpdater
    {
        private static UpdateManager? manager;

        private static UpdateManager Manager => manager ??= new UpdateManager(CreateSource());

        /// <summary>GAMEVAULT_UPDATE_SOURCE (folder or URL) replaces GitHub, to test updates.</summary>
        private static IUpdateSource CreateSource()
        {
            string? custom = Environment.GetEnvironmentVariable("GAMEVAULT_UPDATE_SOURCE");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                return Directory.Exists(custom)
                    ? new SimpleFileSource(new DirectoryInfo(custom))
                    : new SimpleWebSource(custom);
            }
            return new GithubSource(AppRepository.RepositoryUrl, null, false);
        }

        public static bool IsInstalled
        {
            get
            {
                try { return Manager.IsInstalled; }
                catch (Exception ex) { Log.Ignored(ex); return false; }
            }
        }

        public static string? CurrentVersion => IsInstalled ? Manager.CurrentVersion?.ToString() : null;

        /// <summary>The newer release, or null when up to date (or when GitHub cannot be reached).</summary>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                return await Manager.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>Downloads <paramref name="update"/>, then restarts GameVault on the new version.</summary>
        public static async Task DownloadAndRestartAsync(UpdateInfo update, Action<int>? progress = null)
        {
            await Manager.DownloadUpdatesAsync(update, progress);
            Log.Info($"Restarting to apply update {update.TargetFullRelease.Version}");
            Manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
    }
}
