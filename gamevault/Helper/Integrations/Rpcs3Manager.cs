using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// RPCS3 for the PlayStation 3 games, with the console's firmware from RomM, which RPCS3 installs itself once the
    /// player answers Yes in its window.
    /// </summary>
    internal sealed class Rpcs3Manager : StandaloneEmulator
    {
        public override string Name => "RPCS3";
        public override string Core => "rpcs3";
        public override string Console => "PS3";
        public override Rpcs3Layout Layout { get; } = new(Path.Combine(PlatformInfo.LocalDataDirectory, "rpcs3"), PlatformInfo.IsWindows);

        public override string FirmwareState => Layout.HasFirmware
            ? Loc.T("Firmware installed in RPCS3")
            : Loc.T("Not downloaded yet");

        /// <summary>PS3UPDAT.PUP from RomM, installed by RPCS3: its window asks first; it is closed once its log tells it is done.</summary>
        protected override async Task InstallFirmwareAsync(RetroPlatform platform, Action<string> status)
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

        private bool LogSays(string line)
        {
            try
            {
                using var log = new FileStream(Layout.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return new StreamReader(log).ReadToEnd().Contains(line, StringComparison.Ordinal);
            }
            catch (IOException) { return false; }
        }
    }
}
