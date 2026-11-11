using Avalonia.Media.Imaging;
using GameVault.Core;
using gamevault.Helper.Platform;
using gamevault.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The games' logos (their title as drawn on their box), from the SanctuaryVault service: it has RomM's for the
    /// retro games and SteamGridDB's for the others. Kept on this PC once read; a game without one is not asked again
    /// before the client starts again.
    /// </summary>
    internal static class GameLogoService
    {
        private static readonly ConcurrentDictionary<string, Task<Bitmap?>> asked = new();

        private static string Folder => Path.Combine(PlatformInfo.LocalDataDirectory, "cache", "logos");

        /// <summary>The tests start from nothing.</summary>
        internal static void Forget() => asked.Clear();

        public static Task<Bitmap?> OfAsync(Game game) => asked.GetOrAdd($"pc-{game.ID}", key => ReadAsync(key, $"/logos/pc/{game.ID}"));

        public static Task<Bitmap?> OfAsync(RetroGame game) => asked.GetOrAdd($"retro-{game.Id}", key => ReadAsync(key, $"/retro/games/{game.Id}/logo"));

        private static async Task<Bitmap?> ReadAsync(string key, string path)
        {
            try
            {
                // One folder by service: another server's game of the same number is another game
                string file = Path.Combine(Folder, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SanctuaryService.Current)))[..10], key + ".img");
                if (!File.Exists(file))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, SanctuaryService.Current.TrimEnd('/') + path);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                    using HttpResponseMessage response = await WebHelper.SendLongAsync(request, timeout.Token);
                    if (!response.IsSuccessStatusCode)
                        return null;
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllBytesAsync(file + ".part", bytes);
                    File.Move(file + ".part", file, true);
                }
                return await Task.Run(() =>
                {
                    using FileStream stream = File.OpenRead(file);
                    // Shown about 200 high: twice that is enough on a large screen
                    return Bitmap.DecodeToHeight(stream, 400);
                });
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }
    }
}
