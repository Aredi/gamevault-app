using GameVault.Core;
using GameVault.Core.Diagnostics;
using GameVault.Core.Retro;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>Install and Play of a retro game: the same from the Retro page, a game's page or the Downloads page.</summary>
    internal static class RetroPlay
    {
        /// <summary>A game's emulator (or recomp) was started, then closed: on the UI thread.</summary>
        internal static event Action<RetroGame>? Started, Closed;
        /// <summary>A game's achievements came back from RetroAchievements after it was closed (RomM linked).</summary>
        internal static event Action<RetroGame>? Synced;

        /// <summary>Told to whoever listens (the living-room mode); the tests tell it as a real game would.</summary>
        internal static void Raise(bool started, RetroGame game) => Raise(started ? Started : Closed, game);

        internal static void RaiseSynced(RetroGame game) => Raise(Synced, game);

        private static void Raise(Action<RetroGame>? handler, RetroGame game)
        {
            if (handler == null)
                return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { handler(game); }
                catch (Exception ex) { Log.Ignored(ex); }
            });
        }

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
            if (game.Platform.System.IsSwitch && RetroSystems.IsSwitchAddOn(rom))
            {
                game.Status = Loc.F("An update or a DLC is not started: {0} takes it when its game is played.", StandaloneEmulator.Of(game)!.Name);
                return;
            }
            if (Platform.MobilePlatform.Current is Platform.IMobilePlatform mobile)
            {
                await PlayOnAndroidAsync(mobile, game, rom, stateSlot);
                return;
            }
            game.Busy = true;
            game.Failure = null;
            try
            {
                // The emulator an administrator gave it, else its console's (offered its newer release)
                if (game.Platform.System.IsSwitch || GameEmulators.OfConsole(game.Platform.Slug).Any())
                    await GameEmulators.LoadAsync();
                if (GameEmulators.RecompOf(game) is RecompProgram recomp
                    && await ReadyOrSetAsideAsync(game, recomp.Name, () => recomp.Layout.IsInstalled, () => recomp.EnsureReadyAsync(text => game.Status = text)))
                {
                    await PlayRecompAsync(game, recomp, rom);
                    return;
                }
                if (StandaloneEmulator.Of(game) == StandaloneEmulator.Eden)
                    await StandaloneEmulator.Eden.OfferUpdateAsync();
                else if (StandaloneEmulator.Of(game) == StandaloneEmulator.Eglantine)
                    await StandaloneEmulator.Eglantine.OfferUpdateAsync();
                if (StandaloneEmulator.Of(game) is not CustomSwitchEmulator own
                    || !await ReadyOrSetAsideAsync(game, own.Name, () => own.Layout.IsInstalled, () => own.EnsureReadyAsync(game.Platform, text => game.Status = text)))
                    await RetroArchManager.EnsureReadyAsync(game.Platform, text => game.Status = text);
                if (await RetroCloudSaves.TakeAsync(game, text => game.Status = text) is string kept)
                    MainWindowViewModel.Instance.AppBarText = Loc.F("The saves of another PC were newer and were taken; the ones of this PC are kept in {0}", kept);
                var saves = RetroCloudSaves.Of(game);
                var before = saves.Snapshot();
                StandaloneEmulator? emulator = StandaloneEmulator.Of(game);
                Process process = emulator != null
                    ? emulator.Launch(RetroArchManager.Player, rom, RetroArchManager.LanguageOf(game))
                    : RetroArchManager.Launch(game.Platform.System, rom, RetroArchManager.LanguageOf(game), RetroArchManager.ControllerOf(game), stateSlot);
                game.Status = emulator == null ? Loc.T("Playing: Select+Start to quit")
                    : emulator.ControllerHint is string hint ? $"{Loc.T("Playing: close the game's window to quit")} · {hint}"
                    : Loc.T("Playing: close the game's window to quit");
                DiscordHelper.Instance.RetroStarted(game, RetroSystems.PublicCoverUrl(await RommAccount.UrlAsync(), game.Cover));
                // The friends see it in their chat and the community page, as a library game
                ActivityService.Instance.SetRetroPlaying(game);
                Raise(Started, game);
                _ = WhenClosedAsync(process, game, before);
            }
            catch (RetroArchManager.MissingBiosException ex)
            {
                game.Status = WebExceptionHelper.TryGetServerMessage(ex);
                game.Busy = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                game.Failure = new GameFailure(Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex)), null, 0, DateTime.UtcNow);
                game.Status = "";
                game.Busy = false;
            }
        }

        /// <summary>
        /// The libretro core of Android, or null when the console has none there (PS3, Switch, Xbox: their emulators are
        /// other programs).
        /// </summary>
        internal static string? AndroidCore(RetroSystem system) => system.IsStandalone ? null : system.Core switch
        {
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
            if ((mobile.CannotPlay(core) ?? await mobile.UnsupportedAsync(core)) is string why)
            {
                game.Status = why;
                return;
            }
            if (mobile.PlaysBuiltIn(core))
            {
                await PlayBuiltInAsync(mobile, game, core, rom, stateSlot);
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
        private static async Task PlayBuiltInAsync(Platform.IMobilePlatform mobile, RetroGame game, string core, string rom, int? stateSlot)
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
                // The friends see it in their chat and the community page, as on a PC
                ActivityService.Instance.SetRetroPlaying(game);
                Raise(Started, game);
                // Its state where RetroArch keeps its first slot: the game's page offers to resume from it, as on a PC
                string states = System.IO.Path.Combine(RetroArchManager.Layout.StatesDirectory(RetroArchManager.Player),
                    RetroArchManager.Layout.SaveFolderName(game.Platform.System.Core));
                string stem = System.IO.Path.Combine(states, System.IO.Path.GetFileNameWithoutExtension(rom) + ".state");
                string? startState = stateSlot is int slot ? (slot == 0 ? stem : stem + slot) : null;
                _ = WhenClosedAsync(mobile.PlayBuiltInAsync(new Platform.BuiltInGame(game.Name, game.Platform.Slug, core, rom,
                    RetroArchManager.Layout.SystemDirectory, saveFile, RetroArchManager.LanguageOf(game), stem, startState)), game, before);
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

        /// <summary>Asked whether to play with the console's emulator (its name given) when the game's own can no longer be downloaded; the tests answer.</summary>
        internal static Func<string, string, Task<bool>> AskConsoleInstead = async (own, console) =>
            await App.Instance.MainWindow.ShowMessageAsync(Loc.F("The link of {0} no longer works", own),
                Loc.F("{0}, the emulator the administrator chose for this game, can no longer be downloaded: its link no longer works. Tell the administrator. Play with {1} instead? The game may not work with it.", own, console),
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = Loc.F("Play with {0}", console), NegativeButtonText = Loc.T("Cancel") }) == MessageDialogResult.Affirmative;

        /// <summary>
        /// The game's own emulator or recomp installed (then the console's files); when its link is gone (the project removed), the player is told and
        /// offered the console's emulator.
        /// </summary>
        /// <returns>False when it was set aside: the console's emulator plays the game.</returns>
        internal static async Task<bool> ReadyOrSetAsideAsync(RetroGame game, string own, Func<bool> installed, Func<Task> ensureReady)
        {
            try
            {
                await ensureReady();
                return true;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is (HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) && !installed())
            {
                // Not the console's keys or firmware, installed after it: its own download
                Log.Info($"{game.Name}: {own}'s link answered {(int)ex.StatusCode!}");
                string console = StandaloneEmulator.Of(game.Platform.System)?.Name ?? "RetroArch";
                if (!await AskConsoleInstead(own, console))
                    throw new System.IO.IOException(Loc.F("The link of {0} no longer works: tell the administrator.", own));
                GameEmulators.SetAside(game);
                return false;
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

        /// <summary>
        /// The game's recomp instead of an emulator: installed, then started with the game's files, or with their
        /// path copied when it asks for them in its window.
        /// </summary>
        internal static async Task PlayRecompAsync(RetroGame game, RecompProgram recomp, string rom)
        {
            await recomp.EnsureReadyAsync(text => game.Status = text);
            string folder = RetroService.InstalledFolder(game) ?? System.IO.Path.GetDirectoryName(rom)!;
            Process process = recomp.Start(rom, folder);
            if (recomp.Layout.AsksForTheGame)
            {
                string path = GameFilesOf(rom, folder);
                ClipboardHelper.SetText(path);
                game.Status = Loc.F("Playing with {0}. The game's files are in {1}: this path is copied, paste it when {0} asks for the game.", recomp.Name, path);
            }
            else
                game.Status = Loc.F("Playing with {0}: close its windows to quit", recomp.Name);
            DiscordHelper.Instance.RetroStarted(game, RetroSystems.PublicCoverUrl(await RommAccount.UrlAsync(), game.Cover));
            ActivityService.Instance.SetRetroPlaying(game);
            Raise(Started, game);
            _ = WhenRecompClosedAsync(process, recomp, game);
        }

        /// <summary>What a recomp is shown: the game's one file (a cartridge), else the folder of its files (a dump).</summary>
        internal static string GameFilesOf(string rom, string folder) =>
            System.IO.Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.AllDirectories).Take(2).Count() == 1 ? rom : folder;

        /// <summary>Its time counted as any game's; its saves are its own, in its folder.</summary>
        private static async Task WhenRecompClosedAsync(Process process, RecompProgram recomp, RetroGame game)
        {
            DateTime started = DateTime.UtcNow;
            try { await recomp.WhenClosedAsync(process); }
            catch (Exception ex) { Log.Ignored(ex); }
            Raise(Closed, game);
            DiscordHelper.Instance.RetroStopped();
            ActivityService.Instance.SetRetroPlaying(null);
            await RetroLibrary.RecordPlayAsync(game, (int)Math.Round((DateTime.UtcNow - started).TotalMinutes));
            if (RommAccount.IsLinked)
            {
                try { await RommAccount.PlayedAsync(game.Id); }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            game.Status = "";
            game.Busy = false;
        }

        /// <summary>Busy while it runs (not started twice, not deleted under RetroArch), then its time counted and its saves sent; told when it died at its start.</summary>
        internal static Task WhenClosedAsync(Process process, RetroGame game, System.Collections.Generic.Dictionary<string, long> before) =>
            WhenClosedAsync(process.WaitForExitAsync(), game, before, process);

        /// <summary>The same for Android's own player, without a process: nothing to tell about its exit.</summary>
        private static async Task WhenClosedAsync(Task closed, RetroGame game, System.Collections.Generic.Dictionary<string, long> before, Process? process = null)
        {
            DateTime started = DateTime.UtcNow;
            await closed;
            Raise(Closed, game);
            TimeSpan ran = DateTime.UtcNow - started;
            string? failure = process == null ? null : FailureOf(process.ExitCode, ran, StandaloneEmulator.Of(game)?.Name ?? "RetroArch");
            if (failure != null)
            {
                Log.Info($"{game.Name}: {failure}");
                MainWindowViewModel.Instance.AppBarText = failure;
                game.Failure = new GameFailure(failure, process!.ExitCode, (int)ran.TotalSeconds, started);
            }
            DiscordHelper.Instance.RetroStopped();
            ActivityService.Instance.SetRetroPlaying(null);
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
                if (StandaloneEmulator.Of(game) == null && !Platform.PlatformInfo.IsAndroid)
                {
                    await RetroAchievementsAccount.SyncRommAsync();
                    await RetroAchievementsAccount.ShareAsync(game);
                    Raise(Synced, game);
                }
            }
            game.Status = "";
            game.Busy = false;
        }

        private static string? FailureOf(int exitCode, TimeSpan ran, string emulator) => EmulatorExit.Of(exitCode, ran) switch
        {
            EmulatorFailure.None => null,
            EmulatorFailure.OlderProcessor => Loc.F("{0} stopped as it started: this PC's processor can not run it.", emulator),
            EmulatorFailure.MissingRuntime => Loc.F("{0} could not start: install the Microsoft Visual C++ Redistributable (x64) on this PC.", emulator),
            _ => Loc.F("{0} stopped as it started (code {1}).", emulator, EmulatorExit.CodeText(exitCode)),
        };
    }
}
