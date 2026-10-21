namespace GameVault.Core.Retro
{
    /// <summary>How a platform of RomM is played on this PC.</summary>
    /// <param name="Slug">RomM's platform slug.</param>
    /// <param name="Core">The RetroArch core.</param>
    /// <param name="Extensions">The files the emulator starts, best first: a game in several files is given its first match.</param>
    /// <param name="SystemFiles">The core's data files from libretro (assets/system/&lt;name&gt;.zip), unpacked in the system folder.</param>
    /// <param name="FirmwareFolder">Where the BIOS kept in RomM go, inside the system folder ("" for its root); null when none is needed.</param>
    public sealed record RetroSystem(string Slug, string Core, string[] Extensions, string? SystemFiles = null, string? FirmwareFolder = null);

    public static class RetroSystems
    {
        public static readonly IReadOnlyDictionary<string, RetroSystem> All = new[]
        {
            new RetroSystem("nes", "mesen", new[] { "nes", "fds", "unf", "zip" }),
            new RetroSystem("snes", "snes9x", new[] { "sfc", "smc", "fig", "swc", "bs", "zip" }),
            new RetroSystem("gba", "mgba", new[] { "gba", "gb", "gbc", "zip" }),
            new RetroSystem("nds", "melondsds", new[] { "nds", "dsi", "zip" }),
            // GameCube and Wii: Dolphin needs its own data folder (Sys)
            new RetroSystem("ngc", "dolphin", new[] { "rvz", "iso", "gcm", "gcz", "ciso", "wia", "m3u" }, SystemFiles: "Dolphin"),
            new RetroSystem("wii", "dolphin", new[] { "wbfs", "rvz", "iso", "wia", "ciso", "gcz", "wad" }, SystemFiles: "Dolphin"),
            new RetroSystem("psx", "swanstation", new[] { "m3u", "cue", "chd", "pbp", "iso", "img", "bin", "ecm", "mds" }, FirmwareFolder: ""),
            new RetroSystem("ps2", "pcsx2", new[] { "m3u", "chd", "iso", "cso", "zso", "cue", "bin", "img", "mdf", "nrg", "gz" }, SystemFiles: "LRPS2", FirmwareFolder: "pcsx2/bios"),
            // The game stays a zip: FinalBurn Neo reads MAME sets, with neogeo.zip as BIOS
            new RetroSystem("neogeoaes", "fbneo", new[] { "zip", "7z" }, FirmwareFolder: "fbneo"),
            // Azahar plays decrypted games only; a CIA is given as its main content (.cxi), see CiaReader
            new RetroSystem("3ds", "azahar", new[] { "cxi", "zcxi", "3ds", "cci", "zcci", "3dsx", "z3dsx", "app", "elf", "cia" }),
        }.ToDictionary(s => s.Slug);

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
