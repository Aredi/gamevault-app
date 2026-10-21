using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The cloud saves of the retro games, per game as the library's: taken before a game when another PC (or RomM's
    /// web page) has newer ones, sent after it, and from its page sent, taken back or restored from an older version.
    /// They go to the player's RomM when their account is linked (the main save on its own, so RomM's emulator uses
    /// it, the rest in a zip), else to the SanctuaryVault service (one zip).
    /// </summary>
    internal static class RetroCloudSaves
    {
        /// <summary>RomM's slots of SanctuaryVault: stable names, so RomM pairs each new save with the previous one.</summary>
        internal const string MainSlot = "sanctuaryvault", RestSlot = "sanctuaryvault-data";

        /// <summary>The game's name on the SanctuaryVault service: "nes-40".</summary>
        public static string SetOf(RetroGame game) => $"{game.Platform.Slug}-{game.Id}";

        public static bool ToRomm => RommAccount.IsLinked;

        public static RetroGameSaves Of(RetroGame game)
        {
            var layout = RetroArchManager.Layout;
            string player = RetroArchManager.Player;
            string stem = Path.GetFileNameWithoutExtension(game.RomFile ?? game.FileName), destination = ToRomm ? "romm" : "";
            // A console's storage keeps its saves by game (serial, title id): what the game writes while it runs is its saves
            if (StandaloneEmulator.Of(game.Platform.System) is StandaloneEmulator emulator)
                return new RetroGameSaves(emulator.Layout.SavesDirectory(player), emulator.Layout.StatesDirectory, layout.SaveBackupsDirectory(player), SetOf(game), stem, destination);
            return new RetroGameSaves(layout.SavesDirectory(player), layout.StatesDirectory(player), layout.SaveBackupsDirectory(player), SetOf(game), stem, destination);
        }

        #region RomM
        private static long Millis(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

        /// <summary>The player's RomM saves of the game, the newest first.</summary>
        internal static Task<List<RommSave>> RommSavesAsync(RetroGame game) => RommAccount.SavesAsync(game.Id);

        /// <summary>The newest save of each of SanctuaryVault's slots (the list is newest first).</summary>
        private static List<RommSave> Ours(List<RommSave> all) =>
            new[] { MainSlot, RestSlot }.Select(slot => all.FirstOrDefault(s => s.Slot == slot)).OfType<RommSave>().ToList();

        private static Task<RommSave> RommUploadAsync(RetroGame game, string slot, string fileName, byte[] content) =>
            RommAccount.UploadSaveAsync(game.Id, game.Platform.System.Core, slot, fileName, content);

        /// <summary>A save of RomM in place of this PC's: the zip of the rest, or a main save where the emulator reads it.</summary>
        private static async Task<string?> PutBackAsync(RetroGame game, RetroGameSaves saves, IReadOnlyList<RommSave> taken)
        {
            byte[]? rest = null;
            (string, string, byte[])? main = null;
            foreach (RommSave save in taken)
            {
                byte[] content = await RommAccount.SaveContentAsync(save);
                if (save.Slot == RestSlot)
                    rest = content;
                else
                {
                    // Where this PC keeps it, else the core's folder of RetroArch
                    string folder = saves.MainSave() is string current
                        ? Path.GetRelativePath(RetroArchManager.Layout.SavesDirectory(RetroArchManager.Player), Path.GetDirectoryName(current)!)
                        : RetroArchManager.Layout.SaveFolderName(game.Platform.System.Core);
                    main = (folder, Path.GetExtension(save.FileName), content);
                }
            }
            return await Task.Run(() => saves.Replace(rest != null ? new MemoryStream(rest) : null, main));
        }
        #endregion

        /// <summary>Before a game: its saves from another PC (or RomM's web page), when they are newer. Without network or with the cloud saves off, the ones here.</summary>
        /// <returns>Where this PC's own saves were kept when they had changed too; null otherwise.</returns>
        public static async Task<string?> TakeAsync(RetroGame game, Action<string> status)
        {
            if (!SettingsViewModel.Instance.CloudSaves)
                return null;
            RetroGameSaves saves = Of(game);
            try
            {
                if (ToRomm)
                {
                    var all = await RommSavesAsync(game);
                    var ours = Ours(all);
                    // Saves of RomM's web page or another device: taken when this PC has none of the game
                    if (ours.Count == 0 && !saves.HasFiles && all.FirstOrDefault(s => !s.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) is RommSave other)
                        ours.Add(other);
                    if (ours.Count == 0 || !saves.IsOlderThan(Millis(ours.Max(s => s.UpdatedAt))))
                        return null;
                    status(Loc.T("Getting the saves from the other PCs..."));
                    bool changedHere = saves.ChangedSinceSync;
                    string? backup = await PutBackAsync(game, saves, ours);
                    saves.Remember(Millis(ours.Max(s => s.UpdatedAt)));
                    return changedHere ? backup : null;
                }
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

        /// <summary>After a game (or from its page): its saves to the cloud when they changed, or always when forced.</summary>
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
                if (ToRomm)
                {
                    var sent = new List<RommSave>();
                    if (saves.MainSave() is string main)
                        sent.Add(await RommUploadAsync(game, MainSlot, Path.GetFileName(main), await File.ReadAllBytesAsync(main)));
                    var rest = new MemoryStream();
                    if (await Task.Run(() => saves.PackRest(rest)))
                        sent.Add(await RommUploadAsync(game, RestSlot, $"{Path.GetFileNameWithoutExtension(game.RomFile ?? game.FileName)}.sanctuaryvault.zip", rest.ToArray()));
                    saves.Remember(Millis(sent.Max(s => s.UpdatedAt)));
                    return Loc.F("The saves of '{0}' are in RomM", game.Name);
                }
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

        /// <summary>A version of the history (the current one first), from RomM or the SanctuaryVault service.</summary>
        internal sealed record Version(RetroService.SavesVersion? Saves, long SavedAt, long Size, bool IsCurrent, RommSave? Romm = null)
        {
            /// <summary>What RomM knows of it: its file, where it comes from.</summary>
            public string? Label => Romm == null ? null
                : string.Join(" · ", new[] { Romm.FileName, Romm.IsOurs ? "SanctuaryVault" : Romm.Emulator ?? Loc.T("RomM"), Romm.Device }.Where(p => !string.IsNullOrEmpty(p)));
        }

        /// <summary>From the game's page: the cloud's current saves, or an older version, in place of this PC's (kept in a backup).</summary>
        /// <param name="version">Null: the current one.</param>
        public static async Task<string> RestoreAsync(RetroGame game, Version? version = null)
        {
            RetroGameSaves saves = Of(game);
            try
            {
                string? backup;
                if (ToRomm)
                {
                    var all = await RommSavesAsync(game);
                    var taken = version?.Romm != null ? new List<RommSave> { version.Romm }
                        : Ours(all);
                    if (taken.Count == 0)
                        taken = all.Take(1).ToList();
                    if (taken.Count == 0)
                        return Loc.F("No cloud save of '{0}' yet. It is sent when the game is closed.", game.Name);
                    backup = await PutBackAsync(game, saves, taken);
                    if (version?.Romm != null)
                        // An older version (or another device's) becomes the current one, so the other PCs take it too
                        await SendAsync(game, force: true);
                    else
                        saves.Remember(Millis(taken.Max(s => s.UpdatedAt)));
                }
                else if (version?.Saves == null)
                {
                    if (await RetroService.SavesInfoAsync(saves.Set) is not long current)
                        return Loc.F("No cloud save of '{0}' yet. It is sent when the game is closed.", game.Name);
                    byte[] zip = await RetroService.DownloadSavesAsync(saves.Set);
                    backup = await Task.Run(() => saves.Unpack(new MemoryStream(zip)));
                    saves.Remember(current);
                }
                else
                {
                    byte[] zip = await RetroService.DownloadSavesVersionAsync(saves.Set, version.Saves.Name);
                    backup = await Task.Run(() => saves.Unpack(new MemoryStream(zip)));
                    await SendAsync(game, force: true);
                }
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

        /// <summary>The cloud's saves of the game, the current one first: in RomM, every save of the game (any device, its web page).</summary>
        public static async Task<List<Version>?> HistoryAsync(RetroGame game)
        {
            if (ToRomm)
            {
                var all = await RommSavesAsync(game);
                RommSave? current = all.FirstOrDefault(s => s.Slot == MainSlot) ?? all.FirstOrDefault(s => s.Slot == RestSlot);
                return all.Select(s => new Version(null, Millis(s.UpdatedAt), s.Size, s == current, s)).ToList();
            }
            string set = SetOf(game);
            long? latest = await RetroService.SavesInfoAsync(set);
            var versions = new List<Version>();
            if (latest is long savedAt)
                versions.Add(new Version(null, savedAt, 0, true));
            versions.AddRange((await RetroService.SavesHistoryAsync(set)).Select(v => new Version(v, v.SavedAt, v.Size, false)));
            return versions;
        }

        /// <summary>Where the game's saves go, for its page.</summary>
        public static string Destination => ToRomm ? Loc.F("RomM ({0})", RommAccount.Username) : "SanctuaryVault";
    }
}
