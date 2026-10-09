using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameVault.Core.Library
{
    /// <summary>
    /// What a player chose for their profile, like the modules of a Steam profile: a tagline, a color and the
    /// sections shown in their order. Kept by the SanctuaryVault service; anything read from it is checked here.
    /// </summary>
    public sealed class ProfileDocument
    {
        public const int MaxModules = 10;
        public const int MaxTagline = 80;
        public const int MaxTitle = 60;
        public const int MaxText = 1500;

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("tagline")]
        public string? Tagline { get; set; }

        /// <summary>"#RRGGBB", or null for the theme's color.</summary>
        [JsonPropertyName("accent")]
        public string? Accent { get; set; }

        [JsonPropertyName("modules")]
        public List<ProfileModule> Modules { get; set; } = new();

        /// <summary>A profile nobody customized: the figures and the games played lately.</summary>
        public static ProfileDocument Default() => new()
        {
            Modules = { new ProfileModule { Type = ProfileModuleType.Stats }, new ProfileModule { Type = ProfileModuleType.Recent } },
        };

        private static readonly JsonSerializerOptions json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        public string ToJson() => JsonSerializer.Serialize(Sanitized(), json);

        /// <summary>The profile in a document of the service; the default one when it can't be read.</summary>
        public static ProfileDocument Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Default();
            try
            {
                return JsonSerializer.Deserialize<ProfileDocument>(text)?.Sanitized() ?? Default();
            }
            catch (JsonException)
            {
                return Default();
            }
        }

        public ProfileDocument Clone() => Parse(JsonSerializer.Serialize(this, json));

        /// <summary>Known modules only, within their limits; each kind at most once, except texts and showcases.</summary>
        public ProfileDocument Sanitized()
        {
            var modules = new List<ProfileModule>();
            foreach (ProfileModule module in Modules ?? new())
            {
                if (module == null || !ProfileModuleType.All.Contains(module.Type) || modules.Count >= MaxModules)
                    continue;
                if (modules.Count(m => m.Type == module.Type) >= ProfileModuleType.MaxCount(module.Type))
                    continue;
                modules.Add(new ProfileModule
                {
                    Type = module.Type,
                    Title = Clip(module.Title, MaxTitle),
                    Text = ProfileModuleType.HasText(module.Type) ? Clip(module.Text, MaxText) : null,
                    Games = (module.Games ?? new()).Where(id => id > 0).Distinct().Take(ProfileModuleType.MaxGames(module.Type)).ToList(),
                });
            }
            return new ProfileDocument
            {
                Version = 1,
                Tagline = Clip(Tagline, MaxTagline),
                Accent = Accent != null && Regex.IsMatch(Accent, "^#[0-9A-Fa-f]{6}$") ? Accent.ToUpperInvariant() : null,
                Modules = modules,
            };
        }

        private static string? Clip(string? text, int length)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;
            return text.Length <= length ? text : text[..length].TrimEnd();
        }
    }

    public sealed class ProfileModule
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        /// <summary>A title of the player's own (texts and showcases); null for the default title.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>The games chosen (showcase, favorite game), in their order.</summary>
        [JsonPropertyName("games")]
        public List<int> Games { get; set; } = new();
    }

    public static class ProfileModuleType
    {
        public const string Stats = "stats";
        public const string Recent = "recent";
        public const string Showcase = "showcase";
        public const string Favorite = "favorite";
        public const string Text = "text";
        public const string Completed = "completed";
        public const string MostPlayed = "mostPlayed";
        public const string Badges = "badges";

        /// <summary>In the order the "Add a section" menu shows them.</summary>
        public static readonly string[] All = { Showcase, Favorite, Text, Badges, Stats, Recent, MostPlayed, Completed };

        public static int MaxGames(string type) => type switch
        {
            Showcase => 8,
            Favorite => 1,
            _ => 0,
        };

        public static bool ChoosesGames(string type) => MaxGames(type) > 0;
        public static bool HasText(string type) => type == Text;

        public static int MaxCount(string type) => type switch
        {
            Text => 3,
            Showcase => 2,
            _ => 1,
        };
    }

    /// <summary>A badge earned with what a player did on the server; the level shows how far they went.</summary>
    public sealed record ProfileBadge(string Key, int Level, int Value, int NextGoal);

    public static class ProfileBadges
    {
        /// <summary>Each badge with its steps: reaching the first one earns it, each following one a level.</summary>
        public static readonly IReadOnlyDictionary<string, int[]> Steps = new Dictionary<string, int[]>
        {
            ["hours"] = new[] { 10, 50, 100, 250, 500, 1000 },
            ["played"] = new[] { 3, 10, 25, 50, 100 },
            ["completed"] = new[] { 1, 5, 10, 25, 50 },
            ["member"] = new[] { 30, 180, 365, 730, 1095 },
            ["week"] = new[] { 1, 3, 5 },
            ["marathon"] = new[] { 20, 50, 100, 200 },
        };

        /// <param name="stats">The figures of the profile.</param>
        /// <param name="memberSince">When the account was created (null: unknown).</param>
        /// <param name="longestMinutes">The most time spent on one game.</param>
        public static List<ProfileBadge> Compute(ProfileStats stats, DateTime? memberSince, int longestMinutes, DateTime nowUtc)
        {
            var values = new Dictionary<string, int>
            {
                ["hours"] = stats.TotalMinutes / 60,
                ["played"] = stats.GamesPlayed,
                ["completed"] = stats.Completed,
                ["member"] = memberSince == null ? 0 : Math.Max(0, (int)(nowUtc - memberSince.Value.ToUniversalTime()).TotalDays),
                ["week"] = stats.PlayedThisWeek,
                ["marathon"] = longestMinutes / 60,
            };
            var badges = new List<ProfileBadge>();
            foreach (var (key, steps) in Steps)
            {
                int value = values[key];
                int level = steps.Count(step => value >= step);
                if (level > 0)
                    badges.Add(new ProfileBadge(key, level, value, level < steps.Length ? steps[level] : 0));
            }
            return badges.OrderByDescending(b => (double)b.Level / Steps[b.Key].Length).ThenBy(b => b.Key).ToList();
        }
    }
}
