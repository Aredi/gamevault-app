using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// The emulators an administrator gives some retro games instead of their console's (a fork of Eglantine that plays one
    /// game better): each with its builds' links, by system, and the RomM games it plays. One JSON file for all, written whole.
    /// </summary>
    public sealed partial class GameEmulatorStore
    {
        private readonly string file;
        private readonly object writing = new();

        public GameEmulatorStore(UploaderOptions options, ILogger<GameEmulatorStore> logger)
        {
            file = Path.Combine(options.EmulatorsDirectory, "emulators.json");
            Enabled = ProfileStore.IsWritable(options.EmulatorsDirectory, out string error);
            if (!Enabled)
                logger.LogWarning("Emulators by game are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", options.EmulatorsDirectory, error);
        }

        public bool Enabled { get; }

        /// <summary>A build per system, null where it has none: the console's emulator plays there.</summary>
        /// <param name="Executable">Its program in a Windows or Mac build ("lichii.exe", "bin/lichii.exe"), null when it is named as the console emulator's.</param>
        /// <param name="Kind">"recomp": a program of its own that reads the game's files (any console), started instead of an emulator; else a fork.</param>
        /// <param name="Arguments">A recomp's start arguments; "{rom}" is the game's file and "{folder}" its folder.</param>
        public sealed record Emulator(string Id, string Name, string Console, string? Windows, string? Linux, string? Mac, string? Executable = null, string? Kind = null, string? Arguments = null)
        {
            [System.Text.Json.Serialization.JsonIgnore] public bool IsRecomp => Kind == Recomp;
        }

        public const string Recomp = "recomp";

        /// <summary>The emulators, and each RomM game's one by its id.</summary>
        public sealed record Catalog(List<Emulator> Emulators, Dictionary<int, string> Games);

        /// <summary>The consoles whose emulator a fork can replace: the ones of Eden and Eglantine, started alike.</summary>
        public static readonly string[] Consoles = { "switch", "switch-2" };

        [GeneratedRegex("^[a-z0-9-]{1,40}$")]
        public static partial Regex IdPattern();

        /// <summary>A path inside the build: names of letters, digits, spaces and ._()+-, by / or \ (never "..": the clients start it).</summary>
        [GeneratedRegex(@"^[A-Za-z0-9 ._()+-]+([/\\][A-Za-z0-9 ._()+-]+)*$")]
        public static partial Regex ExecutablePattern();

        public Catalog Get()
        {
            lock (writing)
                return File.Exists(file) ? JsonSerializer.Deserialize<Catalog>(File.ReadAllText(file)) ?? Empty() : Empty();
        }

        private static Catalog Empty() => new(new(), new());

        /// <summary>Why it can not be kept, or null.</summary>
        public static string? Invalid(Emulator emulator)
        {
            if (!IdPattern().IsMatch(emulator.Id))
                return "The emulator's id is letters, digits and dashes.";
            if (string.IsNullOrWhiteSpace(emulator.Name) || emulator.Name.Length > 80)
                return "The emulator needs a name (80 characters at most).";
            if (!string.IsNullOrEmpty(emulator.Kind) && emulator.Kind != "emulator" && emulator.Kind != Recomp)
                return "An emulator's kind is \"emulator\" or \"recomp\".";
            if (emulator.IsRecomp ? !IdPattern().IsMatch(emulator.Console ?? "") : !Consoles.Contains(emulator.Console))
                return emulator.IsRecomp ? "The recomp needs the console of its game." : "Only an emulator of the Switch or the Switch 2 can be given to a game.";
            string?[] links = { emulator.Windows, emulator.Linux, emulator.Mac };
            if (links.All(string.IsNullOrWhiteSpace))
                return "The emulator needs a build's link for one system at least.";
            // Downloaded and started by the players' clients: from a secure address only
            if (links.Any(l => !string.IsNullOrWhiteSpace(l) && (l.Length > 1000 || !Uri.TryCreate(l, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)))
                return "A build's link is an https address.";
            if (!string.IsNullOrWhiteSpace(emulator.Executable))
            {
                string program = emulator.Executable.Trim();
                if (program.Length > 200 || !ExecutablePattern().IsMatch(program) || program.Split('/', '\\').Any(part => part.Trim() is "." or ".."))
                    return "The emulator's program is a file of its build: \"lichii.exe\" or \"bin/lichii.exe\".";
            }
            if (emulator.IsRecomp)
            {
                // Nothing names its program for it, but an AppImage, which starts itself
                bool appImages = links.Where(l => !string.IsNullOrWhiteSpace(l)).All(l => new Uri(l!).LocalPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(emulator.Executable) && !appImages)
                    return "The recomp needs its program's name: \"game-launcher.exe\".";
                if (emulator.Arguments is { } arguments && (arguments.Length > 300 || arguments.Any(char.IsControl)))
                    return "The recomp's arguments are one line of 300 characters at most.";
            }
            else if (!string.IsNullOrWhiteSpace(emulator.Arguments))
                return "Only a recomp has arguments.";
            return null;
        }

        public void Set(Emulator emulator)
        {
            static string? Link(string? link) => string.IsNullOrWhiteSpace(link) ? null : link.Trim();
            lock (writing)
            {
                Catalog catalog = Get();
                catalog.Emulators.RemoveAll(e => e.Id == emulator.Id);
                catalog.Emulators.Add(emulator with { Name = emulator.Name.Trim(), Windows = Link(emulator.Windows), Linux = Link(emulator.Linux), Mac = Link(emulator.Mac), Executable = Link(emulator.Executable), Kind = emulator.IsRecomp ? Recomp : null, Arguments = emulator.IsRecomp ? Link(emulator.Arguments) : null });
                Write(catalog);
            }
        }

        /// <summary>Gone with it: its games are played by their console's emulator again.</summary>
        public bool Delete(string id)
        {
            lock (writing)
            {
                Catalog catalog = Get();
                if (catalog.Emulators.RemoveAll(e => e.Id == id) == 0)
                    return false;
                foreach (int game in catalog.Games.Where(g => g.Value == id).Select(g => g.Key).ToList())
                    catalog.Games.Remove(game);
                Write(catalog);
                return true;
            }
        }

        /// <summary>The game's emulator (null: its console's); false when there is no such emulator.</summary>
        public bool SetGame(int gameId, string? emulatorId)
        {
            lock (writing)
            {
                Catalog catalog = Get();
                if (emulatorId != null && catalog.Emulators.All(e => e.Id != emulatorId))
                    return false;
                if (emulatorId == null)
                    catalog.Games.Remove(gameId);
                else
                    catalog.Games[gameId] = emulatorId;
                Write(catalog);
                return true;
            }
        }

        private void Write(Catalog catalog)
        {
            // Written aside then moved: never half written
            string part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(catalog));
            File.Move(part, file, overwrite: true);
        }
    }
}
