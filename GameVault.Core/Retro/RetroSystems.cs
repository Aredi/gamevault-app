namespace GameVault.Core.Retro
{
    /// <summary>How a platform of RomM is played on this PC.</summary>
    /// <param name="Slug">RomM's platform slug.</param>
    /// <param name="Name">Its name when RomM's is not known (offline).</param>
    /// <param name="Core">The RetroArch core.</param>
    /// <param name="Extensions">The files the emulator starts, best first: a game in several files is given its first match.</param>
    /// <param name="SystemFiles">The core's data files from libretro (assets/system/&lt;name&gt;.zip), unpacked in the system folder.</param>
    /// <param name="FirmwareFolder">Where the BIOS kept in RomM go, inside the system folder ("" for its root); null when none is needed.</param>
    /// <param name="CoverRatio">Height / width of the platform's box art (the median of RomM's covers): the cards take it.</param>
    public sealed record RetroSystem(string Slug, string Name, string Core, string[] Extensions, string? SystemFiles = null, string? FirmwareFolder = null, double CoverRatio = 1.4);

    public static class RetroSystems
    {
        public static readonly IReadOnlyDictionary<string, RetroSystem> All = new[]
        {
            new RetroSystem("nes", "NES", "mesen", new[] { "nes", "fds", "unf", "zip" }, CoverRatio: 1.41),
            new RetroSystem("snes", "Super Nintendo", "snes9x", new[] { "sfc", "smc", "fig", "swc", "bs", "zip" }, CoverRatio: 1.5),
            new RetroSystem("gba", "Game Boy Advance", "mgba", new[] { "gba", "gb", "gbc", "zip" }, CoverRatio: 1.0),
            new RetroSystem("nds", "Nintendo DS", "melondsds", new[] { "nds", "dsi", "zip" }, CoverRatio: 1.5),
            // GameCube and Wii: Dolphin needs its own data folder (Sys)
            new RetroSystem("ngc", "GameCube", "dolphin", new[] { "rvz", "iso", "gcm", "gcz", "ciso", "wia", "m3u" }, SystemFiles: "Dolphin", CoverRatio: 1.41),
            new RetroSystem("wii", "Wii", "dolphin", new[] { "wbfs", "rvz", "iso", "wia", "ciso", "gcz", "wad" }, SystemFiles: "Dolphin", CoverRatio: 1.4),
            new RetroSystem("psx", "PlayStation", "swanstation", new[] { "m3u", "cue", "chd", "pbp", "iso", "img", "bin", "ecm", "mds" }, FirmwareFolder: "", CoverRatio: 1.33),
            new RetroSystem("ps2", "PlayStation 2", "pcsx2", new[] { "m3u", "chd", "iso", "cso", "zso", "cue", "bin", "img", "mdf", "nrg", "gz" }, SystemFiles: "LRPS2", FirmwareFolder: "pcsx2/bios", CoverRatio: 1.5),
            // The game stays a zip: FinalBurn Neo reads MAME sets, with neogeo.zip as BIOS
            new RetroSystem("neogeoaes", "Neo Geo", "fbneo", new[] { "zip", "7z" }, FirmwareFolder: "fbneo", CoverRatio: 1.5),
            // Azahar plays decrypted games only; a CIA is given as its main content (.cxi), see CiaReader
            new RetroSystem("3ds", "Nintendo 3DS", "azahar", new[] { "cxi", "zcxi", "3ds", "cci", "zcci", "3dsx", "z3dsx", "app", "elf", "cia" }, CoverRatio: 0.89),
        }.ToDictionary(s => s.Slug);

        /// <summary>
        /// A game's cover as anyone reaches it (RomM serves its covers without a sign-in), for Discord's activity: the small
        /// one, Discord shows it at 128 px. Null without RomM's public address or a cover.
        /// </summary>
        public static string? PublicCoverUrl(string? rommUrl, string? cover) =>
            string.IsNullOrEmpty(rommUrl) || string.IsNullOrEmpty(cover) ? null
                : $"{rommUrl.TrimEnd('/')}/assets/romm/resources/{cover.Replace("/cover/big.", "/cover/small.")}";

        /// <summary>The file to start in a game's folder; null when none has an extension the emulator knows.</summary>
        public static string? LaunchFile(RetroSystem system, IEnumerable<string> files)
        {
            var list = files.ToList();
            foreach (string extension in system.Extensions)
            {
                string? match = list.Where(f => string.Equals(Path.GetExtension(f).TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (match != null)
                    return match;
            }
            return null;
        }
    }
}
