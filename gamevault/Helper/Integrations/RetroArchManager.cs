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
        /// <summary>One set for all the emulators: RetroArch keeps the saves of every core in the player's folder.</summary>
        private const string SavesSet = "retroarch";

        private static string Player => LoginManager.Instance.GetCurrentUser()?.ID.ToString() ?? "0";

        public sealed class MissingBiosException(string message) : Exception(message);

        /// <summary>RetroArch, then what the platform needs: its core, the core's data files and the BIOS.</summary>
        public static async Task EnsureReadyAsync(RetroPlatform platform, Action<string> status)
        {
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
            }
            finally { preparing.Release(); }
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

        public const string SavesSetName = SavesSet;

        public static Task<List<RetroService.SavesVersion>> SavesHistoryAsync() => RetroService.SavesHistoryAsync(SavesSet);

        /// <summary>
        /// An older version of the saves back in place (this PC's go to a backup first), then sent as the current ones
        /// so the other PCs take it too.
        /// </summary>
        /// <returns>The backup of this PC's saves, when there were some.</returns>
        public static async Task<string?> RestoreSavesAsync(RetroService.SavesVersion version)
        {
            byte[] zip = await RetroService.DownloadSavesVersionAsync(SavesSet, version.Name);
            RetroSaveSync saves = CloudSaves();
            string? backup = await Task.Run(() => saves.Unpack(new MemoryStream(zip)));
            string packed = Path.Combine(Layout.Directory, $".saves-{Player}.zip");
            try
            {
                await Task.Run(() =>
                {
                    using FileStream target = File.Create(packed);
                    saves.Pack(target);
                });
                long savedAt;
                await using (FileStream source = File.OpenRead(packed))
                    savedAt = await RetroService.UploadSavesAsync(SavesSet, source);
                saves.Remember(savedAt);
            }
            finally { File.Delete(packed); }
            return backup;
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
            File.WriteAllText(Layout.ConfigFile, Layout.Config(player));
        }

        private static RetroSaveSync CloudSaves() => new(Layout.SavesDirectory(Player), Layout.SaveBackupsDirectory(Player));

        /// <summary>Before a game: the player's saves from another PC, when they are newer. Without network, the ones here.</summary>
        /// <returns>Where this PC's own saves were kept when they had changed too; null otherwise.</returns>
        public static async Task<string?> TakeCloudSavesAsync(Action<string> status)
        {
            // The same switch as the library's cloud saves (Settings)
            if (!SettingsViewModel.Instance.CloudSaves)
                return null;
            RetroSaveSync saves = CloudSaves();
            try
            {
                if (await RetroService.SavesInfoAsync(SavesSet) is not long savedAt || !saves.IsOlderThan(savedAt))
                    return null;
                status(Loc.T("Getting the saves from the other PCs..."));
                byte[] zip = await RetroService.DownloadSavesAsync(SavesSet);
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

        /// <summary>After a game: the saves go to the service when they changed. Without network, after a next game.</summary>
        public static async Task SendCloudSavesAsync()
        {
            if (!SettingsViewModel.Instance.CloudSaves)
                return;
            RetroSaveSync saves = CloudSaves();
            string zip = Path.Combine(Layout.Directory, $".saves-{Player}.zip");
            try
            {
                if (!await Task.Run(() => saves.ChangedSinceSync))
                    return;
                await Task.Run(() =>
                {
                    using FileStream target = File.Create(zip);
                    saves.Pack(target);
                });
                long savedAt;
                await using (FileStream source = File.OpenRead(zip))
                    savedAt = await RetroService.UploadSavesAsync(SavesSet, source);
                saves.Remember(savedAt);
            }
            catch (Exception ex) { Log.Ignored(ex); }
            finally { File.Delete(zip); }
        }

        private static async Task DownloadAsync(string url, string file, Action<double> progress)
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

        /// <summary>RetroArch, its cores, their files and the BIOS: what goes with "Uninstall" (the saves stay).</summary>
        public static IEnumerable<string> InstalledParts => new[] { Layout.AppDirectory, Layout.CoresDirectory, Layout.SystemDirectory };

        public static long InstalledSize() => InstalledParts.Where(Directory.Exists).Sum(GameVault.Core.Storage.StorageCleanup.DirectorySize);

        /// <summary>Taken again from the internet at the next game; the saves and their backups are kept.</summary>
        public static async Task UninstallAsync()
        {
            await preparing.WaitAsync();
            try
            {
                foreach (string part in InstalledParts.Where(Directory.Exists))
                    Directory.Delete(part, true);
                foreach (string file in new[] { Layout.VersionFile, Layout.ConfigFile })
                    File.Delete(file);
            }
            finally { preparing.Release(); }
        }

        public static Process Launch(string core, string rom) => Start(Layout.Arguments(core, rom));

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
