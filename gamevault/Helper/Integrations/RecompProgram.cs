using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// A recomp an administrator gave a game (see <see cref="GameEmulators"/>): a program of its own that reads the game's
    /// files. Installed at the first game in its own folder, where it keeps its saves and what it builds, then started.
    /// </summary>
    internal sealed class RecompProgram
    {
        private readonly SemaphoreSlim preparing = new(1, 1);

        public RecompProgram(GameEmulators.Emulator definition)
        {
            Name = definition.Name;
            Layout = new RecompLayout(Path.Combine(PlatformInfo.LocalDataDirectory, "emulators", definition.Id),
                definition.Windows, definition.Linux, definition.Mac, PlatformInfo.IsWindows, PlatformInfo.IsMac, definition.Executable, definition.Arguments);
        }

        public string Name { get; }
        public RecompLayout Layout { get; }

        /// <summary>Its build from the link given, over the one installed when the link changed.</summary>
        public async Task EnsureReadyAsync(Action<string> status)
        {
            await preparing.WaitAsync();
            try
            {
                if (Layout.IsInstalled)
                    return;
                Directory.CreateDirectory(Layout.Directory);
                string work = Path.Combine(Layout.Directory, ".download");
                if (Directory.Exists(work))
                    Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string package = Path.Combine(work, Path.GetFileName(new Uri(Layout.PackageUrl).LocalPath));
                await RetroArchManager.DownloadAsync(Layout.PackageUrl, package, done => status(Loc.F("Installing {0}: downloading {1:0}%", Name, done * 100)));
                if (Layout.IsAppImage)
                    File.Move(package, Layout.Executable, overwrite: true);
                else
                {
                    status(Loc.F("Installing {0}: unpacking {1}%", Name, 0));
                    string unpacked = Path.Combine(work, "unpacked");
                    var sevenZip = new SevenZipHelper();
                    sevenZip.Process += (_, e) => status(Loc.F("Installing {0}: unpacking {1}%", Name, e.PercentageDone));
                    if (await sevenZip.ExtractArchive(package, unpacked) != 0)
                        throw new IOException(sevenZip.LastError);
                    RecompLayout.Merge(RecompLayout.Root(unpacked), Layout.Directory);
                }
                Directory.Delete(work, true);
                if (!File.Exists(Layout.Executable))
                    throw new FileNotFoundException(Loc.F("The build of {0} has no program '{1}': an administrator names it in the admin console.", Name, Path.GetFileName(Layout.Executable)));
                if (!PlatformInfo.IsWindows)
                    File.SetUnixFileMode(Layout.Executable, File.GetUnixFileMode(Layout.Executable) | UnixFileMode.UserExecute);
                File.WriteAllText(Layout.VersionFile, Layout.PackageUrl);
            }
            finally { preparing.Release(); }
        }

        /// <summary>Started beside its files, where it looks for them.</summary>
        /// <param name="rom">The game's file and folder for its arguments; null: its window alone (its settings).</param>
        public Process Start(string? rom, string? folder)
        {
            var info = new ProcessStartInfo(Layout.Executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Layout.Executable)! };
            if (rom != null)
                foreach (string argument in Layout.Arguments(rom, folder ?? Path.GetDirectoryName(rom)!))
                    info.ArgumentList.Add(argument);
            return Process.Start(info) ?? throw new InvalidOperationException(Loc.F("{0} could not be started", Name));
        }

        /// <summary>
        /// Until the program and every program of its folder are closed: a recomp is often a launcher that starts
        /// the game it built, then closes.
        /// </summary>
        public async Task WhenClosedAsync(Process started)
        {
            await started.WaitForExitAsync();
            do
                await Task.Delay(Poll);
            while (IsRunning(Layout.Directory));
        }

        internal static TimeSpan Poll { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>A program of this folder runs.</summary>
        internal static bool IsRunning(string directory)
        {
            string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison comparison = PlatformInfo.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            bool running = false;
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    // Another user's or the system's programs can not be read: not ours
                    if (!running && process.MainModule?.FileName is string file && file.StartsWith(prefix, comparison))
                        running = true;
                }
                catch (Exception) { }
                finally { process.Dispose(); }
            }
            return running;
        }
    }
}
