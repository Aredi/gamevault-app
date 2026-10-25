namespace GameVault.Core.Retro
{
    /// <summary>How a platform of RomM is played on this PC.</summary>
    /// <param name="Slug">RomM's platform slug.</param>
    /// <param name="Name">Its name when RomM's is not known (offline).</param>
    /// <param name="Core">The RetroArch core.</param>
    /// <param name="Extensions">The files the emulator starts, best first: a game in several files is given its first match. A name with a dot is a whole file name ("EBOOT.BIN").</param>
    /// <param name="SystemFiles">The core's data files from libretro (assets/system/&lt;name&gt;.zip), unpacked in the system folder.</param>
    /// <param name="FirmwareFolder">Where the BIOS kept in RomM go, inside the system folder ("" for its root); null when none is needed.</param>
    /// <param name="CoverRatio">Height / width of the platform's box art (the median of RomM's covers, measured: IGDB's are 3:4): the cards take it, a cover of another shape is shown whole in it.</param>
    public sealed record RetroSystem(string Slug, string Name, string Core, string[] Extensions, string? SystemFiles = null, string? FirmwareFolder = null, double CoverRatio = 4 / 3.0)
    {
        /// <summary>Played with RPCS3, not RetroArch.</summary>
        public bool IsRpcs3 => Core == "rpcs3";
        /// <summary>Played with Eden, not RetroArch.</summary>
        public bool IsEden => Core == "eden";
        /// <summary>Played with xemu, not RetroArch.</summary>
        public bool IsXemu => Core == "xemu";
        public bool IsStandalone => IsRpcs3 || IsEden || IsXemu;
    }

    public static class RetroSystems
    {
        public static readonly IReadOnlyDictionary<string, RetroSystem> All = new[]
        {
            new RetroSystem("nes", "NES", "mesen", new[] { "nes", "fds", "unf", "zip" }),
            new RetroSystem("snes", "Super Nintendo", "snes9x", new[] { "sfc", "smc", "fig", "swc", "bs", "zip" }),
            new RetroSystem("gba", "Game Boy Advance", "mgba", new[] { "gba", "gb", "gbc", "zip" }, CoverRatio: 1.0),
            new RetroSystem("nds", "Nintendo DS", "melondsds", new[] { "nds", "dsi", "zip" }),
            // GameCube and Wii: Dolphin needs its own data folder (Sys)
            new RetroSystem("ngc", "GameCube", "dolphin", new[] { "rvz", "iso", "gcm", "gcz", "ciso", "wia", "m3u" }, SystemFiles: "Dolphin"),
            new RetroSystem("wii", "Wii", "dolphin", new[] { "wbfs", "rvz", "iso", "wia", "ciso", "gcz", "wad" }, SystemFiles: "Dolphin"),
            new RetroSystem("psx", "PlayStation", "swanstation", new[] { "m3u", "cue", "chd", "pbp", "iso", "img", "bin", "ecm", "mds" }, FirmwareFolder: ""),
            new RetroSystem("ps2", "PlayStation 2", "pcsx2", new[] { "m3u", "chd", "iso", "cso", "zso", "cue", "bin", "img", "mdf", "nrg", "gz" }, SystemFiles: "LRPS2", FirmwareFolder: "pcsx2/bios"),
            // The game stays a zip: FinalBurn Neo reads MAME sets, with neogeo.zip as BIOS
            new RetroSystem("neogeoaes", "Neo Geo", "fbneo", new[] { "zip", "7z" }, FirmwareFolder: "fbneo"),
            // Azahar plays decrypted games only; a CIA is given as its main content (.cxi), see CiaReader
            // RPCS3, not RetroArch: an ISO (encrypted ones too), or a game in a folder started by its EBOOT.BIN's folder
            new RetroSystem("ps3", "PlayStation 3", "rpcs3", new[] { "iso", "EBOOT.BIN" }),
            // Eden, not RetroArch: a game card (.xci) or an eShop package (.nsp), compressed ones (.nsz, .xcz) made .nsp at their download; its updates and DLC are read from the games' folders
            new RetroSystem("switch", "Nintendo Switch", "eden", new[] { "xci", "nsp" }),
            // xemu, not RetroArch: an XISO (a full dump is made one at its download, see XboxDisc)
            new RetroSystem("xbox", "Xbox", "xemu", new[] { "iso", "xiso" }),
            new RetroSystem("3ds", "Nintendo 3DS", "azahar", new[] { "cxi", "zcxi", "3ds", "cci", "zcci", "3dsx", "z3dsx", "app", "elf", "cia" }, CoverRatio: 0.89),
        // shortcut: the Mac plays RetroArch's consoles only, RPCS3, Eden and xemu are not set up there; add them if Mac players ask
        }.Where(s => !(OperatingSystem.IsMacOS() && s.IsStandalone)).ToDictionary(s => s.Slug);

        /// <summary>
        /// A game's cover as anyone reaches it (RomM serves its covers without a sign-in), for Discord's activity: the small
        /// one, Discord shows it at 128 px. Null without RomM's public address or a cover.
        /// </summary>
        public static string? PublicCoverUrl(string? rommUrl, string? cover) =>
            string.IsNullOrEmpty(rommUrl) || string.IsNullOrEmpty(cover) ? null
                : $"{rommUrl.TrimEnd('/')}/assets/romm/resources/{cover.Replace("/cover/big.", "/cover/small.")}";

        /// <summary>
        /// A Switch update or DLC: by its title id in the name ("[0100670014483001]": a game's own ends in 000), or in
        /// the "update" or "dlc" folder of a game in RomM.
        /// </summary>
        public static bool IsSwitchAddOn(string path) =>
            System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path), @"\[(0100[0-9A-Fa-f]{12})\]") is { Success: true } id && !id.Groups[1].Value.EndsWith("000")
            || Path.GetFileName(Path.GetDirectoryName(path)) is "update" or "dlc";

        /// <summary>The file to start in a game's folder; null when none has an extension the emulator knows.</summary>
        public static string? LaunchFile(RetroSystem system, IEnumerable<string> files)
        {
            var list = files.ToList();
            foreach (string extension in system.Extensions)
            {
                string? match = extension.Contains('.')
                    // The one nearest the top (a game's other EBOOT.BIN are deeper)
                    ? list.Where(f => string.Equals(Path.GetFileName(f), extension, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Length).FirstOrDefault()
                    // A Switch game before its updates and DLC, which are not started (alone, the add-on is the game's file: it is installed)
                    : list.Where(f => string.Equals(Path.GetExtension(f).TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => system.IsEden && IsSwitchAddOn(f)).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (match != null)
                    return match;
            }
            return null;
        }
    }
}
