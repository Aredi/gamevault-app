using GameVault.Core;
using GameVault.Core.Storage;
using gamevault.Helper.Integrations;
using gamevault.Localization;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>Moves an installed game to another game folder (another disk for example).</summary>
    internal static class GameMover
    {
        private static readonly HashSet<int> moving = new();

        /// <summary>Playing or updating a game being moved would use files that are going away.</summary>
        public static bool IsMoving(int gameId) => moving.Contains(gameId);

        /// <summary>Asks where to, then moves the game. False when nothing was moved.</summary>
        public static async Task<bool> MoveAsync(Game game)
        {
            KeyValuePair<Game, string> installed = InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key.ID == game.ID);
            string? installation = installed.Value;
            if (installation == null || !Directory.Exists(installation))
                return false;
            if (moving.Contains(game.ID))
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is already being moved", game.Title);
                return false;
            }
            if (DownloadsViewModel.Instance.DownloadedGames.Any(d => d.IsGameIdDownloading(game.ID)))
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is being downloaded: move it once the download is finished", game.Title);
                return false;
            }
            long.TryParse(game.Size, out long size);
            var picker = new InstallLocationUserControl(size, installation);
            MainWindowViewModel.Instance.OpenPopup(picker);
            string root = await picker.SelectInstallLocation();
            if (root == string.Empty)
                return false;
            return await MoveToAsync(game, installation, root);
        }

        internal static async Task<bool> MoveToAsync(Game game, string installation, string root)
        {
            if (GameTimeTracker.IsRunning(game.ID, installation))
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Close '{0}' before moving it", game.Title);
                return false;
            }
            if (!moving.Add(game.ID))
                return false;
            try
            {
                int shown = -1;
                var progress = new Progress<double>(p =>
                {
                    int percent = (int)(p * 100);
                    if (percent == shown)
                        return;
                    shown = percent;
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Moving '{0}'… {1}%", game.Title, percent);
                });
                MainWindowViewModel.Instance.AppBarText = Loc.F("Moving '{0}'… {1}%", game.Title, 0);
                string moved = await Task.Run(() => InstallationMover.MoveAsync(installation, root, progress));
                Relocate(game, installation, moved);
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' has been moved to {1}", game.Title, root);
                return true;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' could not be moved: {1}", game.Title, ex.Message);
                return false;
            }
            finally
            {
                moving.Remove(game.ID);
            }
        }

        /// <summary>The chosen program is stored with its full path, and the library points to the new folder.</summary>
        private static void Relocate(Game game, string from, string to)
        {
            string settings = Path.Combine(to, "gamevault-exec");
            try
            {
                string executable = Preferences.Get(AppConfigKey.Executable, settings);
                string prefix = Path.TrimEndingDirectorySeparator(from) + Path.DirectorySeparatorChar;
                if (executable.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    string newExecutable = Path.Combine(to, executable.Substring(prefix.Length));
                    Preferences.Set(AppConfigKey.Executable, newExecutable, settings);
                    // The Windows shortcut shows the icon of the program
                    if (OperatingSystem.IsWindows() && DesktopHelper.ShortcutExists(game))
                    {
                        DesktopHelper.RemoveShotcut(game);
                        _ = DesktopHelper.CreateShortcut(game, newExecutable, false);
                    }
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            var games = InstallViewModel.Instance.InstalledGames;
            int index = games.ToList().FindIndex(g => g.Key.ID == game.ID);
            if (index >= 0)
                games[index] = new KeyValuePair<Game, string>(games[index].Key, to);
        }
    }
}
