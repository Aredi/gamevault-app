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
    /// xemu for the Xbox games, with the console's boot ROM and BIOS from RomM and xemu's free hard disk, nothing to
    /// answer; each GameVault player plays on their own hard disk, with their saves.
    /// </summary>
    internal sealed class XemuManager : StandaloneEmulator
    {
        public override string Name => "xemu";
        public override string Core => "xemu";
        public override string Console => "Xbox";
        public override XemuLayout Layout { get; } = new(Path.Combine(PlatformInfo.LocalDataDirectory, "xemu"), PlatformInfo.IsWindows, PlatformInfo.IsMac);

        public override string FirmwareState => Layout.HasFirmware
            ? Loc.T("Boot ROM, BIOS and hard disk installed in xemu")
            : Loc.T("Not downloaded yet");

        /// <summary>RomM's files told by their size (their names vary): the boot ROM's 512 bytes, a BIOS of 256 KB or 1 MB (Complex 4627 first).</summary>
        protected override async Task InstallFirmwareAsync(RetroPlatform platform, Action<string> status)
        {
            List<RetroService.Firmware> files;
            try { files = await RetroService.ListFirmwareAsync(platform); }
            catch (Exception ex) { Log.Ignored(ex); files = new(); }
            RetroService.Firmware? bootRom = files.FirstOrDefault(f => f.Size == XemuLayout.BootRomSize);
            RetroService.Firmware? bios = files.Where(f => XemuLayout.IsFlashRomSize(f.Size))
                .OrderByDescending(f => f.FileName.Contains("4627") || f.FileName.Contains("complex", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (bootRom == null || bios == null)
                throw new RetroArchManager.MissingBiosException(Loc.F("{0} needs its boot ROM (mcpx_1.0.bin, 512 bytes) and a modified BIOS (Complex 4627: a retail BIOS starts no game in xemu): an administrator adds them in RomM, in the Firmware of the platform.", platform.Name));

            status(Loc.T("Downloading the Xbox boot ROM and BIOS..."));
            Directory.CreateDirectory(Path.GetDirectoryName(Layout.BootRom)!);
            await RetroService.DownloadFirmwareAsync(bootRom, Layout.BootRom);
            await RetroService.DownloadFirmwareAsync(bios, Layout.FlashRom);
            if (!File.Exists(Layout.BaseHardDisk))
            {
                string part = Layout.BaseHardDisk + ".part";
                await RetroArchManager.DownloadAsync(XemuLayout.HardDiskUrl, part, done => status(Loc.F("Downloading the Xbox hard disk... {0:0}%", done * 100)));
                File.Move(part, Layout.BaseHardDisk, overwrite: true);
            }
        }

        /// <summary>The player's hard disk in the console, a copy of the empty one the first time.</summary>
        public override Process Launch(string player, string launchFile, string language)
        {
            string disk = Layout.HardDisk(player);
            if (!File.Exists(disk))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(disk)!);
                File.Copy(Layout.BaseHardDisk, disk);
            }
            // The buttons of Régler mes touches, for the gamepad plugged in now too
            var pad = RetroArchManager.Pad.GetValueOrDefault("xbox");
            string? plugged = pad != null && RetroArchManager.Sdl2Library is string library ? RetroGamepad.FirstControllerGuid(library) : null;
            string config = Layout.Controls(File.ReadAllText(Layout.ConfigFile), RetroArchManager.Keyboard.GetValueOrDefault("xbox"), pad, plugged);
            File.WriteAllText(Layout.ConfigFile, Layout.WithHardDisk(config, player));
            return base.Launch(player, launchFile, language);
        }
    }
}
