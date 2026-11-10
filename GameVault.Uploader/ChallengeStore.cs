using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameVault.Uploader
{
    /// <summary>
    /// The challenge of the week and what each player did for it. A week goes from Monday 00:00 UTC to the next one
    /// and has one file: its challenge (an automatic one unless an administrator chose it) and, per player, the
    /// minutes played per game, the games played for the first time and the retro achievements earned. All of it is
    /// kept whatever the challenge is, so a challenge changed in the middle of the week counts what was already done.
    /// </summary>
    public sealed class ChallengeStore
    {
        public const string Hours = "hours", Games = "games", Discovery = "discovery", Retro = "retro", Achievements = "achievements", Game = "game";
        /// <summary>A game counts among the "different games" from this many minutes.</summary>
        public const int CountedGameMinutes = 15;
        /// <summary>The weeks before this one shown with their winner.</summary>
        public const int PastWeeksShown = 8;

        public sealed class Challenge
        {
            [JsonPropertyName("kind")] public string Kind { get; set; } = Hours;
            /// <summary>Minutes, or a number of games or achievements.</summary>
            [JsonPropertyName("goal")] public int Goal { get; set; }
            /// <summary>The game to play ("game"), or the only one whose achievements count ("achievements").</summary>
            [JsonPropertyName("game")] public ChatStore.SharedGame? Game { get; set; }
            /// <summary>The administrator who chose it; nobody for an automatic one.</summary>
            [JsonPropertyName("setBy")] public string? SetBy { get; set; }
        }

        public sealed class Entry
        {
            [JsonPropertyName("username")] public string Username { get; set; } = "";
            /// <summary>By game: "gv:12" a game of the library, "psx:2161" a retro game.</summary>
            [JsonPropertyName("minutes")] public Dictionary<string, int> Minutes { get; set; } = new();
            /// <summary>The games the player had never played before this week.</summary>
            [JsonPropertyName("fresh")] public List<string> Fresh { get; set; } = new();
            /// <summary>The retro achievements earned this week, by game.</summary>
            [JsonPropertyName("unlocks")] public Dictionary<string, List<DateTime>> Unlocks { get; set; } = new();
            [JsonPropertyName("doneAt")] public DateTime? DoneAt { get; set; }
        }

        public sealed class Week
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("challenge")] public Challenge Challenge { get; set; } = new();
            [JsonPropertyName("players")] public Dictionary<int, Entry> Players { get; set; } = new();
        }

        /// <summary>The games a player ever played, to tell the ones they discover.</summary>
        private sealed class Known
        {
            /// <summary>Filled with the games played before the challenges existed.</summary>
            [JsonPropertyName("seeded")] public bool Seeded { get; set; }
            [JsonPropertyName("keys")] public HashSet<string> Keys { get; set; } = new();
        }

        public sealed record Rank(int UserId, string Username, int Value, DateTime? DoneAt);
        public sealed record Honor(int UserId, string Username, int Completed, int First);
        public sealed record Past(string Id, DateTime StartsAt, Challenge Challenge, Rank? Winner, int Completed);

        private readonly string directory;
        private readonly bool retro;
        private readonly object gate = new();
        private Week? current;
        private List<Week>? past;
        /// <summary>When each player's last minute on a library game was counted: a minute is a minute.</summary>
        private readonly Dictionary<(int, string), DateTime> lastMinute = new();

        public ChallengeStore(UploaderOptions options, ILogger<ChallengeStore> logger)
        {
            directory = options.ChallengesDirectory;
            retro = !string.IsNullOrEmpty(options.RommUrl);
            Enabled = ProfileStore.IsWritable(directory, out string error);
            if (!Enabled)
                logger.LogWarning("Challenges are off: {Directory} can not be written ({Error}). Mount a volume there to keep them.", directory, error);
        }

        public bool Enabled { get; }

        /// <summary>The time, for the tests to move from a week to the next.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public static string WeekOf(DateTime date) => $"{ISOWeek.GetYear(date)}-W{ISOWeek.GetWeekOfYear(date):00}";

        /// <summary>Monday 00:00 UTC of "2026-W46".</summary>
        public static DateTime StartOf(string week) =>
            DateTime.SpecifyKind(ISOWeek.ToDateTime(int.Parse(week[..4], CultureInfo.InvariantCulture), int.Parse(week[6..], CultureInfo.InvariantCulture), DayOfWeek.Monday), DateTimeKind.Utc);

        public static string LibraryKey(int gameId) => $"gv:{gameId}";

        private static string KeyOf(ChatStore.SharedGame game) => game.Kind == "retro" ? $"{game.Platform}:{game.Id}" : LibraryKey(game.Id);

        /// <summary>The challenge of a week nobody chose: they follow each other, the retro ones only with a retro library.</summary>
        internal Challenge Automatic(string week)
        {
            var all = new List<Challenge>
            {
                new() { Kind = Hours, Goal = 300 },
                new() { Kind = Games, Goal = 3 },
                new() { Kind = Discovery, Goal = 120 },
            };
            if (retro)
            {
                all.Add(new() { Kind = Retro, Goal = 120 });
                all.Add(new() { Kind = Achievements, Goal = 3 });
            }
            return all[int.Parse(week[6..], CultureInfo.InvariantCulture) % all.Count];
        }

        /// <summary>How far a player is: minutes, games or achievements, as the challenge counts.</summary>
        public static int Progress(Challenge challenge, Entry entry) => challenge.Kind switch
        {
            Hours => entry.Minutes.Values.Sum(),
            Games => entry.Minutes.Values.Count(minutes => minutes >= CountedGameMinutes),
            Discovery => entry.Fresh.Select(key => entry.Minutes.GetValueOrDefault(key)).DefaultIfEmpty().Max(),
            Retro => entry.Minutes.Where(game => !game.Key.StartsWith("gv:", StringComparison.Ordinal)).Sum(game => game.Value),
            Achievements => challenge.Game == null ? entry.Unlocks.Values.Sum(dates => dates.Count) : entry.Unlocks.GetValueOrDefault(KeyOf(challenge.Game))?.Count ?? 0,
            Game => challenge.Game == null ? 0 : entry.Minutes.GetValueOrDefault(KeyOf(challenge.Game)),
            _ => 0,
        };

        private string WeekFile(string id) => Path.Combine(directory, "weeks", id + ".json");
        private string KnownFile(int userId) => Path.Combine(directory, "known", userId + ".json");

        private static void Write(string file, object value)
        {
            // Written aside then moved: never half written
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file + ".part", JsonSerializer.Serialize(value));
            File.Move(file + ".part", file, overwrite: true);
        }

        private static T? Read<T>(string file) where T : class
        {
            try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : null; }
            catch (JsonException) { return null; }
        }

        /// <summary>This week, made with its automatic challenge the first time it is asked for. Under the lock.</summary>
        private Week Current()
        {
            string id = WeekOf(Clock());
            if (current?.Id == id)
                return current;
            past = null;
            current = Read<Week>(WeekFile(id));
            if (current == null)
                Write(WeekFile(id), current = new Week { Id = id, Challenge = Automatic(id) });
            return current;
        }

        /// <summary>The weeks before this one, the last one first. Under the lock.</summary>
        private List<Week> PastWeeks()
        {
            string now = Current().Id;
            string weeks = Path.Combine(directory, "weeks");
            return past ??= !Directory.Exists(weeks) ? new() : Directory.EnumerateFiles(weeks, "*.json")
                .Select(Read<Week>).OfType<Week>().Where(week => string.CompareOrdinal(week.Id, now) < 0)
                .OrderByDescending(week => week.Id, StringComparer.Ordinal).ToList();
        }

        private Entry EntryOf(Week week, int userId, string username)
        {
            if (!week.Players.TryGetValue(userId, out Entry? entry))
                week.Players[userId] = entry = new Entry();
            entry.Username = username;
            return entry;
        }

        /// <returns>Whether the player just reached the goal.</returns>
        private bool Settle(Week week, Entry entry)
        {
            if (entry.DoneAt != null || Progress(week.Challenge, entry) < week.Challenge.Goal)
                return false;
            entry.DoneAt = Clock();
            return true;
        }

        /// <summary>Whether the games the player played before the challenges are known (they are asked once).</summary>
        public bool IsSeeded(int userId)
        {
            lock (gate)
                return Read<Known>(KnownFile(userId))?.Seeded == true;
        }

        /// <summary>The games the player played before: none of them is a discovery.</summary>
        public void Seed(int userId, IEnumerable<string> keys)
        {
            lock (gate)
            {
                Known known = Read<Known>(KnownFile(userId)) ?? new Known();
                known.Keys.UnionWith(keys);
                known.Seeded = true;
                Write(KnownFile(userId), known);
            }
        }

        /// <summary>A minute on each of these games of the library; a game is counted once a minute at most.</summary>
        /// <returns>Whether the player just completed the challenge.</returns>
        public bool AddLibraryMinute(int userId, string username, IEnumerable<int> gameIds)
        {
            lock (gate)
            {
                DateTime now = Clock();
                bool done = false;
                foreach (string key in gameIds.Distinct().Select(LibraryKey))
                {
                    if (lastMinute.TryGetValue((userId, key), out DateTime last) && now - last < TimeSpan.FromSeconds(45) && now >= last)
                        continue;
                    lastMinute[(userId, key)] = now;
                    done |= AddPlay(userId, username, key, 1);
                }
                return done;
            }
        }

        /// <summary>Some minutes on a game ("gv:12", "psx:2161").</summary>
        /// <returns>Whether the player just completed the challenge.</returns>
        public bool AddPlay(int userId, string username, string key, int minutes)
        {
            lock (gate)
            {
                Week week = Current();
                Entry entry = EntryOf(week, userId, username);
                Known known = Read<Known>(KnownFile(userId)) ?? new Known();
                if (known.Keys.Add(key))
                {
                    // Before the games played earlier are known, nothing is called a discovery
                    if (known.Seeded)
                        entry.Fresh.Add(key);
                    Write(KnownFile(userId), known);
                }
                entry.Minutes[key] = entry.Minutes.GetValueOrDefault(key) + minutes;
                bool done = Settle(week, entry);
                Write(WeekFile(week.Id), week);
                return done;
            }
        }

        /// <summary>The achievements of a retro game as they are now: the ones earned this week count.</summary>
        /// <returns>Whether the player just completed the challenge.</returns>
        public bool SetUnlocks(int userId, string username, string key, IEnumerable<DateTime> dates)
        {
            lock (gate)
            {
                Week week = Current();
                DateTime start = StartOf(week.Id), end = start.AddDays(7);
                List<DateTime> earned = dates.Select(date => date.ToUniversalTime()).Where(date => date >= start && date < end).Distinct().Order().ToList();
                if (earned.Count == 0 && !(week.Players.TryGetValue(userId, out Entry? had) && had.Unlocks.ContainsKey(key)))
                    return false;
                Entry entry = EntryOf(week, userId, username);
                if (earned.Count == 0)
                    entry.Unlocks.Remove(key);
                else
                    entry.Unlocks[key] = earned;
                bool done = Settle(week, entry);
                Write(WeekFile(week.Id), week);
                return done;
            }
        }

        /// <summary>An administrator chooses this week's challenge: what was done since Monday counts for it.</summary>
        public void Set(Challenge challenge)
        {
            lock (gate)
            {
                Week week = Current();
                week.Challenge = challenge;
                foreach (Entry entry in week.Players.Values)
                {
                    entry.DoneAt = null;
                    Settle(week, entry);
                }
                Write(WeekFile(week.Id), week);
            }
        }

        /// <summary>Who did it first, then who is the closest.</summary>
        private static List<Rank> BoardOf(Week week) => week.Players
            .Select(player => new Rank(player.Key, player.Value.Username, Progress(week.Challenge, player.Value), player.Value.DoneAt))
            .Where(rank => rank.Value > 0)
            .OrderBy(rank => rank.DoneAt ?? DateTime.MaxValue).ThenByDescending(rank => rank.Value).ThenBy(rank => rank.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public sealed record State(string Week, DateTime StartsAt, DateTime EndsAt, Challenge Challenge, List<Rank> Board, List<Honor> AllTime, List<Past> Past);

        /// <summary>This week's challenge and ranking, the challenges each player completed so far, and the last weeks.</summary>
        public State Get()
        {
            lock (gate)
            {
                Week week = Current();
                List<Week> before = PastWeeks();
                var names = new Dictionary<int, string>();
                var completed = new Dictionary<int, int>();
                var first = new Dictionary<int, int>();
                // The oldest first: the name kept is the last one seen
                foreach (Week one in before.AsEnumerable().Reverse().Append(week))
                {
                    List<Rank> done = BoardOf(one).Where(rank => rank.DoneAt != null).ToList();
                    foreach (Rank rank in done)
                    {
                        names[rank.UserId] = rank.Username;
                        completed[rank.UserId] = completed.GetValueOrDefault(rank.UserId) + 1;
                    }
                    if (done.Count > 0)
                        first[done[0].UserId] = first.GetValueOrDefault(done[0].UserId) + 1;
                }
                DateTime start = StartOf(week.Id);
                return new State(week.Id, start, start.AddDays(7), week.Challenge, BoardOf(week),
                    completed.Select(player => new Honor(player.Key, names[player.Key], player.Value, first.GetValueOrDefault(player.Key)))
                        .OrderByDescending(honor => honor.Completed).ThenByDescending(honor => honor.First).ThenBy(honor => honor.Username, StringComparer.OrdinalIgnoreCase).ToList(),
                    before.Take(PastWeeksShown).Select(one =>
                    {
                        List<Rank> done = BoardOf(one).Where(rank => rank.DoneAt != null).ToList();
                        return new Past(one.Id, StartOf(one.Id), one.Challenge, done.FirstOrDefault(), done.Count);
                    }).ToList());
            }
        }
    }
}
