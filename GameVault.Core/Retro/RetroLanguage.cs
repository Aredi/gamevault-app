namespace GameVault.Core.Retro
{
    /// <summary>
    /// The language the consoles that have one start in (Settings > Retro, or a game's page): written in each emulator's
    /// own setting (checked in their sources, at the versions the client takes). A game without it keeps its own.
    /// </summary>
    public static class RetroLanguage
    {
        /// <summary>RomM's names of the languages, the ones every emulator here offers.</summary>
        public static readonly string[] All = { "English", "French", "German", "Spanish", "Italian", "Japanese" };

        /// <summary>The cores (RetroArch's, or "rpcs3", "eden") whose console has a language; the others' games have their own.</summary>
        public static readonly IReadOnlySet<string> Cores = new HashSet<string> { "azahar", "melondsds", "dolphin", "rpcs3", "eden" };

        /// <summary>A RetroArch core's option for it; none for RPCS3 and Eden (their own files) or a console without.</summary>
        public static (string Key, string Value)? CoreOption(string core, string language) => core switch
        {
            "azahar" => ("citra_language_value", language),
            "melondsds" => ("melonds_firmware_language", language switch { "French" => "fr", "German" => "de", "Spanish" => "es", "Italian" => "it", "Japanese" => "ja", _ => "en" }),
            // DiscIO::Language, for the GameCube and the Wii
            "dolphin" => ("dolphin_language", (language switch { "Japanese" => 0, "German" => 2, "French" => 3, "Spanish" => 4, "Italian" => 5, _ => 1 }).ToString()),
            _ => null,
        };

        /// <summary>
        /// The language a game starts in: the one chosen for it, else the setting's when the game has it (or RomM does not
        /// know its languages), else the first of its own the emulators offer.
        /// </summary>
        public static string Pick(string? chosen, string setting, IReadOnlyCollection<string> gameLanguages)
        {
            if (chosen != null)
                return chosen;
            if (gameLanguages.Count == 0 || gameLanguages.Contains(setting))
                return setting;
            return gameLanguages.FirstOrDefault(All.Contains) ?? setting;
        }

        /// <summary>The ones chosen for games, as kept in the profile: "559=French;12=Japanese".</summary>
        public static Dictionary<int, string> Parse(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _) && All.Contains(p[1]))
            .GroupBy(p => int.Parse(p[0]))
            .ToDictionary(g => g.Key, g => g.Last()[1]);

        public static string Format(IReadOnlyDictionary<int, string> games) => string.Join(";", games.OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Value}"));
    }
}
