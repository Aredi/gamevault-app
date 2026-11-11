using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameVault.Core.Couch
{
    /// <summary>A program started from the living-room mode's home (a video player, another launcher).</summary>
    public sealed record CouchApp(string Name, string Path, string? Arguments = null);

    /// <summary>How the living-room mode looks and behaves, kept in "couch.json" beside the profile's settings.</summary>
    public sealed class CouchSettings
    {
        public const string Xbox = "xbox", PlayStation = "playstation", Switch = "switch", Auto = "auto";
        public const string ByName = "name", ByRecent = "recent", ByTime = "time";

        /// <summary>The gamepad whose buttons the hints name; <see cref="Auto"/>: the one the player uses.</summary>
        public string Buttons { get; set; } = Auto;
        /// <summary>The settings' version: 2 since the buttons follow the gamepad by default.</summary>
        public int Version { get; set; }
        /// <summary>The accent color follows the selected game's cover.</summary>
        public bool DynamicColors { get; set; } = true;
        /// <summary>The accent color chosen ("#RRGGBB"); empty: the theme's.</summary>
        public string Accent { get; set; } = "";
        /// <summary>The selected game's numbers beside its name.</summary>
        public bool ShowStats { get; set; } = true;
        /// <summary>No fades, no screenshots behind a game's screen: for a small PC.</summary>
        public bool Performance { get; set; }
        /// <summary>The games shown one after the other when nothing is touched for this long; 0: never.</summary>
        public int ScreenSaverMinutes { get; set; } = 5;
        /// <summary>The right stick moves the mouse, its click clicks (Windows).</summary>
        public bool Mouse { get; set; }
        /// <summary>Start + Back while a game runs shows the menu over it (Windows).</summary>
        public bool Overlay { get; set; } = true;
        /// <summary>The selected game's logo instead of its name, when the server has one.</summary>
        public bool Logos { get; set; } = true;
        /// <summary>The picture of a game while it starts.</summary>
        public bool Splash { get; set; } = true;
        /// <summary>A game's trailer plays in its screen after a moment.</summary>
        public bool Trailers { get; set; } = true;
        /// <summary>The quiet music behind the menus.</summary>
        public bool Music { get; set; } = true;

        // --- The customization, as Aniki ReMake's: each one has its few values, the first the usual one ---

        /// <summary>The values each customization takes, the usual one first. A value read that is not among them is the usual one.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Choices = new Dictionary<string, string[]>
        {
            [nameof(Layout)] = new[] { "signature", "panel", "showcase" },
            [nameof(Background)] = new[] { "game", "aurora", "night", "synthwave", "accent", "none" },
            [nameof(Font)] = new[] { "sora", "figtree", "system" },
            [nameof(Corners)] = new[] { "round", "square", "soft" },
            [nameof(InfoStyle)] = new[] { "panel", "compact" },
            [nameof(Shade)] = new[] { "normal", "light", "strong" },
            [nameof(CoverShape)] = new[] { "portrait", "square" },
            [nameof(CoverSize)] = new[] { "normal", "small", "large" },
            [nameof(SoundPack)] = new[] { "soft", "retro", "crystal" },
            [nameof(MusicTheme)] = new[] { "calm", "night" },
            [nameof(StartTab)] = new[] { "home", "library", "installed" },
            [nameof(LoginBackground)] = new[] { "game", "aurora", "night", "synthwave", "accent" },
        };

        /// <summary>A game's screen: on its picture at the left, in a panel, or centered at the bottom.</summary>
        public string Layout { get; set; } = "signature";
        /// <summary>Behind the lists: the selected game's picture, a still background made here, or nothing.</summary>
        public string Background { get; set; } = "game";
        /// <summary>A game's screen shows its own picture, whatever is behind the lists.</summary>
        public bool DetailsBackground { get; set; } = true;
        public string Font { get; set; } = "sora";
        public string Corners { get; set; } = "round";
        /// <summary>The selected game's numbers: docked at the screen's edge, or on a line under its name.</summary>
        public string InfoStyle { get; set; } = "panel";
        /// <summary>How dark the screen is under the texts.</summary>
        public string Shade { get; set; } = "normal";
        public string CoverShape { get; set; } = "portrait";
        public string CoverSize { get; set; } = "normal";
        /// <summary>The chosen cover glows.</summary>
        public bool Glow { get; set; } = true;
        /// <summary>The picture behind drifts slowly.</summary>
        public bool Drift { get; set; } = true;
        /// <summary>A game's screenshots come one after the other behind its screen.</summary>
        public bool Shots { get; set; } = true;
        /// <summary>A game's screen and the menus come in with a short move.</summary>
        public bool Animations { get; set; } = true;
        /// <summary>What the buttons do, at the bottom of the screen.</summary>
        public bool Hints { get; set; } = true;
        public bool Clock { get; set; } = true;
        public string SoundPack { get; set; } = "soft";
        /// <summary>The sounds' loudness, in percent (25 to 100).</summary>
        public int SoundVolume { get; set; } = 100;
        public string MusicTheme { get; set; } = "calm";
        /// <summary>The music's loudness, in percent (25 to 100).</summary>
        public int MusicVolume { get; set; } = 75;
        /// <summary>The login screen when the mode opens with the client.</summary>
        public bool Welcome { get; set; } = true;
        /// <summary>Behind the login screen: the game played last, or a background made here.</summary>
        public string LoginBackground { get; set; } = "game";
        /// <summary>The tab shown when the mode opens.</summary>
        public string StartTab { get; set; } = "home";

        /// <summary>A customization's value, by its name (the menus go through them).</summary>
        public string Get(string name) => (string)typeof(CouchSettings).GetProperty(name)!.GetValue(this)!;

        public void Set(string name, string value) => typeof(CouchSettings).GetProperty(name)!.SetValue(this, value);

        /// <summary>Every customization among its values, the volumes among theirs.</summary>
        public void Check()
        {
            foreach (var (name, values) in Choices)
                if (Array.IndexOf(values, Get(name)) < 0)
                    Set(name, values[0]);
            SoundVolume = Volumes.Contains(SoundVolume) ? SoundVolume : 100;
            MusicVolume = Volumes.Contains(MusicVolume) ? MusicVolume : 75;
        }

        public static readonly int[] Volumes = { 25, 50, 75, 100 };

        public string Sort { get; set; } = ByName;
        public bool InstalledOnly { get; set; }
        public bool NeverPlayed { get; set; }
        /// <summary>The only genre shown; null: all.</summary>
        public string? Genre { get; set; }

        public List<CouchApp> Apps { get; set; } = new();

        [JsonIgnore] public bool IsFiltered => InstalledOnly || NeverPlayed || Genre != null || Sort != ByName;

        public static readonly string[] Accents = { "", "#7C6BFF", "#2F9BFF", "#21B8A6", "#3FBF5F", "#F2A93B", "#F0563F", "#E0559B" };

        public static CouchSettings Load(string file)
        {
            try
            {
                if (File.Exists(file) && JsonSerializer.Deserialize<CouchSettings>(File.ReadAllText(file)) is CouchSettings read)
                {
                    // Before the buttons followed the gamepad, "xbox" was kept without being chosen
                    if (read.Version < 2 && read.Buttons == Xbox)
                        read.Buttons = Auto;
                    if (read.Buttons is not (Xbox or PlayStation or Switch or Auto))
                        read.Buttons = Auto;
                    read.Version = 2;
                    if (read.Sort is not (ByName or ByRecent or ByTime))
                        read.Sort = ByName;
                    read.Apps ??= new();
                    read.ScreenSaverMinutes = Math.Clamp(read.ScreenSaverMinutes, 0, 60);
                    read.Check();
                    return read;
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return new CouchSettings();
        }

        public void Save(string file)
        {
            try
            {
                // Written aside then moved: never half written
                File.WriteAllText(file + ".part", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(file + ".part", file, true);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The next value of a list after this one, back to the first after the last.</summary>
        public static T Next<T>(IReadOnlyList<T> values, T current)
        {
            int index = -1;
            for (int i = 0; i < values.Count; i++)
                if (EqualityComparer<T>.Default.Equals(values[i], current))
                    index = i;
            return values[(index + 1) % values.Count];
        }
    }

    /// <summary>What a button is called on each gamepad: the hints say "A" on an Xbox one, the cross on a PlayStation one.</summary>
    public static class CouchGlyphs
    {
        /// <summary>The buttons to name: the ones chosen, else the gamepad that moved last (an Xbox one before any).</summary>
        public static string Resolve(string chosen, string? used) => chosen == CouchSettings.Auto ? used ?? CouchSettings.Xbox : chosen;

        /// <summary>
        /// The kind of a gamepad SDL reads, by the type SDL gives (SDL 2.0.12 and later) or else by its name: the
        /// PlayStation and Nintendo ones; any other is named as an Xbox one.
        /// </summary>
        public static string KindOf(int sdlType, string? name)
        {
            switch (sdlType)
            {
                case 3 or 4 or 7: return CouchSettings.PlayStation;
                case 5 or 11 or 12 or 13: return CouchSettings.Switch;
                case 1 or 2: return CouchSettings.Xbox;
            }
            string n = name ?? "";
            bool Has(string part) => n.Contains(part, StringComparison.OrdinalIgnoreCase);
            if (Has("PS3") || Has("PS4") || Has("PS5") || Has("DualShock") || Has("DualSense") || Has("PlayStation") || Has("Sony"))
                return CouchSettings.PlayStation;
            if (Has("Nintendo") || Has("Switch") || Has("Joy-Con") || Has("Pro Controller"))
                return CouchSettings.Switch;
            return CouchSettings.Xbox;
        }

        /// <param name="xbox">The button as an Xbox gamepad names it: "A", "LB RB", "START", "BACK", "L3".</param>
        public static string Label(string style, string xbox) => string.Join(" ", xbox.Split(' ').Select(button => style switch
        {
            CouchSettings.PlayStation => button switch
            {
                "A" => "✕", "B" => "○", "X" => "□", "Y" => "△", "LB" => "L1", "RB" => "R1", "LT" => "L2", "RT" => "R2",
                "START" => "OPTIONS", "BACK" => "SHARE", _ => button,
            },
            // The same places, with the names printed on the gamepad
            CouchSettings.Switch => button switch
            {
                "A" => "B", "B" => "A", "X" => "Y", "Y" => "X", "LB" => "L", "RB" => "R", "LT" => "ZL", "RT" => "ZR",
                "START" => "+", "BACK" => "−", _ => button,
            },
            _ => button,
        }));
    }

    /// <summary>A game as the order and the filters of the living-room mode see it.</summary>
    public sealed record CouchEntry(string Title, bool Installed, int Minutes, DateTime? LastPlayed, IReadOnlyList<string> Genres);

    public static class CouchFilter
    {
        /// <summary>The games to show, by their place in <paramref name="entries"/> (given by name), filtered and in the order chosen.</summary>
        public static List<int> Apply(IReadOnlyList<CouchEntry> entries, CouchSettings settings)
        {
            IEnumerable<int> shown = Enumerable.Range(0, entries.Count)
                .Where(i => !settings.InstalledOnly || entries[i].Installed)
                .Where(i => !settings.NeverPlayed || entries[i].Minutes == 0)
                .Where(i => settings.Genre == null || entries[i].Genres.Contains(settings.Genre, StringComparer.OrdinalIgnoreCase));
            return settings.Sort switch
            {
                // The games never played keep their order by name, after the others
                CouchSettings.ByRecent => shown.OrderByDescending(i => entries[i].LastPlayed ?? DateTime.MinValue).ToList(),
                CouchSettings.ByTime => shown.OrderByDescending(i => entries[i].Minutes).ToList(),
                _ => shown.ToList(),
            };
        }

        /// <summary>The genres of these games, by name.</summary>
        public static List<string> Genres(IEnumerable<CouchEntry> entries) =>
            entries.SelectMany(e => e.Genres).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
