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
        public static IEnumerable<string> InstalledParts => new[] { Layout.AppDirectory, Layout.CoresDirectory, Layout.SystemDirectory, Layout.ShadersDirectory };

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

        /// <param name="stateSlot">The save state the game starts from, or none.</param>
        public static Process Launch(RetroSystem system, string rom, int? stateSlot = null)
        {
            try { SetPicture(system, rom); }
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
