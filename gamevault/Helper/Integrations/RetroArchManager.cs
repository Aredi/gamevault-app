using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Installs RetroArch and the cores the first time they are needed (in the client's data folder), and starts the
    /// games with them. The saves stay in that folder, so deleting a game keeps them.
    /// </summary>
    internal static class RetroArchManager
    {
        public static RetroArchLayout Layout { get; } = new(Path.Combine(PlatformInfo.LocalDataDirectory, "retroarch"), PlatformInfo.IsWindows);
        private static readonly SemaphoreSlim preparing = new(1, 1);

        /// <summary>RetroArch at the pinned version and the core, then the configuration written again.</summary>
        public static async Task EnsureReadyAsync(string core, Action<string> status)
        {
            await preparing.WaitAsync();
            try
            {
                Directory.CreateDirectory(Layout.Directory);
                if (!Layout.IsInstalled)
                {
                    string work = Path.Combine(Layout.Directory, ".download");
                    if (Directory.Exists(work))
                        Directory.Delete(work, true);
                    Directory.CreateDirectory(work);
                    string archive = Path.Combine(work, "RetroArch.7z");
                    await DownloadAsync(Layout.PackageUrl, archive, done => status(Loc.F("Downloading RetroArch... {0:0}%", done * 100)));
                    status(Loc.T("Unpacking RetroArch..."));
                    var sevenZip = new SevenZipHelper();
                    if (await sevenZip.ExtractArchive(archive, work) != 0)
                        throw new IOException(sevenZip.LastError);
                    if (Directory.Exists(Layout.AppDirectory))
                        Directory.Delete(Layout.AppDirectory, true);
                    Directory.Move(Path.Combine(work, Path.GetFileName(Layout.AppDirectory)), Layout.AppDirectory);
                    Directory.Delete(work, true);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(Layout.Executable, File.GetUnixFileMode(Layout.Executable) | UnixFileMode.UserExecute);
                    File.WriteAllText(Layout.VersionFile, RetroArchLayout.Version);
                }
                foreach (string folder in Layout.Folders)
                    Directory.CreateDirectory(folder);
                if (!File.Exists(Layout.CoreFile(core)))
                {
                    status(Loc.T("Downloading the emulator..."));
                    using var zip = new ZipArchive(new MemoryStream(await HttpClients.Shared.GetByteArrayAsync(Layout.CoreUrl(core))));
                    zip.ExtractToDirectory(Layout.CoresDirectory, overwriteFiles: true);
                }
                File.WriteAllText(Layout.ConfigFile, Layout.Config());
            }
            finally { preparing.Release(); }
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

        public static Process Launch(string core, string rom)
        {
            var info = new ProcessStartInfo(Layout.Executable) { UseShellExecute = false, WorkingDirectory = Layout.AppDirectory };
            foreach (string argument in Layout.Arguments(core, rom))
                info.ArgumentList.Add(argument);
            // Without libfuse2 an AppImage can not mount itself: it unpacks to /tmp instead (10 MB, each start)
            if (!PlatformInfo.IsWindows)
                info.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
            return Process.Start(info) ?? throw new InvalidOperationException("RetroArch could not be started");
        }
    }
}
