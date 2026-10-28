using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Localization;
using gamevault.Models;
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
        /// <param name="stateSlot">A save state of the game to start from (RetroArch's games), or none.</param>
        public static async Task PlayAsync(RetroGame game, int? stateSlot = null)
        {
            if (game.Busy || game.RomFile is not string rom)
                return;
            if (game.Platform.System.IsEden && RetroSystems.IsSwitchAddOn(rom))
            {
                game.Status = Loc.T("An update or a DLC is not started: Eden takes it when its game is played.");
                return;
            }
            if (Platform.MobilePlatform.Current is Platform.IMobilePlatform mobile)
            {
                await PlayOnAndroidAsync(mobile, game, rom, stateSlot);
                return;
            }
            game.Busy = true;
            try
            {
                if (game.Platform.System.IsEden)
                    await StandaloneEmulator.Eden.OfferUpdateAsync();
                await RetroArchManager.EnsureReadyAsync(game.Platform, text => game.Status = text);
                if (await RetroCloudSaves.TakeAsync(game, text => game.Status = text) is string kept)
                    MainWindowViewModel.Instance.AppBarText = Loc.F("The saves of another PC were newer and were taken; the ones of this PC are kept in {0}", kept);
                var saves = RetroCloudSaves.Of(game);
                var before = saves.Snapshot();
                StandaloneEmulator? emulator = StandaloneEmulator.Of(game.Platform.System);
                Process process = emulator != null
                    ? emulator.Launch(RetroArchManager.Player, rom, RetroArchManager.LanguageOf(game))
                    : RetroArchManager.Launch(game.Platform.System, rom, RetroArchManager.LanguageOf(game), RetroArchManager.ControllerOf(game), stateSlot);
                game.Status = emulator == null ? Loc.T("Playing: Select+Start to quit")
                    : emulator.ControllerHint is string hint ? $"{Loc.T("Playing: close the game's window to quit")} · {hint}"
                    : Loc.T("Playing: close the game's window to quit");
                DiscordHelper.Instance.RetroStarted(game, RetroSystems.PublicCoverUrl(await RommAccount.UrlAsync(), game.Cover));
                _ = WhenClosedAsync(process.WaitForExitAsync(), game, before);
            }
            catch (RetroArchManager.MissingBiosException ex)
            {
                game.Status = WebExceptionHelper.TryGetServerMessage(ex);
                game.Busy = false;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
                game.Busy = false;
            }
        }

        /// <summary>
        /// The libretro core of Android, or null when the console has none there (PS3, Switch, Xbox: their emulators are
        /// other programs).
        /// </summary>
        internal static string? AndroidCore(RetroSystem system) => system.Core switch
        {
            "rpcs3" or "eden" or "xemu" => null,
            // Azahar is the PC's: Android has Citra's core, its origin
            "azahar" => "citra",
            string core => core,
        };

        /// <summary>
        /// Android: RetroArch for Android (installed by the player, with the core), until SanctuaryVault is back on the
        /// screen. The play time is counted; the saves stay in RetroArch's folders for now.
        /// </summary>
        private static async Task PlayOnAndroidAsync(Platform.IMobilePlatform mobile, RetroGame game, string rom, int? stateSlot)
        {
            if (AndroidCore(game.Platform.System) is not string core)
            {
                game.Status = Loc.F("{0} games are not playable on Android yet.", game.Platform.Name);
                return;
            }
            if (mobile.CannotPlay(core) is string why)
            {
                game.Status = why;
                return;
            }
            if (mobile.PlaysBuiltIn(core))
            {
                await PlayBuiltInAsync(mobile, game, core, rom);
                return;
            }
            game.Busy = true;
            try
            {
                // The first game installs RetroArch, as on a PC
                if (!mobile.HasRetroArch && !await mobile.InstallRetroArchAsync(text => game.Status = text))
                {
                    game.Status = Loc.T("RetroArch was not installed: Play installs it again.");
                    return;
                }
                game.Status = Loc.T("Playing in RetroArch");
                DateTime started = DateTime.UtcNow;
                await mobile.PlayAsync(core, rom);
                TimeSpan played = DateTime.UtcNow - started;
                await RetroLibrary.RecordPlayAsync(game, (int)Math.Round(played.TotalMinutes));
                if (RommAccount.IsLinked)
                {
                    try { await RommAccount.PlayedAsync(game.Id); }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
                // Back at once: most often RetroArch has not the console's core yet (it can not be added from here)
                game.Status = played < TimeSpan.FromSeconds(10)
                    ? Loc.F("RetroArch closed at once: if the game did not start, download the core '{0}' in RetroArch (Main Menu > Online Updater > Core Downloader), then press Play again.", core)
                    : "";
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
            }
            finally { game.Busy = false; }
        }

        /// <summary>
        /// Android, the consoles whose cores draw without the GPU: the app's own player, as RetroArch on a PC (its core and
        /// BIOS downloaded, the saves taken from the cloud before and sent after, the play time counted).
        /// </summary>
        private static async Task PlayBuiltInAsync(Platform.IMobilePlatform mobile, RetroGame game, string core, string rom)
        {
            game.Busy = true;
            try
            {
                await mobile.EnsureCoreAsync(core, text => game.Status = text);
                await RetroArchManager.EnsureSystemFilesAsync(game.Platform, text => game.Status = text);
                await OfferRommAsync(game);
                if (await RetroCloudSaves.TakeAsync(game, text => game.Status = text) is string kept)
                    MainWindowViewModel.Instance.AppBarText = Loc.F("The saves of another PC were newer and were taken; the ones of this PC are kept in {0}", kept);
                var saves = RetroCloudSaves.Of(game);
                var before = saves.Snapshot();
                // The cartridge's (or memory card's) save where the cloud put it, else in the core's folder, as RetroArch
                string saveFile = saves.MainSave() ?? System.IO.Path.Combine(RetroArchManager.SavesFolder,
                    RetroArchManager.Layout.SaveFolderName(game.Platform.System.Core), System.IO.Path.GetFileNameWithoutExtension(rom) + ".srm");
                game.Status = Loc.T("Playing: Select+Start to quit");
                _ = WhenClosedAsync(mobile.PlayBuiltInAsync(new Platform.BuiltInGame(game.Name, game.Platform.Slug, core, rom,
                    RetroArchManager.Layout.SystemDirectory, saveFile, RetroArchManager.LanguageOf(game))), game, before);
            }
            catch (RetroArchManager.MissingBiosException ex)
            {
                game.Status = WebExceptionHelper.TryGetServerMessage(ex);
                game.Busy = false;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                game.Status = Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
                game.Busy = false;
            }
        }

        /// <summary>
        /// Android, the first retro game: RomM keeps the saves of the other devices when they are linked to it, and this
        /// phone's profile is new. Linking is offered once (then in Settings > Retro); the page opens in the browser.
        /// </summary>
        private static async Task OfferRommAsync(RetroGame game)
        {
            try
            {
                string config = LoginManager.Instance.GetUserProfile().UserConfigFile;
                if (RommAccount.IsLinked || Preferences.Get(AppConfigKey.RommOffered, config) == "1" || await RommAccount.UrlAsync() == null)
                    return;
                Preferences.Set(AppConfigKey.RommOffered, "1", config);
                if (!await DialogService.ConfirmAsync(Loc.T("RomM keeps the saves of your retro games. Link your RomM account on this device to find the saves of your other devices here?"),
                        Loc.T("RomM saves"), Loc.T("Link RomM"), Loc.T("Not now")))
                    return;
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(10));
                string? failure = await RommAccount.LinkAsync((code, page) =>
                {
                    game.Status = Loc.F("Approve the code {0} on RomM's page that opened, then come back", code);
                    Platform.PlatformInfo.OpenUrl(page);
                }, timeout.Token);
                MainWindowViewModel.Instance.AppBarText = failure ?? Loc.F("Linked to RomM as {0}", RommAccount.Username);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>Busy while it runs (not started twice, not deleted under RetroArch), then its time counted and its saves sent.</summary>
        private static async Task WhenClosedAsync(Task closed, RetroGame game, System.Collections.Generic.Dictionary<string, long> before)
        {
            DateTime started = DateTime.UtcNow;
            await closed;
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
            // RomM's "last played", and the achievements RetroArch unlocked
            if (RommAccount.IsLinked)
            {
                try { await RommAccount.PlayedAsync(game.Id); }
                catch (Exception ex) { Log.Ignored(ex); }
                // RetroArch's achievements (not the Android app's own player)
                if (StandaloneEmulator.Of(game.Platform.System) == null && !Platform.PlatformInfo.IsAndroid)
                {
                    await RetroAchievementsAccount.SyncRommAsync();
                    await RetroAchievementsAccount.ShareAsync(game);
                }
            }
            game.Status = "";
            game.Busy = false;
        }
    }
}
