namespace GameVault.Core.Retro
{
    /// <summary>
    /// The controller a console's core is given on its first port (RetroArch's "Device Type"), for the consoles with
    /// several (Settings > Retro, or a game's page): RetroArch's ids, checked in the cores' sources.
    /// </summary>
    public static class RetroControllers
    {
        /// <summary>A controller of the console: its name (translated where it is shown) and RetroArch's id.</summary>
        public sealed record Controller(string Name, int Device);

        /// <summary>By the platform's slug (Dolphin plays the GameCube too, with one controller), the default first.</summary>
        public static readonly IReadOnlyDictionary<string, Controller[]> BySystem = new Dictionary<string, Controller[]>
        {
            // RETRO_DEVICE_SUBCLASS(JOYPAD, n): (n + 1) << 8 | 1. The Nunchuk is what most Wii games ask for
            ["wii"] = new Controller[] { new("Wiimote + Nunchuk", 769), new("Wiimote", 1), new("Sideways Wiimote", 513), new("Classic Controller", 1025), new("GameCube Controller", 1537) },
            // SwanStation's DualShock starts digital (the games that want analog switch it), so it plays both kinds of games
            ["psx"] = new Controller[] { new("DualShock (analog)", 261), new("Digital controller", 1) },
        };

        /// <summary>The controller a game starts with: the one chosen for it, else its console's, else the default; null when the console has one only.</summary>
        public static int? Pick(string slug, int? chosen, int? console)
        {
            if (!BySystem.TryGetValue(slug, out Controller[]? controllers))
                return null;
            return new[] { chosen, console }.FirstOrDefault(d => d is int id && controllers.Any(c => c.Device == id)) ?? controllers[0].Device;
        }

        private const string RemapMark = "# SanctuaryVault";

        /// <summary>
        /// The game's remap file with the controller (RetroArch reads the controller type in remap files only): the one it
        /// would read without it (the folder's or the core's, the player's controls) with the controller. Null keeps the
        /// game's own, saved by the player in RetroArch's menu.
        /// </summary>
        /// <param name="game">The game's remap file, if there is one.</param>
        /// <param name="otherwise">The one RetroArch reads without it, if there is one.</param>
        public static string? Remap(string? game, string? otherwise, int device)
        {
            if (game != null && !game.StartsWith(RemapMark, StringComparison.Ordinal))
                return null;
            return $"{RemapMark}: the controller chosen in the client; a remap saved in RetroArch's menu for this game replaces this file\n"
                + RetroResolution.MergeOptions(otherwise ?? "", "input_libretro_device_p1", device.ToString());
        }

        /// <summary>The choices as kept in the profile: "wii=769;psx=1" (by console) or "559=1025" (by RomM game id).</summary>
        public static Dictionary<string, int> Parse(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0].Length > 0 && int.TryParse(p[1], out _))
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => int.Parse(g.Last()[1]));

        public static string Format(IReadOnlyDictionary<string, int> choices) => string.Join(";", choices.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}={c.Value}"));
    }
}
