using GameVault.Core.Integrations;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Moonlight, taken once from its releases (Windows' portable build, Linux's AppImage): it shows a room member's shared
    /// screen and sends this PC's gamepads to it. Paired once per host, with the PIN its client types there.
    /// </summary>
    internal static class MoonlightClient
    {
        public const string Version = "6.2.0";

        private static string Folder => Path.Combine(PlatformInfo.LocalDataDirectory, "moonlight", Version);
        private static string Executable => Path.Combine(Folder, PlatformInfo.IsWindows ? "Moonlight.exe" : "Moonlight.AppImage");
        private static string PackageUrl => $"https://github.com/moonlight-stream/moonlight-qt/releases/download/v{Version}/"
            + (PlatformInfo.IsWindows ? $"MoonlightPortable-x64-{Version}.zip" : $"Moonlight-{Version}-x86_64.AppImage");

        private static async Task InstallAsync(Action<string> status)
        {
            if (File.Exists(Executable))
                return;
            Directory.CreateDirectory(Folder);
            string part = Path.Combine(Folder, "download.part");
            await RetroArchManager.DownloadAsync(PackageUrl, part, done => status(Loc.F("Installing Moonlight, once: downloading {0:0}%", done * 100)));
            if (PlatformInfo.IsWindows)
            {
                ZipFile.ExtractToDirectory(part, Folder, overwriteFiles: true);
                File.Delete(part);
            }
            else
            {
                File.SetUnixFileMode(part, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.Move(part, Executable, overwrite: true);
            }
        }

        private static ProcessStartInfo Info(params string[] arguments)
        {
            // Windows' portable build keeps its settings beside it when started from its folder (its portable.dat)
            var info = new ProcessStartInfo(Executable) { UseShellExecute = false, WorkingDirectory = Folder, CreateNoWindow = true };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            if (!PlatformInfo.IsWindows)
                info.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
            return info;
        }

        /// <summary>"list" answers without a window: 0 once paired, else not paired or not reached.</summary>
        private static async Task<bool> PairedAsync(string host)
        {
            ProcessStartInfo info = Info("list", host);
            info.RedirectStandardOutput = info.RedirectStandardError = true;
            using Process process = Process.Start(info) ?? throw new InvalidOperationException(Loc.T("Moonlight could not be started."));
            await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync(), process.WaitForExitAsync());
            return process.ExitCode == 0;
        }

        /// <summary>
        /// The host's shared screen: Moonlight installed and paired the first time, then streaming its whole screen.
        /// Paired, Moonlight waits for a click on its window: no "list" meanwhile, two Moonlights at once write over
        /// each other's hosts (the pairing lost, seen with Sunshine).
        /// </summary>
        public static async Task JoinAsync(string host, string pin, Action<string> status)
        {
            await InstallAsync(status);
            if (!await PairedAsync(host))
            {
                status(Loc.T("Pairing with the host's PC: click OK in Moonlight once it says it is paired..."));
                using (Process pairing = Process.Start(Info("pair", host, "--pin", pin)) ?? throw new InvalidOperationException(Loc.T("Moonlight could not be started.")))
                    await pairing.WaitForExitAsync();
                if (!await PairedAsync(host))
                    throw new InvalidOperationException(Loc.T("The host's PC did not accept the pairing: check that its screen is still shared, then try again."));
            }
            status(Loc.T("Joining the shared screen... To leave it: Ctrl+Alt+Shift+Q."));
            Process.Start(Info("stream", host, ScreenSharing.DesktopApp))?.Dispose();
        }
    }
}
