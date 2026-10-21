using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>Install and Play of a retro game: the same from the Retro page, a game's page or the Downloads page.</summary>
    internal static class RetroPlay
    {
        /// <summary>Into the Downloads page and its queue, as the library's games.</summary>
        /// <param name="playWhenDone">"Install &amp; Play": the game starts once it is ready.</param>
        public static async Task InstallAsync(RetroGame game, bool playWhenDone = false)
        {
            if (game.Busy)
                return;
            try
            {
                game.Status = Loc.T("Checking the game...");
                string? refused = await RetroDownloads.StartAsync(game, playWhenDone);
                if (refused != null)
                    game.Status = refused;
                else
                    MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is in the downloads", game.Name);
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
            }
        }

        /// <summary>RetroArch ready, the game's saves from the cloud when newer, then the game.</summary>
        public static async Task PlayAsync(RetroGame game)
        {
            if (game.Busy || game.RomFile is not string rom)
                return;
            game.Busy = true;
            try
            {
                await RetroArchManager.EnsureReadyAsync(game.Platform, text => game.Status = text);
                if (await RetroCloudSaves.TakeAsync(game, text => game.Status = text) is string kept)
                    MainWindowViewModel.Instance.AppBarText = Loc.F("The saves of another PC were newer and were taken; the ones of this PC are kept in {0}", kept);
                var saves = RetroCloudSaves.Of(game);
                var before = saves.Snapshot();
                Process process = RetroArchManager.Launch(game.Platform.System.Core, rom);
                game.Status = Loc.T("Playing: Select+Start to quit");
                DiscordHelper.Instance.RetroStarted(game, RetroSystems.PublicCoverUrl(await RommAccount.UrlAsync(), game.Cover));
                _ = WhenClosedAsync(process, game, before);
            }
            catch (RetroArchManager.MissingBiosException ex)
            {
                game.Status = ex.Message;
                game.Busy = false;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", ex.Message);
                game.Busy = false;
            }
        }

        /// <summary>Busy while it runs (not started twice, not deleted under RetroArch), then its time counted and its saves sent.</summary>
        private static async Task WhenClosedAsync(Process process, RetroGame game, System.Collections.Generic.Dictionary<string, long> before)
        {
            DateTime started = DateTime.UtcNow;
            await process.WaitForExitAsync();
            DiscordHelper.Instance.RetroStopped();
            try
            {
                // What the game wrote outside the files named after it (a memory card) is its saves from now on
                RetroCloudSaves.Of(game).Track(before);
            }
            catch (Exception ex) { Log.Ignored(ex); }
            // Counted as the library's games: the minutes of the session, on the profile and in the activity
            await RetroLibrary.RecordPlayAsync(game, (int)Math.Round((DateTime.UtcNow - started).TotalMinutes));
            game.Status = Loc.T("Sending the saves...");
            await RetroCloudSaves.SendAsync(game);
            game.Status = "";
            game.Busy = false;
        }
    }
}
