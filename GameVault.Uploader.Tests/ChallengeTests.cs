using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Uploader.Tests
{
    public sealed partial class UploaderTests
    {
        /// <summary>A Tuesday: the week 2026-W46 goes from Monday the 9th to Monday the 16th of November.</summary>
        private DateTime challengeTime = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);

        private ChallengeStore Challenges()
        {
            var store = factory.Services.GetRequiredService<ChallengeStore>();
            store.Clock = () => challengeTime;
            return store;
        }

        /// <summary>The player plays this game of the library for some minutes, as their client says it each minute.</summary>
        private async Task<HttpResponseMessage> PlayAsync(HttpClient player, int gameId, int minutes)
        {
            HttpResponseMessage? last = null;
            for (int i = 0; i < minutes; i++)
            {
                challengeTime = challengeTime.AddMinutes(1);
                last = await player.PostAsJsonAsync("/challenges/me/plays", new { gameIds = new[] { gameId } });
                Assert.True(last.IsSuccessStatusCode);
            }
            return last!;
        }

        private async Task<JsonElement> ChallengesAsync(HttpClient client) =>
            JsonDocument.Parse(await client.GetStringAsync("/challenges")).RootElement;

        [Fact]
        public async Task TheChallengeOfTheWeek_CountsTheMinutesPlayed_AndRanksThePlayers()
        {
            Challenges();
            var admin = Client("admin");
            var player = Client("user");
            var other = Client("p10");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/challenges")).StatusCode);

            // Nobody chose: the week has its automatic challenge
            JsonElement state = await ChallengesAsync(player);
            Assert.Equal("2026-W46", state.GetProperty("week").GetString());
            Assert.Equal(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), state.GetProperty("endsAt").GetDateTime().ToUniversalTime());
            Assert.Equal(JsonValueKind.Null, state.GetProperty("challenge").GetProperty("setBy").ValueKind);
            Assert.Equal(0, state.GetProperty("board").GetArrayLength());

            // Only an administrator chooses it
            Assert.Equal(HttpStatusCode.Forbidden, (await player.PutAsJsonAsync("/challenges/week", new { kind = "hours", goal = 15 })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "hours", goal = 15 })).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await PlayAsync(other, 9, 5)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await PlayAsync(player, 9, 14)).StatusCode);
            // The same minute said twice counts once
            Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsJsonAsync("/challenges/me/plays", new { gameIds = new[] { 9 } })).StatusCode);
            // The minute that reaches the goal says it, the next ones do not say it again
            HttpResponseMessage reached = await PlayAsync(player, 9, 1);
            Assert.Equal(HttpStatusCode.OK, reached.StatusCode);
            Assert.True(JsonDocument.Parse(await reached.Content.ReadAsStringAsync()).RootElement.GetProperty("challengeDone").GetBoolean());
            Assert.Equal(HttpStatusCode.NoContent, (await PlayAsync(player, 9, 1)).StatusCode);

            state = await ChallengesAsync(other);
            Assert.Equal(("hours", 15, "admin"), (state.GetProperty("challenge").GetProperty("kind").GetString(), state.GetProperty("challenge").GetProperty("goal").GetInt32(), state.GetProperty("challenge").GetProperty("setBy").GetString()));
            var board = state.GetProperty("board").EnumerateArray().ToList();
            Assert.Equal(new[] { (2, "player", 16), (10, "p10", 5) }, board.Select(r => (r.GetProperty("userId").GetInt32(), r.GetProperty("username").GetString()!, r.GetProperty("value").GetInt32())));
            Assert.Equal(JsonValueKind.String, board[0].GetProperty("doneAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, board[1].GetProperty("doneAt").ValueKind);

            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/challenges/me/plays", new { gameIds = Array.Empty<int>() })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/challenges/me/plays", new { gameIds = new[] { -1 } })).StatusCode);
        }

        [Fact]
        public async Task ADiscovery_IsAGameNeverPlayedBefore_OfTheLibraryOrRetro()
        {
            Challenges();
            var player = Client("user");
            Assert.Equal(HttpStatusCode.NoContent, (await Client("admin").PutAsJsonAsync("/challenges/week", new { kind = "discovery", goal = 20 })).StatusCode);

            // The game 5 was played before the challenges (the GameVault server says so): no discovery
            await PlayAsync(player, 5, 6);
            Assert.Equal(0, (await ChallengesAsync(player)).GetProperty("board").GetArrayLength());
            // The game 6 was opened and never played, the game 9 is new: the best of them counts, not their sum
            await PlayAsync(player, 6, 4);
            await PlayAsync(player, 9, 7);
            Assert.Equal(7, (await ChallengesAsync(player)).GetProperty("board")[0].GetProperty("value").GetInt32());

            // A retro game played for the first time is one too, and completes the challenge
            HttpResponseMessage retro = await player.PostAsJsonAsync("/retro/players/me/plays", new { romId = 40, platform = "nes", platformName = "NES", name = "Zelda", minutes = 25 });
            Assert.Equal(HttpStatusCode.OK, retro.StatusCode);
            Assert.Contains("challengeDone", await retro.Content.ReadAsStringAsync());
            Assert.Equal(25, (await ChallengesAsync(player)).GetProperty("board")[0].GetProperty("value").GetInt32());
            Assert.Equal(25, JsonDocument.Parse(await player.GetStringAsync("/retro/players/me")).RootElement.GetProperty("plays")[0].GetProperty("minutes").GetInt32());

            // The week after, these games are known: they are no discovery any more
            challengeTime = challengeTime.AddDays(7);
            Assert.Equal(HttpStatusCode.NoContent, (await Client("admin").PutAsJsonAsync("/challenges/week", new { kind = "discovery", goal = 20 })).StatusCode);
            await PlayAsync(player, 9, 3);
            Assert.Equal(0, (await ChallengesAsync(player)).GetProperty("board").GetArrayLength());
        }

        [Fact]
        public async Task TheAchievementsOfAGame_EarnedThisWeek_CompleteItsChallenge()
        {
            Challenges();
            var admin = Client("admin");
            var player = Client("user");
            var mario = new { kind = "retro", id = 77, platform = "snes", platformName = "Super Nintendo", title = "Super Mario World", cover = "roms/4/77/cover/big.png" };
            // Achievements are earned on retro games; a challenge on a game needs it
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "achievements", goal = 1, game = new { kind = "pc", id = 5, title = "Prey" } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "game", goal = 60 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "dance", goal = 60 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "hours", goal = 5 })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "achievements", goal = 2, game = mario })).StatusCode);

            object Earned(string name, params DateTime[] dates) => new { name, platformName = "SNES", earned = dates.Length, total = 50, unlocks = dates.Select((date, i) => new { title = $"Level {i + 1}", date }) };
            DateTime monday = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
            // Another game's achievements, and the ones earned last week, do not count
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/retro/players/me/achievements/nes/40", Earned("Zelda", monday.AddHours(3), monday.AddHours(4)))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/retro/players/me/achievements/snes/77", Earned("Super Mario World", monday.AddHours(-2), monday.AddHours(5)))).StatusCode);
            JsonElement state = await ChallengesAsync(player);
            Assert.Equal(("Super Mario World", "snes", 77, "roms/4/77/cover/big.png"), (state.GetProperty("challenge").GetProperty("game").GetProperty("title").GetString(), state.GetProperty("challenge").GetProperty("game").GetProperty("platform").GetString(),
                state.GetProperty("challenge").GetProperty("game").GetProperty("id").GetInt32(), state.GetProperty("challenge").GetProperty("game").GetProperty("cover").GetString()));
            Assert.Equal(1, state.GetProperty("board")[0].GetProperty("value").GetInt32());
            Assert.Equal(HttpStatusCode.OK, (await player.PutAsJsonAsync("/retro/players/me/achievements/snes/77", Earned("Super Mario World", monday.AddHours(-2), monday.AddHours(5), monday.AddHours(30)))).StatusCode);

            // The challenge changed in the week: what was done since Monday counts for the new one
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "achievements", goal = 4 })).StatusCode);
            state = await ChallengesAsync(player);
            Assert.Equal(JsonValueKind.Null, state.GetProperty("challenge").GetProperty("game").ValueKind);
            Assert.Equal((4, JsonValueKind.String), (state.GetProperty("board")[0].GetProperty("value").GetInt32(), state.GetProperty("board")[0].GetProperty("doneAt").ValueKind));
        }

        [Fact]
        public async Task ANewWeek_HasANewChallenge_AndThePastOnesKeepTheirWinner()
        {
            ChallengeStore store = Challenges();
            var admin = Client("admin");
            var player = Client("user");
            var other = Client("p10");
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/challenges/week", new { kind = "game", goal = 15, game = new { kind = "pc", id = 9, title = " Hades ", cover = "anything" } })).StatusCode);
            await PlayAsync(other, 8, 30);// another game: nothing for this challenge
            await PlayAsync(other, 9, 15);
            await PlayAsync(player, 9, 15);
            await PlayAsync(admin, 9, 2);

            // The Monday after: the next automatic challenge, nobody on its ranking yet
            challengeTime = new DateTime(2026, 11, 16, 0, 5, 0, DateTimeKind.Utc);
            JsonElement state = await ChallengesAsync(player);
            Assert.Equal("2026-W47", state.GetProperty("week").GetString());
            Assert.Equal(store.Automatic("2026-W47").Kind, state.GetProperty("challenge").GetProperty("kind").GetString());
            Assert.NotEqual(store.Automatic("2026-W46").Kind, store.Automatic("2026-W47").Kind);
            Assert.Equal(0, state.GetProperty("board").GetArrayLength());

            JsonElement last = Assert.Single(state.GetProperty("past").EnumerateArray());
            Assert.Equal(("2026-W46", "game", "Hades", 2), (last.GetProperty("week").GetString(), last.GetProperty("challenge").GetProperty("kind").GetString(),
                last.GetProperty("challenge").GetProperty("game").GetProperty("title").GetString(), last.GetProperty("completed").GetInt32()));
            Assert.Equal(JsonValueKind.Null, last.GetProperty("challenge").GetProperty("game").GetProperty("cover").ValueKind);
            Assert.Equal("p10", last.GetProperty("winner").GetProperty("username").GetString());

            // The challenges completed so far: who has the most, then who was first the most often
            Assert.Equal(new[] { (10, 1, 1), (2, 1, 0) }, state.GetProperty("allTime").EnumerateArray().Select(h => (h.GetProperty("userId").GetInt32(), h.GetProperty("completed").GetInt32(), h.GetProperty("first").GetInt32())));

            // A week nobody completed has no winner
            challengeTime = challengeTime.AddDays(7);
            state = await ChallengesAsync(player);
            Assert.Equal(new[] { "2026-W47", "2026-W46" }, state.GetProperty("past").EnumerateArray().Select(w => w.GetProperty("week").GetString()));
            Assert.Equal((JsonValueKind.Null, 0), (state.GetProperty("past")[0].GetProperty("winner").ValueKind, state.GetProperty("past")[0].GetProperty("completed").GetInt32()));
        }

        [Fact]
        public void EachKindOfChallenge_CountsItsOwnWay()
        {
            var entry = new ChallengeStore.Entry
            {
                Minutes = { ["gv:1"] = 40, ["gv:2"] = 10, ["nes:40"] = 20, ["snes:77"] = 15 },
                Fresh = { "gv:2", "nes:40" },
                Unlocks = { ["snes:77"] = new() { DateTime.UtcNow }, ["nes:40"] = new() { DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1) } },
            };
            int Value(string kind, ChatStore.SharedGame? game = null) => ChallengeStore.Progress(new ChallengeStore.Challenge { Kind = kind, Game = game }, entry);
            Assert.Equal(85, Value(ChallengeStore.Hours));
            // A game counts from 15 minutes
            Assert.Equal(3, Value(ChallengeStore.Games));
            Assert.Equal(20, Value(ChallengeStore.Discovery));
            Assert.Equal(35, Value(ChallengeStore.Retro));
            Assert.Equal(3, Value(ChallengeStore.Achievements));
            Assert.Equal(1, Value(ChallengeStore.Achievements, new ChatStore.SharedGame { Kind = "retro", Platform = "snes", Id = 77 }));
            Assert.Equal(40, Value(ChallengeStore.Game, new ChatStore.SharedGame { Kind = "pc", Id = 1 }));
            Assert.Equal(20, Value(ChallengeStore.Game, new ChatStore.SharedGame { Kind = "retro", Platform = "nes", Id = 40 }));
            Assert.Equal(0, ChallengeStore.Progress(new ChallengeStore.Challenge { Kind = ChallengeStore.Discovery }, new ChallengeStore.Entry()));
        }
    }
}
