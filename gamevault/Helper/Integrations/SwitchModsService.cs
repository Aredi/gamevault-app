using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Localization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper.Integrations
{
    /// <summary>
    /// The mods of the Switch games installed here, for the emulator that plays them (Eden, its forks): found on
    /// GameBanana, the file for the game's version, opened and put where the emulator reads them.
    /// </summary>
    internal static class SwitchModsService
    {
        /// <summary>A game's mods changed (one put, turned on or off, taken away).</summary>
        public static event Action<RetroGame>? Changed;

        private static readonly ConcurrentDictionary<string, string?> ids = new();

        public static SwitchEmulator? EmulatorOf(RetroGame game) => StandaloneEmulator.Of(game) as SwitchEmulator;

        private static IEnumerable<string> FilesOf(RetroGame game) => RetroService.InstalledFolder(game) is string folder
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();

        /// <summary>The game's id ("0100F2C0115B6000"), its mods' folder's name; null when it can not be read (no console keys yet).</summary>
        public static string? TitleIdOf(RetroGame game)
        {
            if (EmulatorOf(game) is not SwitchEmulator emulator || game.RomFile is not string rom)
                return null;
            if (ids.TryGetValue(rom, out string? known) && known != null)
                return known;
            string? id = FilesOf(game).Select(SwitchMods.TitleIdFromName).FirstOrDefault(i => i != null)
                ?? SwitchMods.TitleIdOf(rom, SwitchMods.HeaderKey(Path.Combine(emulator.Layout.KeysDirectory, SwitchLayout.KeysFile)));
            ids[rom] = id;
            return id;
        }

        /// <summary>"1.2.1": the version of the game's update here, from its file's name; null when none says it.</summary>
        public static string? VersionOf(RetroGame game) => SwitchMods.GameVersion(FilesOf(game));

        public static List<SwitchMods.InstalledMod> Installed(RetroGame game) =>
            EmulatorOf(game) is SwitchEmulator emulator && TitleIdOf(game) is string id ? SwitchMods.Installed(emulator.Layout, id) : new();

        /// <summary>Downloads the file, opens it beside the emulator's folder and puts its mods, turned on.</summary>
        public static async Task InstallAsync(RetroGame game, GameBananaModPage mod, GameBananaFile file, IProgress<double>? progress, CancellationToken ct)
        {
            if (EmulatorOf(game) is not SwitchEmulator emulator)
                throw new InvalidOperationException(Loc.T("Mods are for the Switch games played with Eden."));
            string id = TitleIdOf(game) ?? throw new InvalidOperationException(Loc.T("The game could not be identified: play it once, so the console's keys are here, then try again."));
            // On the same drive as the emulator's folder: the mod's folders are moved there, not copied
            string work = Path.Combine(emulator.Layout.UserDirectory, "load-tmp", Guid.NewGuid().ToString("N"));
            try
            {
                string archive = await GameBanana.DownloadAsync(file, Path.Combine(work, "file"), new Progress<double>(p => progress?.Report(p * 0.8)), ct);
                string opened = Path.Combine(work, "opened");
                await OpenAsync(archive, opened);
                progress?.Report(0.95);
                string? version = VersionOf(game);
                List<string> put = await Task.Run(() => SwitchMods.Install(emulator.Layout, id, opened, mod.Name, mod.Id, file.Id, version), ct);
                if (put.Count == 0)
                    throw new InvalidDataException(Loc.T("This mod can not be installed for Eden: it needs a tool of its own, or is not made for an emulator."));
                progress?.Report(1);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(work))
                        Directory.Delete(work, true);
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            Changed?.Invoke(game);
        }

        private static async Task OpenAsync(string archive, string folder)
        {
            Directory.CreateDirectory(folder);
            if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // .NET writes nothing outside the folder (an entry "../x" is refused)
                await Task.Run(() => ZipFile.ExtractToDirectory(archive, folder));
                return;
            }
            var sevenZip = new SevenZipHelper();
            int code = await sevenZip.ExtractArchive(archive, folder);
            if (code != 0)
                throw new InvalidDataException(Loc.F("The mod's archive could not be opened ({0}).", sevenZip.LastError.Trim().Split('\n').FirstOrDefault()?.Trim() ?? code.ToString()));
        }

        public static void SetEnabled(RetroGame game, string folder, bool enabled)
        {
            if (EmulatorOf(game) is SwitchEmulator emulator && TitleIdOf(game) is string id)
            {
                SwitchMods.SetEnabled(emulator.Layout, id, folder, enabled);
                Changed?.Invoke(game);
            }
        }

        public static void Remove(RetroGame game, string folder)
        {
            if (EmulatorOf(game) is SwitchEmulator emulator && TitleIdOf(game) is string id)
            {
                SwitchMods.Remove(emulator.Layout, id, folder);
                Changed?.Invoke(game);
            }
        }
    }
}
