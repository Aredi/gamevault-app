namespace GameVault.Core.Retro
{
    /// <summary>
    /// The definition the 3D consoles are drawn at (Settings > Retro > Picture): a multiple of the console's own, written
    /// in each emulator's own setting (checked in their sources, at the versions the client takes).
    /// </summary>
    public static class RetroResolution
    {
        /// <summary>By the platform's core (RetroArch's, or "rpcs3", "eden"): the multiples offered, the console's own first.</summary>
        public static readonly IReadOnlyDictionary<string, int[]> Scales = new Dictionary<string, int[]>
        {
            ["dolphin"] = new[] { 1, 2, 3, 4 },
            // LRPS2 offers 1x, 2x and 4x
            ["pcsx2"] = new[] { 1, 2, 4 },
            ["azahar"] = new[] { 1, 2, 3, 4 },
            ["rpcs3"] = new[] { 1, 2, 3 },
            ["eden"] = new[] { 1, 2, 3 },
        };

        /// <summary>A RetroArch core's option for it; none for the cores that draw at the console's definition only.</summary>
        public static (string Key, string Value)? CoreOption(string core, int scale) => core switch
        {
            "dolphin" => ("dolphin_efb_scale", scale.ToString()),
            "pcsx2" => ("pcsx2_upscale_multiplier", scale >= 4 ? "4x" : scale >= 2 ? "2x" : "1x (Native)"),
            "azahar" => ("citra_resolution_factor", scale.ToString()),
            _ => null,
        };

        /// <summary>A core's options file as RetroArch writes it (key = "value"), with this value, the others kept.</summary>
        public static string MergeOptions(string existing, string key, string value)
        {
            var lines = existing.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
            string line = $"{key} = \"{value}\"";
            int at = lines.FindIndex(l => l.Split('=', 2)[0].Trim() == key);
            if (at >= 0)
                lines[at] = line;
            else
                lines.Add(line);
            return string.Join('\n', lines) + "\n";
        }

        /// <summary>The setting as kept in the profile: "dolphin=2;eden=1".</summary>
        public static Dictionary<string, int> Parse(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && Scales.TryGetValue(p[0], out int[]? scales) && int.TryParse(p[1], out int scale) && scales.Contains(scale))
            .ToDictionary(p => p[0], p => int.Parse(p[1]));

        public static string Format(IReadOnlyDictionary<string, int> scales) => string.Join(";", scales.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}"));
    }
}
