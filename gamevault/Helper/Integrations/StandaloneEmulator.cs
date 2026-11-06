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
    /// An emulator of its own for a console RetroArch does not play (RPCS3, Eden, Eglantine, xemu): installed the first time one of its
    /// games is played (in the client's data folder), with the console's system files from RomM, then started on the game.
    /// </summary>
    internal abstract class StandaloneEmulator
    {
        public static Rpcs3Manager Rpcs3 { get; } = new();
        public static EdenManager Eden { get; } = new();
        public static EglantineManager Eglantine { get; } = new();
        public static XemuManager Xemu { get; } = new();
        public static IEnumerable<StandaloneEmulator> All => new StandaloneEmulator[] { Rpcs3, Eden, Eglantine, Xemu };
        /// <summary>The emulator an administrator gave the game for this system (see <see cref="GameEmulators"/>), else its console's; null for RetroArch.</summary>
        public static StandaloneEmulator? Of(RetroGame game) => GameEmulators.For(game) ?? Of(game.Platform.System);
        public static StandaloneEmulator? Of(RetroSystem system) => system.IsRpcs3 ? Rpcs3 : system.IsEden ? Eden : system.IsEglantine ? Eglantine : system.IsXemu ? Xemu : null;

        /// <summary>"RPCS3".</summary>
        public abstract string Name { get; }
        /// <summary>"PS3": in the texts.</summary>
        public abstract string Console { get; }
        public abstract StandaloneLayout Layout { get; }
        private readonly SemaphoreSlim preparing = new(1, 1);

        /// <summary>The emulator, then the console's system files.</summary>
        public async Task EnsureReadyAsync(RetroPlatform platform, Action<string> status)
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

        protected internal abstract Task InstallFirmwareAsync(RetroPlatform platform, Action<string> status);

        /// <summary>What a settings row tells of the console's system files.</summary>
        public abstract string FirmwareState { get; }

        private async Task InstallAsync(Action<string> status)
        {
            Directory.CreateDirectory(Layout.Directory);
            if (!Layout.IsInstalled)
            {
                string work = Path.Combine(Layout.Directory, ".download");
                if (Directory.Exists(work))
                    Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string package = Path.Combine(work, Path.GetFileName(new Uri(Layout.PackageUrl).LocalPath));
                await RetroArchManager.DownloadAsync(Layout.PackageUrl, package, done => status(Loc.F("Installing {0}, once for all the {1} games: downloading {2:0}%", Name, Console, done * 100)));
                status(Loc.F("Installing {0}, once for all the {1} games: unpacking {2}%", Name, Console, 0));
                async Task UnpackAsync(string folder)
                {
                    var sevenZip = new SevenZipHelper();
                    sevenZip.Process += (_, e) => status(Loc.F("Installing {0}, once for all the {1} games: unpacking {2}%", Name, Console, e.PercentageDone));
                    string into = Layout.PackageInFolder ? Path.Combine(work, "unpacked") : folder;
                    if (await sevenZip.ExtractArchive(package, into) != 0)
                        throw new IOException(sevenZip.LastError);
                    if (Layout.PackageInFolder)
                        // Its files in one folder (a release's name), else the files themselves
                        MoveInto(Directory.GetFiles(into).Length == 0 && Directory.GetDirectories(into) is [string single] ? single : into, folder);
                }
                if (PlatformInfo.IsWindows)
                    // Over the previous version: its console storage (beside it) stays
                    await UnpackAsync(Layout.Directory);
                else if (PlatformInfo.IsMac)
                {
                    // Its app in a disk image (Eden), else in a 7z or a zip (7-Zip keeps the app's links); its storage is outside the app's folder
                    if (Directory.Exists(Layout.AppDirectory))
                        Directory.Delete(Layout.AppDirectory, true);
                    if (package.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                        await MacDiskImage.CopyAppAsync(package, Layout.MacApp, work, Layout.AppDirectory);
                    else
                        await UnpackAsync(Layout.AppDirectory);
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
                File.WriteAllText(Layout.VersionFile, Layout.Version);
            }
            foreach (var (link, target) in Layout.Links.Where(l => !Directory.Exists(l.Link)))
            {
                Directory.CreateDirectory(target);
                await CreateLinkAsync(link, target);
            }
            foreach (string file in Layout.SettingsFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, Layout.Settings(File.Exists(file) ? File.ReadAllText(file) : ""));
            }
        }

        /// <summary>A link to a folder; on Windows a junction, which needs neither the administrator nor the developer mode a link needs.</summary>
        private static async Task CreateLinkAsync(string link, string target)
        {
            if (!PlatformInfo.IsWindows)
            {
                Directory.CreateSymbolicLink(link, target);
                return;
            }
            var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "/c", "mklink", "/J", link, target })
                info.ArgumentList.Add(argument);
            using Process mklink = Process.Start(info)!;
            string error = await mklink.StandardError.ReadToEndAsync();
            await mklink.WaitForExitAsync();
            if (mklink.ExitCode != 0)
                throw new IOException(Loc.F("The folder {0} could not be linked: {1}", link, error.Trim()));
        }

        /// <summary>A folder's files and folders moved into another, over the ones there of the same name (the others stay: the console's storage).</summary>
        private static void MoveInto(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.Move(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            foreach (string folder in Directory.GetDirectories(from))
            {
                string target = Path.Combine(to, Path.GetFileName(folder));
                if (Directory.Exists(target))
                    Directory.Delete(target, true);
                Directory.Move(folder, target);
            }
        }

        /// <summary>The emulator's own window: its controllers, its settings.</summary>
        public async Task<Process> OpenAsync(Action<string> status)
        {
            await preparing.WaitAsync();
            try { await InstallAsync(status); }
            finally { preparing.Release(); }
            return Start(Array.Empty<string>());
        }

        /// <summary>What the player has to do once for their controller, shown while the game runs; none when it is ready.</summary>
        public virtual string? ControllerHint => null;

        /// <summary>Its platform's core, as the settings know it ("rpcs3").</summary>
        public abstract string Core { get; }

        /// <param name="language">The console's: see <see cref="RetroArchManager.LanguageOf"/>.</param>
        public virtual Process Launch(string player, string launchFile, string language)
        {
            // The definition chosen in Settings > Retro; none chosen, the emulator's own setting stays
            bool scaled = RetroArchManager.Resolutions.TryGetValue(Core, out int scale);
            foreach (string file in Layout.ResolutionFiles)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    string config = Layout.Language(File.Exists(file) ? File.ReadAllText(file) : "", language);
                    File.WriteAllText(file, scaled ? Layout.Resolution(config, scale) : config);
                }
                catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
            }
            return Start(Layout.Arguments(player, launchFile));
        }

        protected Process Start(IReadOnlyList<string> arguments)
        {
            var info = new ProcessStartInfo(Layout.Executable) { UseShellExecute = false, WorkingDirectory = Layout.WorkingDirectory };
            foreach (string argument in Layout.StartArguments.Concat(arguments))
                info.ArgumentList.Add(argument);
            foreach (var (name, value) in Layout.Environment)
                info.Environment[name] = value;
            return Process.Start(info) ?? throw new InvalidOperationException(Loc.F("{0} could not be started", Name));
        }

        /// <summary>The emulator, the system files, its caches: what goes with "Uninstall" (the console's storage, with the saves, stays).</summary>
        public long InstalledSize()
        {
            if (!Directory.Exists(Layout.Directory))
                return 0;
            return GameVault.Core.Storage.StorageCleanup.DirectorySize(Layout.Directory)
                - Layout.KeptPaths.Where(Directory.Exists).Sum(GameVault.Core.Storage.StorageCleanup.DirectorySize);
        }

        public async Task UninstallAsync()
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
                else if (Directory.Exists(entry) && PlatformInfo.IsWindows)
                    // A junction: the link goes, not what it leads to
                    Directory.Delete(entry);
                else
                    File.Delete(entry);
            }
        }
    }
}
