using GameVault.Core;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The library's cloud saves in the player's RomM too, when their account is linked: the same archive as the one
    /// sent to the GameVault server, on RomM's copy of the game. The server stays where the saves are taken from (its
    /// conflicts and history); RomM's are taken when the server has none of the game and this PC neither.
    /// </summary>
    internal static class RommPcSaves
    {
        /// <summary>A stable slot: RomM keeps its last 10 versions.</summary>
        internal const string Slot = "sanctuaryvault";
        /// <summary>The library's saves are Ludusavi's archives.</summary>
        internal const string Emulator = "ludusavi";

        private static Game? GameOf(int gameId) => InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key?.ID == gameId).Key;

        private static async Task<int?> RomOfAsync(Game? game) =>
            RommAccount.IsLinked && !string.IsNullOrEmpty(game?.Path) ? await RommAccount.PcGameIdAsync(Path.GetFileName(game.Path)) : null;

        /// <summary>After the upload to the server: the same archive to RomM. A failure only goes to the log (the server has it).</summary>
        public static async Task SendAsync(int gameId, string archive)
        {
            try
            {
                Game? game = GameOf(gameId);
                if (await RomOfAsync(game) is int rom)
                    await RommAccount.UploadSaveAsync(rom, Emulator, Slot, $"{Path.GetFileNameWithoutExtension(game!.Path)}.{Slot}.zip", await File.ReadAllBytesAsync(archive));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>RomM's newest archive of the game; null without a linked account, the game in RomM or a save there.</summary>
        public static async Task<byte[]?> LatestAsync(int gameId)
        {
            try
            {
                if (await RomOfAsync(GameOf(gameId)) is int rom && (await RommAccount.SavesAsync(rom)).FirstOrDefault(s => s.Slot == Slot) is RommSave save)
                    return await RommAccount.SaveContentAsync(save);
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return null;
        }
    }
}
