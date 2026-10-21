using GameVault.Core.CloudSaves;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The library's cloud saves in the player's RomM, when their account is linked and RomM has the game (RomM and
    /// GameVault read the same folder): sent there only, taken from there, with RomM's history. Otherwise they stay
    /// with the GameVault server. The archives are Ludusavi's, as the server's.
    /// </summary>
    internal static class RommPcSaves
    {
        /// <summary>A stable slot: RomM keeps its last 10 versions.</summary>
        internal const string Slot = "sanctuaryvault";
        internal const string Emulator = "ludusavi";

        private static Game? GameOf(int gameId) => InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key?.ID == gameId).Key;

        /// <summary>RomM's copy of the game, where its saves go; null: they go to the GameVault server (no account linked, or RomM has not the game).</summary>
        public static async Task<int?> RomAsync(int gameId) =>
            RommAccount.IsLinked && GameOf(gameId)?.Path is { Length: > 0 } path ? await RommAccount.PcGameIdAsync(Path.GetFileName(path)) : null;

        /// <summary>The game's newest save in RomM; null when there is none yet.</summary>
        public static async Task<RommSave?> LatestAsync(int rom) => (await RommAccount.SavesAsync(rom)).FirstOrDefault(s => s.Slot == Slot);

        /// <summary>As the server's newest save, for the conflict rules: this installation's when this PC sent it or took it.</summary>
        public static ServerSave AsServerSave(RommSave save, string installationId) =>
            new(save.UpdatedAt, save.IsCurrentHere ? installationId : save.OriginDevice ?? "romm");

        public static async Task UploadAsync(int gameId, int rom, string archive) =>
            await RommAccount.UploadSaveAsync(rom, Emulator, Slot, $"{Path.GetFileNameWithoutExtension(GameOf(gameId)?.Path ?? gameId.ToString())}.zip", await File.ReadAllBytesAsync(archive));

        /// <param name="saveId">A save of the history (its RomM id) or the newest.</param>
        public static async Task DownloadAsync(int saveId, string target) =>
            await File.WriteAllBytesAsync(target, await RommAccount.SaveContentAsync(new RommSave { Id = saveId }));

        /// <summary>RomM's versions of the game, as the server's history (the newest first).</summary>
        public static async Task<List<SaveVersion>> HistoryAsync(int rom, string thisInstallation) =>
            (await RommAccount.SavesAsync(rom)).Where(s => s.Slot == Slot).Select(s => new SaveVersion
            {
                Name = s.Id.ToString(),
                SavedAt = new DateTimeOffset(s.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                Size = s.Size,
                InstallationId = s.OriginDevice == RommAccount.DeviceId ? thisInstallation : s.OriginDevice ?? "",
            }).ToList();
    }
}
