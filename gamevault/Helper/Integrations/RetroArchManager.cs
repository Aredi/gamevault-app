using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Installs RetroArch, the cores, their data files and the platforms' BIOS (from RomM) the first time they are needed
    /// (in the client's data folder), and starts the games with them. The saves stay in that folder, one per player, so
    /// deleting a game keeps them; they also go to the SanctuaryVault service after each game.
    /// </summary>
    internal static class RetroArchManager
    {
        public static RetroArchLayout Layout { get; } = new(Path.Combine(PlatformInfo.LocalDataDirectory, "retroarch"), PlatformInfo.IsWindows);
        private static readonly SemaphoreSlim preparing = new(1, 1);
        internal static string Player => LoginManager.Instance.GetCurrentUser()?.ID.ToString() ?? "0";

        public sealed class MissingBiosException(string message) : Exception(message);

        /// <summary>RetroArch, then what the platform needs: its core, the core's data files and the BIOS.</summary>
        public static async Task EnsureReadyAsync(RetroPlatform platform, Action<string> status)
        {
            // The PS3 is RPCS3's, the Switch Eden's
            if (StandaloneEmulator.Of(platform.System) is StandaloneEmulator emulator)
            {
                await emulator.EnsureReadyAsync(platform, status);
                return;
            }
            await preparing.WaitAsync();
            try
            {
                await InstallRetroArchAsync(status);
                RetroSystem system = platform.System;
                if (!File.Exists(Layout.CoreFile(system.Core)))
                {
                    status(Loc.T("Downloading the emulator..."));
                    using var zip = new ZipArchive(new MemoryStream(await HttpClients.Shared.GetByteArrayAsync(Layout.CoreUrl(system.Core))));
                    zip.ExtractToDirectory(Layout.CoresDirectory, overwriteFiles: true);
                }
                if (system.SystemFiles is string name && !File.Exists(Layout.SystemFilesMarker(name)))
                {
                    status(Loc.T("Downloading the emulator's files..."));
                    using var zip = new ZipArchive(new MemoryStream(await HttpClients.Shared.GetByteArrayAsync(RetroArchLayout.SystemFilesUrl(name))));
                    zip.ExtractToDirectory(Layout.SystemDirectory, overwriteFiles: true);
                    File.WriteAllText(Layout.SystemFilesMarker(name), "");
                }
                if (system.FirmwareFolder is string folder)
                    await EnsureBiosAsync(platform, Path.Combine(Layout.SystemDirectory, folder), status);
                if (CrtSystems.Contains(system.Slug))
                    foreach (var (extension, url) in RetroArchLayout.CrtShaderUrls.Where(s => !File.Exists(Layout.CrtShaderFile(s.Extension))))
                    {
                        status(Loc.T("Downloading the CRT look..."));
                        Directory.CreateDirectory(Path.GetDirectoryName(Layout.CrtShaderFile(extension))!);
                        await File.WriteAllBytesAsync(Layout.CrtShaderFile(extension), await HttpClients.Shared.GetByteArrayAsync(url));
                    }
            }
            finally { preparing.Release(); }
        }

        private static string ProfileConfig => LoginManager.Instance.GetUserProfile().UserConfigFile;

        /// <summary>The consoles the player sees with the CRT look (Settings > Retro), by their slug.</summary>
        public static HashSet<string> CrtSystems
        {
            get => Preferences.Get(Models.AppConfigKey.RetroCrtSystems, ProfileConfig).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            set => Preferences.Set(Models.AppConfigKey.RetroCrtSystems, string.Join(",", value.Order()), ProfileConfig);
        }

        /// <summary>The definition of the 3D consoles, by their core (Settings > Retro).</summary>
        public static Dictionary<string, int> Resolutions
        {
            get => RetroResolution.Parse(Preferences.Get(Models.AppConfigKey.RetroResolution, ProfileConfig));
            set => Preferences.Set(Models.AppConfigKey.RetroResolution, RetroResolution.Format(value), ProfileConfig);
        }

        /// <summary>The cores whose consoles are drawn in 16:9 (Settings > Retro).</summary>
        public static HashSet<string> WidescreenCores
        {
            get => Preferences.Get(Models.AppConfigKey.RetroWidescreen, ProfileConfig).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            set => Preferences.Set(Models.AppConfigKey.RetroWidescreen, string.Join(",", value.Order()), ProfileConfig);
        }

        /// <summary>The language of the consoles chosen in Settings > Retro (RomM's name, "French"), or "" for the client's.</summary>
        public static string LanguageSetting
        {
            get => Preferences.Get(Models.AppConfigKey.RetroLanguage, ProfileConfig);
            set => Preferences.Set(Models.AppConfigKey.RetroLanguage, value, ProfileConfig);
        }
        public static string ClientLanguage => Loc.Language == "fr" ? "French" : "English";
        public static string Language => RetroLanguage.All.Contains(LanguageSetting) ? LanguageSetting : ClientLanguage;

        /// <summary>The language chosen on games' pages, by their RomM id.</summary>
        public static Dictionary<int, string> GameLanguages
        {
            get => RetroLanguage.Parse(Preferences.Get(Models.AppConfigKey.RetroGameLanguages, ProfileConfig));
            set => Preferences.Set(Models.AppConfigKey.RetroGameLanguages, RetroLanguage.Format(value), ProfileConfig);
        }

        /// <summary>The language the game starts in.</summary>
        public static string LanguageOf(RetroGame game) => RetroLanguage.Pick(GameLanguages.GetValueOrDefault(game.Id), Language, game.Languages);

        /// <summary>The controller of the consoles that have several (Settings > Retro), by their slug.</summary>
        public static Dictionary<string, int> Controllers
        {
            get => RetroControllers.Parse(Preferences.Get(Models.AppConfigKey.RetroControllers, ProfileConfig));
            set => Preferences.Set(Models.AppConfigKey.RetroControllers, RetroControllers.Format(value), ProfileConfig);
        }

        /// <summary>The controller chosen on games' pages, by their RomM id.</summary>
        public static Dictionary<string, int> GameControllers
        {
            get => RetroControllers.Parse(Preferences.Get(Models.AppConfigKey.RetroGameControllers, ProfileConfig));
            set => Preferences.Set(Models.AppConfigKey.RetroGameControllers, RetroControllers.Format(value), ProfileConfig);
        }

        /// <summary>The controller the game starts with; null when its console has one only.</summary>
        /// <summary>The keys chosen by console in Settings > Retro: see <see cref="RetroKeyboard"/>.</summary>
        public static Dictionary<string, Dictionary<string, RetroKeyboard.Key>> Keyboard
        {
            get => RetroKeyboard.Parse(Preferences.Get(Models.AppConfigKey.RetroKeyboard, ProfileConfig));
            set => Preferences.Set(Models.AppConfigKey.RetroKeyboard, RetroKeyboard.Format(value), ProfileConfig);
        }

        /// <summary>The gamepad's buttons chosen by console in Régler mes touches: see <see cref="RetroPad"/>.</summary>
        public static Dictionary<string, Dictionary<string, RetroPad.Input>> Pad
        {
            get => RetroPad.Parse(Preferences.Get(Models.AppConfigKey.RetroPad, ProfileConfig));
            set => Preferences.Set(Models.AppConfigKey.RetroPad, RetroPad.Format(value), ProfileConfig);
        }

        /// <summary>The console's keys and buttons in the client's config, written again before each game (<see cref="EnsureReadyAsync"/>): the previous console's are gone.</summary>
        private static void SetKeyboard(RetroSystem system, int? controller)
        {
            var pad = RetroPad.Config(system.Slug, controller, Pad.GetValueOrDefault(system.Slug)).ToList();
            // The gamepad set, not a LED controller or a KVM SDL lists before it
            if (pad.Count > 0 && Sdl2Library is string library && RetroGamepad.FirstControllerIndex(library) is int index)
                pad.Add(("input_player1_joypad_index", index.ToString()));
            var lines = RetroKeyboard.Config(system.Slug, controller, Keyboard.GetValueOrDefault(system.Slug)).Concat(pad).ToList();
            if (lines.Count == 0)
                return;
            string config = File.ReadAllText(Layout.ConfigFile);
            foreach (var (key, value) in lines)
                config = RetroResolution.MergeOptions(config, key, value);
            File.WriteAllText(Layout.ConfigFile, config);
        }

        public static int? ControllerOf(RetroGame game) => RetroControllers.Pick(game.Platform.Slug,
            GameControllers.TryGetValue(game.Id.ToString(), out int chosen) ? chosen : null,
            Controllers.TryGetValue(game.Platform.Slug, out int console) ? console : null);

        /// <summary>A value of the core's options (the others, chosen in RetroArch's menu, kept).</summary>
        private static void SetOption(string core, (string Key, string Value)? option)
        {
            if (option is not var (key, value))
                return;
            string file = Layout.CoreOptionsFile(core);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, RetroResolution.MergeOptions(File.Exists(file) ? File.ReadAllText(file) : "", key, value));
        }

        /// <summary>The controller in the game's remap file, unless the player saved their own for it in RetroArch's menu.</summary>
        private static void SetController(string core, string rom, int device)
        {
            static string? Read(string file) => File.Exists(file) ? File.ReadAllText(file) : null;
            string file = Layout.GameRemapFile(core, rom);
            if (RetroControllers.Remap(Read(file), Read(Layout.FolderRemapFile(core, rom)) ?? Read(Layout.CoreRemapFile(core)), device) is not string remap)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, remap);
        }

        /// <summary>The game's CRT preset written when its console has the look, removed when it no longer has it.</summary>
        private static void SetPicture(RetroSystem system, string rom)
        {
            bool crt = CrtSystems.Contains(system.Slug);
            foreach (var (extension, _) in RetroArchLayout.CrtShaderUrls)
            {
                string preset = Layout.GamePreset(system.Core, rom, extension);
                if (crt && File.Exists(Layout.CrtShaderFile(extension)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(preset)!);
                    File.WriteAllText(preset, Layout.CrtPreset(extension));
                }
                else if (File.Exists(preset))
                    File.Delete(preset);
            }
        }

        /// <summary>The BIOS RomM keeps for the platform, fetched when missing. Without network, the ones already here.</summary>
        private static async Task EnsureBiosAsync(RetroPlatform platform, string folder, Action<string> status)
        {
            List<RetroService.Firmware> firmware;
            try { firmware = await RetroService.ListFirmwareAsync(platform); }
            catch (Exception ex) { Log.Ignored(ex); firmware = new(); }
            Directory.CreateDirectory(folder);
            foreach (RetroService.Firmware bios in firmware)
            {
                // A name from the network stays a name: nothing is written outside the folder
                string file = Path.Combine(folder, Path.GetFileName(bios.FileName));
                if (File.Exists(file))
                    continue;
                status(Loc.F("Downloading the BIOS {0}...", Path.GetFileName(file)));
                await RetroService.DownloadFirmwareAsync(bios, file);
            }
            if (!Directory.EnumerateFiles(folder).Any(f => !Path.GetFileName(f).StartsWith('.')))
                throw new MissingBiosException(Loc.F("{0} needs a BIOS: an administrator adds it in RomM, in the Firmware of the platform.", platform.Name));
        }

        /// <summary>Back to RetroArch's own settings: the core options and controls changed in its menu go.</summary>
        public static void ResetMenuSettings()
        {
            if (Directory.Exists(Layout.MenuSettingsDirectory))
                Directory.Delete(Layout.MenuSettingsDirectory, true);
        }

        public static string SavesFolder => Layout.SavesDirectory(Player);
        public static string SaveBackupsFolder => Layout.SaveBackupsDirectory(Player);

        /// <summary>RetroArch's own menu, to change the controls or the picture.</summary>
        public static async Task<Process> OpenMenuAsync(Action<string> status)
        {
            await preparing.WaitAsync();
            try { await InstallRetroArchAsync(status); }
            finally { preparing.Release(); }
            return Start(Layout.MenuArguments);
        }

        /// <summary>RetroArch alone, without a console's core (Régler mes touches reads the gamepad with its SDL2).</summary>
        public static async Task EnsureRetroArchAsync(Action<string> status)
        {
            await preparing.WaitAsync();
            try { await InstallRetroArchAsync(status); }
            finally { preparing.Release(); }
        }

        /// <summary>
        /// The SDL2 library RetroArch is played with (its SDL2 driver's numbers are the client's): beside it on Windows, taken
        /// out of its AppImage once on Linux (with the libraries it needs, found beside it). Null when it can not be had.
        /// </summary>
        public static async Task<string?> Sdl2LibraryAsync()
        {
            if (OperatingSystem.IsWindows() || Sdl2Library != null)
                return Sdl2Library;
            string folder = Path.Combine(Layout.Directory, "sdl2"), library = Path.Combine(folder, "libSDL2-2.0.so.0"), version = Path.Combine(folder, "version");
            string work = Path.Combine(Layout.Directory, ".sdl2");
            try
            {
                if (Directory.Exists(work))
                    Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                using (Process unpack = Process.Start(new ProcessStartInfo(Layout.Executable) { ArgumentList = { "--appimage-extract", "usr/lib/*" }, WorkingDirectory = work, UseShellExecute = false, RedirectStandardOutput = true })!)
                {
                    _ = unpack.StandardOutput.ReadToEndAsync();
                    await unpack.WaitForExitAsync();
                }
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
                Directory.Move(Path.Combine(work, "squashfs-root", "usr", "lib"), folder);
                File.WriteAllText(version, RetroArchLayout.Version);
                return File.Exists(library) ? library : null;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
            finally
            {
                if (Directory.Exists(work))
                    Directory.Delete(work, true);
            }
        }

        /// <summary>RetroArch's SDL2 library when it is there (see <see cref="Sdl2LibraryAsync"/>), else null.</summary>
        public static string? Sdl2Library
        {
            get
            {
                if (OperatingSystem.IsWindows())
                    return File.Exists(Path.Combine(Layout.AppDirectory, "SDL2.dll")) ? Path.Combine(Layout.AppDirectory, "SDL2.dll") : null;
                string folder = Path.Combine(Layout.Directory, "sdl2"), version = Path.Combine(folder, "version");
                return File.Exists(Path.Combine(folder, "libSDL2-2.0.so.0")) && File.Exists(version) && File.ReadAllText(version) == RetroArchLayout.Version
                    ? Path.Combine(folder, "libSDL2-2.0.so.0") : null;
            }
        }

        /// <summary>RetroArch at the pinned version, the player's folders, and the configuration written again.</summary>
        private static async Task InstallRetroArchAsync(Action<string> status)
        {
            Directory.CreateDirectory(Layout.Directory);
            if (!Layout.IsInstalled)
            {
                string work = Path.Combine(Layout.Directory, ".download");
                // Shown first: removing what a stopped installation left can take a while
                status(Loc.F("Installing RetroArch, once for all the games: downloading {0:0}%", 0));
                if (Directory.Exists(work))
                    Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string archive = Path.Combine(work, "RetroArch.7z");
                await DownloadAsync(Layout.PackageUrl, archive, done => status(Loc.F("Installing RetroArch, once for all the games: downloading {0:0}%", done * 100)));
                status(Loc.F("Installing RetroArch, once for all the games: unpacking {0}%", 0));
                var sevenZip = new SevenZipHelper();
                sevenZip.Process += (_, e) => status(Loc.F("Installing RetroArch, once for all the games: unpacking {0}%", e.PercentageDone));
                if (await sevenZip.ExtractArchive(archive, work, excludedNames: RetroArchLayout.UnusedPackageFolders) != 0)
                    throw new IOException(sevenZip.LastError);
                if (Directory.Exists(Layout.AppDirectory))
                    Directory.Delete(Layout.AppDirectory, true);
                Directory.Move(Path.Combine(work, Path.GetFileName(Layout.AppDirectory)), Layout.AppDirectory);
                Directory.Delete(work, true);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(Layout.Executable, File.GetUnixFileMode(Layout.Executable) | UnixFileMode.UserExecute);
                File.WriteAllText(Layout.VersionFile, RetroArchLayout.Version);
            }
            string player = Player;
            Layout.AdoptSharedSaves(player);
            foreach (string folder in Layout.Folders(player))
                Directory.CreateDirectory(folder);
            File.WriteAllText(Layout.ConfigFile, Layout.Config(player, RetroAchievementsAccount.Account));
        }

        internal static async Task DownloadAsync(string url, string file, Action<double> progress)
        {
            using HttpResponseMessage response = await HttpClients.Shared.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using Stream body = await response.Content.ReadAsStreamAsync();
            await using FileStream target = File.Create(file);
            byte[] buffer = new byte[1 << 16];
            long done = 0, shown = 0;
            int read;
            while ((read = await body.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                // Every 2 MB: enough for the percentage
                if (total > 0 && done - shown > 2 << 20)
                {
                    shown = done;
                    progress((double)done / total);
                }
            }
        }

        /// <summary>RetroArch, its cores, their files, the BIOS and the CRT look: what goes with "Uninstall" (the saves stay).</summary>
        public static IEnumerable<string> InstalledParts => new[] { Layout.AppDirectory, Layout.CoresDirectory, Layout.SystemDirectory, Layout.ShadersDirectory, Path.Combine(Layout.Directory, "sdl2") };

        /// <summary>RetroArch, RPCS3 and Eden, as one "emulators" line of the storage.</summary>
        public static long InstalledSize() => InstalledParts.Where(Directory.Exists).Sum(GameVault.Core.Storage.StorageCleanup.DirectorySize) + StandaloneEmulator.All.Sum(e => e.InstalledSize());

        /// <summary>Taken again from the internet at the next game; the saves and their backups are kept.</summary>
        public static async Task UninstallAsync()
        {
            await preparing.WaitAsync();
            try
            {
                foreach (string part in InstalledParts.Where(Directory.Exists))
                    Directory.Delete(part, true);
                // RetroArch may never have been installed (only PS3 or Switch games played)
                foreach (string file in new[] { Layout.VersionFile, Layout.ConfigFile }.Where(File.Exists))
                    File.Delete(file);
            }
            finally { preparing.Release(); }
            foreach (StandaloneEmulator emulator in StandaloneEmulator.All)
                await emulator.UninstallAsync();
        }

        /// <param name="language">The console's, when it has one: see <see cref="LanguageOf"/>.</param>
        /// <param name="controller">Its first port's, when the console has several: see <see cref="ControllerOf"/>.</param>
        /// <param name="stateSlot">The save state the game starts from, or none.</param>
        public static Process Launch(RetroSystem system, string rom, string language, int? controller = null, int? stateSlot = null)
        {
            try
            {
                SetPicture(system, rom);
                if (Resolutions.TryGetValue(system.Core, out int scale))
                    foreach (var option in RetroResolution.CoreOptions(system.Core, scale))
                        SetOption(system.Core, option);
                foreach (var option in RetroResolution.WidescreenOptions(system.Core, WidescreenCores.Contains(system.Core)))
                    SetOption(system.Core, option);
                SetOption(system.Core, RetroLanguage.CoreOption(system.Core, language));
                if (controller is int device)
                    SetController(system.Core, rom, device);
                SetKeyboard(system, controller);
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return Start(Layout.Arguments(system.Core, rom, stateSlot));
        }

        private static Process Start(IReadOnlyList<string> arguments)
        {
            var info = new ProcessStartInfo(Layout.Executable) { UseShellExecute = false, WorkingDirectory = Layout.AppDirectory };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            // Without libfuse2 an AppImage can not mount itself: it unpacks to /tmp instead (10 MB, each start)
            if (!PlatformInfo.IsWindows)
                info.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
            return Process.Start(info) ?? throw new InvalidOperationException("RetroArch could not be started");
        }
    }
}
