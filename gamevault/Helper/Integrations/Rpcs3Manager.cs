using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// RPCS3 for the PlayStation 3 games, installed the first time one is played (in the client's data folder), with
    /// the console's firmware from RomM, which RPCS3 installs itself once the player answers Yes in its window.
    /// </summary>
    internal static class Rpcs3Manager
    {
        public static Rpcs3Layout Layout { get; } = new(Path.Combine(PlatformInfo.LocalDataDirectory, "rpcs3"), PlatformInfo.IsWindows);
        private static readonly SemaphoreSlim preparing = new(1, 1);

        /// <summary>RPCS3, then the console's firmware.</summary>
        public static async Task EnsureReadyAsync(RetroPlatform platform, Action<string> status)
        {
            await preparing.WaitAsync();
            try
            {
                await InstallAsync(status);
                if (!Layout.HasFirmware)
                    await InstallFirmwareAsync(platform, status);
            }
            finally { preparing.Release(); }
        }

        private static async Task InstallAsync(Action<string> status)
        {
            Directory.CreateDirectory(Layout.Directory);
            if (!Layout.IsInstalled)
            {
                string work = Path.Combine(Layout.Directory, ".download");
                if (Directory.Exists(work))
                    Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string package = Path.Combine(work, Path.GetFileName(new Uri(Layout.PackageUrl).LocalPath));
                await RetroArchManager.DownloadAsync(Layout.PackageUrl, package, done => status(Loc.F("Installing RPCS3, once for all the PS3 games: downloading {0:0}%", done * 100)));
                status(Loc.F("Installing RPCS3, once for all the PS3 games: unpacking {0}%", 0));
                if (PlatformInfo.IsWindows)
                {
                    // Over the previous version: its console storage (beside it) stays
                    var sevenZip = new SevenZipHelper();
                    sevenZip.Process += (_, e) => status(Loc.F("Installing RPCS3, once for all the PS3 games: unpacking {0}%", e.PercentageDone));
                    if (await sevenZip.ExtractArchive(package, Layout.Directory) != 0)
                        throw new IOException(sevenZip.LastError);
                }
                else
                {
                    File.SetUnixFileMode(package, File.GetUnixFileMode(package) | UnixFileMode.UserExecute);
                    using (Process unpack = Process.Start(new ProcessStartInfo(package, "--appimage-extract") { WorkingDirectory = work, UseShellExecute = false, RedirectStandardOutput = true })!)
                    {
                        _ = unpack.StandardOutput.ReadToEndAsync();
                        await unpack.WaitForExitAsync();
                    }
                    // squashfs-root is a link to the folder it unpacked to (AppDir)
                    var root = new DirectoryInfo(Path.Combine(work, "squashfs-root"));
                    string unpacked = root.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? root.FullName;
                    if (Directory.Exists(Layout.AppDirectory))
                        Directory.Delete(Layout.AppDirectory, true);
                    Directory.Move(unpacked, Layout.AppDirectory);
                }
                Directory.Delete(work, true);
                File.WriteAllText(Layout.VersionFile, Rpcs3Layout.Version);
            }
            foreach (string file in Layout.GuiSettingsFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, Rpcs3Layout.GuiSettings(File.Exists(file) ? File.ReadAllText(file) : ""));
            }
        }

        /// <summary>PS3UPDAT.PUP from RomM, installed by RPCS3: its window asks first; it is closed once its log tells it is done.</summary>
        private static async Task InstallFirmwareAsync(RetroPlatform platform, Action<string> status)
        {
            List<RetroService.Firmware> firmware;
            try { firmware = await RetroService.ListFirmwareAsync(platform); }
            catch (Exception ex) { Log.Ignored(ex); firmware = new(); }
            RetroService.Firmware? pup = firmware.FirstOrDefault(f => f.FileName.Equals(Rpcs3Layout.FirmwareFile, StringComparison.OrdinalIgnoreCase))
                ?? firmware.FirstOrDefault(f => f.FileName.EndsWith(".pup", StringComparison.OrdinalIgnoreCase));
            if (pup == null)
                throw new RetroArchManager.MissingBiosException(Loc.F("{0} needs its firmware (PS3UPDAT.PUP): an administrator adds it in RomM, in the Firmware of the platform.", platform.Name));
            string file = Path.Combine(Layout.Directory, Rpcs3Layout.FirmwareFile);
            status(Loc.T("Downloading the PS3 firmware..."));
            await RetroService.DownloadFirmwareAsync(pup, file);
            try
            {
                status(Loc.T("RPCS3 asks to install the PS3 firmware: answer Yes in its window"));
                using Process process = Start(Rpcs3Layout.FirmwareArguments(file));
                while (!process.HasExited)
                {
                    await Task.Delay(1000);
                    if (Layout.HasFirmware && LogSays(Rpcs3Layout.FirmwareInstalledLine))
                    {
                        await Task.Delay(1500);
                        process.Kill(entireProcessTree: true);
                        break;
                    }
                }
                await process.WaitForExitAsync();
            }
            finally { File.Delete(file); }
            if (!Layout.HasFirmware)
                throw new RetroArchManager.MissingBiosException(Loc.T("The PS3 firmware was not installed: play again and answer Yes in RPCS3's window."));
        }

        private static bool LogSays(string line)
        {
            try
            {
                using var log = new FileStream(Layout.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return new StreamReader(log).ReadToEnd().Contains(line, StringComparison.Ordinal);
            }
            catch (IOException) { return false; }
        }

        /// <summary>RPCS3's own window: its controllers, its settings.</summary>
        public static async Task<Process> OpenAsync(Action<string> status)
        {
            await preparing.WaitAsync();
            try { await InstallAsync(status); }
            finally { preparing.Release(); }
            return Start(Array.Empty<string>());
        }

        public static Process Launch(string player, string launchFile) => Start(Layout.Arguments(player, launchFile));

        private static Process Start(IReadOnlyList<string> arguments)
        {
            var info = new ProcessStartInfo(Layout.Executable) { UseShellExecute = false, WorkingDirectory = Layout.AppDirectory };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            foreach (var (name, value) in Layout.Environment)
                info.Environment[name] = value;
            return Process.Start(info) ?? throw new InvalidOperationException("RPCS3 could not be started");
        }

        /// <summary>RPCS3 and the firmware, its caches: what goes with "Uninstall" (the console's storage, with the saves, stays).</summary>
        public static long InstalledSize()
        {
            if (!Directory.Exists(Layout.Directory))
                return 0;
            return GameVault.Core.Storage.StorageCleanup.DirectorySize(Layout.Directory)
                - Layout.KeptPaths.Where(Directory.Exists).Sum(GameVault.Core.Storage.StorageCleanup.DirectorySize);
        }

        public static async Task UninstallAsync()
        {
            await preparing.WaitAsync();
            try
            {
                if (Directory.Exists(Layout.Directory))
                    DeleteExcept(Layout.Directory, Layout.KeptPaths.Select(Path.GetFullPath).ToHashSet());
            }
            finally { preparing.Release(); }
        }

        private static void DeleteExcept(string folder, HashSet<string> kept)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFullPath))
            {
                if (kept.Contains(entry))
                    continue;
                if (kept.Any(k => k.StartsWith(entry + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                    DeleteExcept(entry, kept);
                else if (Directory.Exists(entry) && !File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(entry, true);
                else
                    File.Delete(entry);
            }
        }
    }
}
