namespace GameVault.Core.Retro
{
    /// <summary>
    /// How a game feels, in RetroArch's config before each game (Settings > Retro): a game resumed where it was left,
    /// and the gamepad's lag removed on the consoles light enough for it.
    /// </summary>
    public static class RetroComfort
    {
        /// <summary>
        /// Quick resume: a state saved on quitting, loaded at the next start (a save state chosen on the game's page comes
        /// first). The cores whose states are small: they go with the cloud saves (a PS2's or a GameCube's are tens of MB).
        /// </summary>
        public static readonly string[] ResumeCores = { "mesen", "snes9x", "mgba", "fbneo", "melondsds", "swanstation" };

        /// <summary>
        /// Run-ahead: each frame computed one ahead, which hides a frame of the gamepad's lag. It runs the console twice:
        /// only the light ones.
        /// </summary>
        public static readonly string[] RunAheadCores = { "mesen", "snes9x", "mgba", "fbneo" };

        public static IEnumerable<(string Key, string Value)> Config(string core, bool resume, bool runAhead)
        {
            string resumes = resume && ResumeCores.Contains(core) ? "true" : "false";
            yield return ("savestate_auto_save", resumes);
            yield return ("savestate_auto_load", resumes);
            bool ahead = runAhead && RunAheadCores.Contains(core);
            yield return ("run_ahead_enabled", ahead ? "true" : "false");
            yield return ("run_ahead_frames", "1");
            // A second instance for the frame ahead: the sound does not crackle
            yield return ("run_ahead_secondary_instance", ahead ? "true" : "false");
            yield return ("run_ahead_hide_warnings", "true");
        }
    }
}
