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
    /// Eden for the Nintendo Switch games, with the console's keys and firmware from RomM copied in it, nothing to
    /// answer; each GameVault player is the console's user who plays.
    /// </summary>
    internal sealed class EdenManager : StandaloneEmulator
    {
        public override string Name => "Eden";
        public override string Core => "eden";
        public override string Console => "Switch";
        public override EdenLayout Layout { get; } = new(Path.Combine(PlatformInfo.LocalDataDirectory, "eden"), PlatformInfo.IsWindows);

        public override string FirmwareState => Layout.HasFirmware
            ? Loc.T("Keys and firmware installed in Eden")
            : Loc.T("Not downloaded yet");

        /// <summary>prod.keys (title.keys too when there), and the firmware: a zip of its .nca files, or the files themselves.</summary>
        protected override async Task InstallFirmwareAsync(RetroPlatform platform, Action<string> status)
        {
            List<RetroService.Firmware> files;
            try { files = await RetroService.ListFirmwareAsync(platform); }
            catch (Exception ex) { Log.Ignored(ex); files = new(); }
            var keys = files.Where(f => f.FileName.Equals(EdenLayout.KeysFile, StringComparison.OrdinalIgnoreCase) || f.FileName.Equals(EdenLayout.TitleKeysFile, StringComparison.OrdinalIgnoreCase)).ToList();
            RetroService.Firmware? zip = files.FirstOrDefault(f => f.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            var ncas = files.Where(f => f.FileName.EndsWith(".nca", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!keys.Any(k => k.FileName.Equals(EdenLayout.KeysFile, StringComparison.OrdinalIgnoreCase)) || (zip == null && ncas.Count == 0))
                throw new RetroArchManager.MissingBiosException(Loc.F("{0} needs its keys (prod.keys) and its firmware (a zip of its .nca files): an administrator adds them in RomM, in the Firmware of the platform.", platform.Name));

            status(Loc.T("Downloading the Switch keys and firmware..."));
            Directory.CreateDirectory(Layout.KeysDirectory);
            foreach (RetroService.Firmware key in keys)
                await RetroService.DownloadFirmwareAsync(key, Path.Combine(Layout.KeysDirectory, key.FileName.ToLowerInvariant()));
            if (zip != null)
            {
                string file = Path.Combine(Layout.Directory, ".firmware.zip");
                await RetroService.DownloadFirmwareAsync(zip, file);
                try
                {
                    await Task.Run(() =>
                    {
                        using FileStream content = File.OpenRead(file);
                        Layout.InstallFirmware(content);
                    });
                }
                finally { File.Delete(file); }
            }
            else
            {
                if (Directory.Exists(Layout.FirmwareDirectory))
                    Directory.Delete(Layout.FirmwareDirectory, true);
                Directory.CreateDirectory(Layout.FirmwareDirectory);
                // A name from the network stays a name: nothing is written outside the folder
                foreach (RetroService.Firmware nca in ncas)
                    await RetroService.DownloadFirmwareAsync(nca, Path.Combine(Layout.FirmwareDirectory, Path.GetFileName(nca.FileName)));
            }
            if (!Layout.HasFirmware)
                throw new RetroArchManager.MissingBiosException(Loc.T("The Switch firmware in RomM has no .nca file: an administrator replaces it with a zip of the firmware's files."));
        }

        public override string? ControllerHint => File.Exists(Layout.SettingsFiles.Single()) && EdenLayout.HasController(File.ReadAllText(Layout.SettingsFiles.Single()))
            ? null
            : Loc.T("Controller: Eden plays with the keyboard until yours is chosen once in its window (Emulation > Configure > Controls > Input Device)");

        /// <summary>The player becomes the console's first user, the one the game is started as; the updates and DLC are the installed ones.</summary>
        public override Process Launch(string player, string launchFile, string language)
        {
            string config = Layout.SettingsFiles.Single();
            File.WriteAllText(config, EdenLayout.ContentFolders(File.ReadAllText(config),
                RetroService.SavedGames(downloading: false).Where(g => g.Game.Platform.System.IsEden).Select(g => g.Folder).ToList()));
            byte[]? existing = File.Exists(Layout.ProfilesFile) ? File.ReadAllBytes(Layout.ProfilesFile) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(Layout.ProfilesFile)!);
            File.WriteAllBytes(Layout.ProfilesFile, EdenLayout.Profiles(existing, player, LoginManager.Instance.GetCurrentUser()?.Username ?? "Player"));
            return base.Launch(player, launchFile, language);
        }
    }
}
