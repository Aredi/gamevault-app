using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The cloud saves of the retro games, per game as the library's: taken before a game when another PC sent newer
    /// ones, sent after it, and from its page sent, taken back or restored from an older version.
    /// </summary>
    internal static class RetroCloudSaves
    {
        /// <summary>The game's name on the SanctuaryVault service: "nes-40".</summary>
        public static string SetOf(RetroGame game) => $"{game.Platform.Slug}-{game.Id}";

        public static RetroGameSaves Of(RetroGame game)
        {
            var layout = RetroArchManager.Layout;
            string player = RetroArchManager.Player;
            return new RetroGameSaves(layout.SavesDirectory(player), layout.StatesDirectory(player), layout.SaveBackupsDirectory(player), SetOf(game),
                Path.GetFileNameWithoutExtension(game.RomFile ?? game.FileName));
        }

        /// <summary>Before a game: its saves from another PC, when they are newer. Without network or with the cloud saves off, the ones here.</summary>
        /// <returns>Where this PC's own saves were kept when they had changed too; null otherwise.</returns>
        public static async Task<string?> TakeAsync(RetroGame game, Action<string> status)
        {
            if (!SettingsViewModel.Instance.CloudSaves)
                return null;
            RetroGameSaves saves = Of(game);
            try
            {
                if (await RetroService.SavesInfoAsync(saves.Set) is not long savedAt || !saves.IsOlderThan(savedAt))
                    return null;
                status(Loc.T("Getting the saves from the other PCs..."));
                byte[] zip = await RetroService.DownloadSavesAsync(saves.Set);
                return await Task.Run(() =>
                {
                    bool changedHere = saves.ChangedSinceSync;
                    string? backup = saves.Unpack(new MemoryStream(zip));
                    saves.Remember(savedAt);
                    return changedHere ? backup : null;
                });
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>After a game (or from its page): its saves to the service when they changed, or always when forced.</summary>
        /// <returns>What happened, for the page.</returns>
        public static async Task<string> SendAsync(RetroGame game, bool force = false)
        {
            if (!SettingsViewModel.Instance.CloudSaves && !force)
                return "";
            RetroGameSaves saves = Of(game);
            if (!await Task.Run(() => saves.HasFiles))
                return Loc.T("No save of this game on this PC yet.");
            if (!force && !await Task.Run(() => saves.ChangedSinceSync))
                return "";
            string zip = Path.Combine(RetroArchManager.Layout.Directory, $".saves-{saves.Set}.zip");
            try
            {
                await Task.Run(() =>
                {
                    using FileStream target = File.Create(zip);
                    saves.Pack(target);
                });
                long savedAt;
                await using (FileStream source = File.OpenRead(zip))
                    savedAt = await RetroService.UploadSavesAsync(saves.Set, source);
                saves.Remember(savedAt);
                return Loc.F("The saves of '{0}' are in the cloud", game.Name);
            }
            catch (Exception ex)
            {
                // Sent after the next game
                Log.Ignored(ex);
                return Loc.F("The saves of '{0}' could not be sent: {1}", game.Name, WebExceptionHelper.TryGetServerMessage(ex));
            }
            finally { File.Delete(zip); }
        }

        /// <summary>From the game's page: the cloud's current saves, or an older version, in place of this PC's (kept in a backup).</summary>
        /// <param name="version">Null: the current one.</param>
        public static async Task<string> RestoreAsync(RetroGame game, RetroService.SavesVersion? version = null)
        {
            RetroGameSaves saves = Of(game);
            try
            {
                long savedAt;
                byte[] zip;
                if (version == null)
                {
                    if (await RetroService.SavesInfoAsync(saves.Set) is not long current)
                        return Loc.F("No cloud save of '{0}' yet. It is sent when the game is closed.", game.Name);
                    savedAt = current;
                    zip = await RetroService.DownloadSavesAsync(saves.Set);
                }
                else
                {
                    zip = await RetroService.DownloadSavesVersionAsync(saves.Set, version.Name);
                    savedAt = 0;
                }
                string? backup = await Task.Run(() => saves.Unpack(new MemoryStream(zip)));
                if (version == null)
                    saves.Remember(savedAt);
                else
                    // An older version becomes the current one, so the other PCs take it too
                    await SendAsync(game, force: true);
                return backup != null
                    ? Loc.F("The saves of '{0}' are back; the ones of this PC are kept in {1}", game.Name, backup)
                    : Loc.F("The saves of '{0}' are back", game.Name);
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return WebExceptionHelper.TryGetServerMessage(ex);
            }
        }

        internal sealed record Version(RetroService.SavesVersion? Saves, long SavedAt, long Size, bool IsCurrent);

        /// <summary>The cloud's saves of the game, the current one first.</summary>
        /// <returns>Null when the service keeps none (not available).</returns>
        public static async Task<List<Version>?> HistoryAsync(RetroGame game)
        {
            string set = SetOf(game);
            long? current = await RetroService.SavesInfoAsync(set);
            var versions = new List<Version>();
            if (current is long savedAt)
                versions.Add(new Version(null, savedAt, 0, true));
            versions.AddRange((await RetroService.SavesHistoryAsync(set)).Select(v => new Version(v, v.SavedAt, v.Size, false)));
            return versions;
        }
    }
}
