namespace GameVault.Core.Library
{
    /// <summary>
    /// A game archive holding a disc image (an .iso) with the setup inside (Queens Domain: a .rar with an .iso with
    /// setup.exe): the server can not see the setup, so it calls the game undetectable, and nothing is found to install
    /// once the archive is extracted. The images are opened in turn, and their setup recognized.
    /// </summary>
    public static class DiscImages
    {
        private static readonly string[] Programs = { ".exe", ".msi", ".bat", ".cmd" };

        private static bool IsProgram(string path) => Programs.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        /// <summary>The disc images to open: the files are .iso ones and no program is beside them; else none.</summary>
        public static IReadOnlyList<string> ToOpen(IEnumerable<string> files)
        {
            var all = files.ToList();
            if (all.Any(IsProgram))
                return Array.Empty<string>();
            return all.Where(f => string.Equals(Path.GetExtension(f), ".iso", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>The installer among the files: "setup.exe" first, then "setup…", "install…" or an .msi; null when none looks like one.</summary>
        public static string? Setup(IEnumerable<string> files)
        {
            int Rank(string path)
            {
                string name = Path.GetFileName(path).ToLowerInvariant();
                string extension = Path.GetExtension(name);
                if (name == "setup.exe")
                    return 0;
                if (extension == ".exe" && (name.StartsWith("setup") || name.StartsWith("install")))
                    return 1;
                if (extension == ".msi")
                    return 2;
                return int.MaxValue;
            }
            // The shallowest one: the disc's own, not a redistributable's in a sub folder
            return files.Select(f => (File: f, Rank: Rank(f), Depth: f.Count(c => c is '/' or '\\')))
                .Where(f => f.Rank != int.MaxValue)
                .OrderBy(f => f.Rank).ThenBy(f => f.Depth)
                .Select(f => f.File).FirstOrDefault();
        }
    }
}
