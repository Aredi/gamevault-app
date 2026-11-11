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
