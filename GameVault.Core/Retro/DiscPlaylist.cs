namespace GameVault.Core.Retro
{
    /// <summary>
    /// A game on several discs (a PlayStation game's Disc 1, Disc 2): an .m3u lists them, so that the emulator starts the
    /// first and can change to the next one (RetroArch's disc control, the app's own player on Android).
    /// </summary>
    public static class DiscPlaylist
    {
        private static readonly string[] Images = { ".chd", ".pbp", ".iso", ".img", ".mds" };

        /// <summary>The consoles whose games come on several discs.</summary>
        public static bool AppliesTo(RetroSystem system) => system.Slug is "psx" or "ps2";

        /// <summary>
        /// The discs of a game's files, in their order (by name: "Disc 1" before "Disc 2"), or null for a game of one disc or
        /// one that has its playlist already. A disc in a .cue is the .cue (its tracks are read through it).
        /// </summary>
        public static IReadOnlyList<string>? Discs(IEnumerable<string> files)
        {
            var list = files.ToList();
            if (list.Any(f => Path.GetExtension(f).Equals(".m3u", StringComparison.OrdinalIgnoreCase)))
                return null;
            var cues = list.Where(f => Path.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToList();
            var discs = cues.Count > 0 ? cues : list.Where(f => Images.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            return discs.Count >= 2 ? discs.OrderBy(f => f, new NaturalOrder()).ToList() : null;
        }

        /// <summary>The playlist's lines: each disc relative to the playlist's folder, with forward slashes.</summary>
        public static string Content(string folder, IEnumerable<string> discs) =>
            string.Join("\n", discs.Select(d => Path.GetRelativePath(folder, d).Replace('\\', '/'))) + "\n";

        /// <summary>"Disc 2" before "Disc 10".</summary>
        private sealed class NaturalOrder : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                string a = x ?? "", b = y ?? "";
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        int byNumber = long.Parse(a[si..Math.Min(i, si + 18)]).CompareTo(long.Parse(b[sj..Math.Min(j, sj + 18)]));
                        if (byNumber != 0)
                            return byNumber;
                    }
                    else
                    {
                        int byChar = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                        if (byChar != 0)
                            return byChar;
                        i++;
                        j++;
                    }
                }
                return (a.Length - i).CompareTo(b.Length - j);
            }
        }
    }
}
