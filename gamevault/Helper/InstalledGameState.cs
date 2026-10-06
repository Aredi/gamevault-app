using GameVault.Core;
using GameVault.Core.Library;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>Which server build of a game is installed (stored in its gamevault-exec) and whether a newer one exists.</summary>
    internal static class InstalledGameState
    {
        private static string SettingsFile(string installationDirectory) => Path.Combine(installationDirectory, "gamevault-exec");

        public static InstalledVersion Read(string installationDirectory)
        {
            string file = SettingsFile(installationDirectory);
            if (!File.Exists(file))
                return new InstalledVersion(null, null, null);
            static string? Value(string text) => string.IsNullOrEmpty(text) ? null : text;
            return new InstalledVersion(
                Value(Preferences.Get(AppConfigKey.InstalledGameVersion, file)),
                Value(Preferences.Get(AppConfigKey.InstalledGameFile, file)),
                Value(Preferences.Get(AppConfigKey.InstalledGameSize, file)));
        }

        /// <summary>Records <paramref name="game"/> (as the server sends it now) as the installed build.</summary>
        public static void Record(string installationDirectory, Game game)
        {
            string file = SettingsFile(installationDirectory);
            Preferences.Set(AppConfigKey.InstalledGameVersion, game.Version ?? "", file);
            Preferences.Set(AppConfigKey.InstalledGameFile, game.Path ?? "", file);
            Preferences.Set(AppConfigKey.InstalledGameSize, game.Size ?? "", file);
        }

        public static bool HasUpdate(Game game, string installationDirectory)
        {
            if (game == null || game.DeletedAt != null || !Directory.Exists(installationDirectory))
                return false;
            InstalledVersion installed = Read(installationDirectory);
            if (installed.Version == null && installed.FilePath == null && installed.Size == null)
            {
                // Installed before builds were recorded: taken as the current one
                Record(installationDirectory, game);
                return false;
            }
            return GameUpdates.IsUpdateAvailable(installed, game.Version, game.Path, game.Size);
        }

        /// <summary>Installed games whose server build changed.</summary>
        public static Game[] GamesWithUpdates() => InstallViewModel.Instance.InstalledGames
            .Where(g => g.Key != null && HasUpdate(g.Key, g.Value))
            .Select(g => g.Key)
            .ToArray();
    }
}
