using gamevault.Localization;
using GameVault.Core;
using gamevault.Helper.Platform;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Desktop shortcuts that start a game through the gamevault:// protocol (Windows: .url, Linux: .desktop).
    /// </summary>
    public static class DesktopHelper
    {
        private static string ShortcutPath(Game game)
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktopDir))
                desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            string safeTitle = string.Concat(game.Title.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(desktopDir, safeTitle + (PlatformInfo.IsWindows ? ".url" : ".desktop"));
        }

        public static async Task CreateShortcut(Game game, string iconPath, bool ask)
        {
            try
            {
                string shortcutPath = ShortcutPath(game);
                if (File.Exists(shortcutPath))
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Desktop shortcut already exists");
                    return;
                }
                if (ask)
                {
                    MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Do you want to create a desktop shortcut for {0}?", game.Title), "",
                    MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });

                    if (result != MessageDialogResult.Affirmative)
                        return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
                if (PlatformInfo.IsWindows)
                {
                    using StreamWriter writer = new StreamWriter(shortcutPath);
                    writer.Write("[InternetShortcut]\r\n");
                    writer.Write($"URL=gamevault://start?gameid={game.ID}\r\n");
                    writer.Write("IconIndex=0\r\n");
                    writer.Write("IconFile=" + iconPath.Replace('\\', '/') + "\r\n");
                    writer.Flush();
                }
                else
                {
                    // Windows executables carry no icon a Linux desktop can show, so the cached cover is used.
                    string icon = CacheHelper.GetImageCacheForGame(game).GetValueOrDefault("gbox") ?? DesktopEntry.IconPath;
                    File.WriteAllText(shortcutPath, DesktopEntry.Create(
                        name: game.Title,
                        exec: $"{DesktopEntry.Quote(PlatformInfo.ExecutablePath)} start --gameid={game.ID}",
                        comment: Loc.F("Play {0} with SanctuaryVault", game.Title),
                        icon: icon,
                        extra: "Categories=Game;\n"));
                    PlatformInfo.MakeExecutable(shortcutPath);
                    TrustDesktopFile(shortcutPath);
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        /// <summary>
        /// GNOME/Nautilus only launches desktop files marked as trusted.
        /// </summary>
        private static void TrustDesktopFile(string path)
        {
            try
            {
                string? gio = PlatformInfo.FindInPath("gio");
                if (gio == null)
                    return;
                var info = new ProcessStartInfo(gio) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (string arg in new[] { "set", path, "metadata::trusted", "true" })
                    info.ArgumentList.Add(arg);
                Process.Start(info)?.WaitForExit(3000);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        public static void RemoveShotcut(Game game)
        {
            try
            {
                string shortcutPath = ShortcutPath(game);
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        public static bool ShortcutExists(Game game)
        {
            return File.Exists(ShortcutPath(game));
        }
    }
}
