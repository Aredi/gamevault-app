namespace GameVault.Core.Retro
{
    /// <summary>
    /// A recompilation ("recomp") an administrator gives a game: a program of its own, not an emulator, that reads
    /// the game's files. Its build for this system comes from the link given and is kept in its own folder with
    /// what it writes there (its saves, its settings, the game it built).
    /// </summary>
    /// <param name="executable">Its program in the build ("fefw-launcher.exe", "bin/game.exe"); an AppImage starts itself.</param>
    /// <param name="arguments">What it is started with; "{rom}" is the game's file and "{folder}" its folder. Empty: it asks for the game in its window.</param>
    public sealed class RecompLayout(string directory, string? windowsUrl, string? linuxUrl, string? macUrl, bool windows, bool mac, string? executable, string? arguments = null)
    {
        public string Directory => directory;

        private string? Url => (windows ? windowsUrl : mac ? macUrl : linuxUrl) is { } url && !string.IsNullOrWhiteSpace(url) ? url.Trim() : null;

        /// <summary>Its build is one file that starts itself, not an archive to unpack.</summary>
        public bool IsAppImage => Url is string url && new Uri(url).LocalPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);

        /// <summary>The program given, by this system's separators; null when none or when it would leave the build's folder.</summary>
        private string? Program
        {
            get
            {
                if (string.IsNullOrWhiteSpace(executable))
                    return IsAppImage ? Path.GetFileName(new Uri(Url!).LocalPath) : null;
                string[] parts = executable.Trim().Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 0 || parts.Any(p => p.Trim() is "." or "..") || Path.IsPathRooted(executable.Trim()) || executable.Contains(':') ? null : Path.Combine(parts);
            }
        }

        /// <summary>It has a build for this system and its program is known: else the game's console emulator plays it.</summary>
        public bool IsAvailable => Url != null && Program != null;

        public string PackageUrl => Url ?? throw new InvalidOperationException("No build of this program for this system");
        public string Executable => Path.Combine(directory, Program ?? throw new InvalidOperationException("The program of this build is not named"));
        /// <summary>The link it was installed from: another link given, the new build is unpacked over it.</summary>
        public string VersionFile => Path.Combine(directory, ".gamevault-build");
        public bool IsInstalled => IsAvailable && File.Exists(Executable) && File.Exists(VersionFile) && File.ReadAllText(VersionFile) == PackageUrl;

        /// <summary>Nothing tells it where the game is: the player chooses the game's files in its window.</summary>
        public bool AsksForTheGame => arguments == null || (!arguments.Contains("{rom}") && !arguments.Contains("{folder}"));

        /// <summary>What it is started with: the words of the arguments given (quotes keep spaces), the game's paths in their place.</summary>
        public IReadOnlyList<string> Arguments(string rom, string folder)
        {
            var words = new List<string>();
            var word = new System.Text.StringBuilder();
            bool quoted = false, any = false;
            foreach (char c in arguments ?? "")
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    any = true;
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (any)
                        words.Add(word.ToString());
                    word.Clear();
                    any = false;
                }
                else
                {
                    word.Append(c);
                    any = true;
                }
            }
            if (any)
                words.Add(word.ToString());
            // Replaced once split: a path with spaces stays one word
            return words.Select(w => w.Replace("{rom}", rom).Replace("{folder}", folder)).ToList();
        }

        /// <summary>The files of an unpacked build: in its one folder (a release's name), else where it was unpacked.</summary>
        public static string Root(string unpacked) =>
            System.IO.Directory.GetFiles(unpacked).Length == 0 && System.IO.Directory.GetDirectories(unpacked) is [string single] ? single : unpacked;

        /// <summary>A build's files moved over the ones installed; what the program wrote beside them stays (its saves, its settings).</summary>
        public static void Merge(string from, string to)
        {
            System.IO.Directory.CreateDirectory(to);
            foreach (string file in System.IO.Directory.GetFiles(from))
                File.Move(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            foreach (string folder in System.IO.Directory.GetDirectories(from))
                Merge(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
    }
}
