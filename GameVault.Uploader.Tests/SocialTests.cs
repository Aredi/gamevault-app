using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Uploader.Tests
{
    public sealed partial class UploaderTests
    {
        private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        [Fact]
        public async Task AReaction_OnAnotherPlayersActivity_IsKeptAndShown_OnceByPlayer()
        {
            var player = Client("user");
            var other = Client("p10");
            const string item = "pc:10:793:2026-11-20";
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync($"/social/reactions?items={item}")).StatusCode);

            var put = await player.PutAsJsonAsync("/social/reactions", new { item, emoji = "🔥" });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            // A new one replaces it: one reaction per player and line
            put = await player.PutAsJsonAsync("/social/reactions", new { item, emoji = "GG" });
            JsonElement shown = await JsonOf(put);
            Assert.Equal(1, shown.GetArrayLength());
            Assert.Equal("GG", shown[0].GetProperty("emoji").GetString());
            Assert.Equal("player", shown[0].GetProperty("username").GetString());

            JsonElement all = JsonDocument.Parse(await other.GetStringAsync($"/social/reactions?items={item},pc:2:1:2026-11-20,bad")).RootElement;
            Assert.Equal("GG", all.GetProperty(item)[0].GetProperty("emoji").GetString());
            Assert.False(all.TryGetProperty("pc:2:1:2026-11-20", out _));

            // Not on one's own line, not any emoji, not a line that is no line
            Assert.Equal(HttpStatusCode.BadRequest, (await other.PutAsJsonAsync("/social/reactions", new { item, emoji = "🔥" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync("/social/reactions", new { item, emoji = "💩" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync("/social/reactions", new { item = "../x", emoji = "🔥" })).StatusCode);

            // Taken away
            Assert.Equal(0, (await JsonOf(await player.PutAsJsonAsync("/social/reactions", new { item, emoji = (string?)null }))).GetArrayLength());
            // Kept on the disk: a new store reads it again
            await player.PutAsJsonAsync("/social/reactions", new { item, emoji = "👏" });
            var store = factory.Services.GetRequiredService<SocialStore>();
            Assert.Single(store.ReactionsOn(new[] { item })[item]);

            // A player id too long for a number: refused, not a crash
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync("/social/reactions", new { item = "pc:9999999999:1:2026-11-20", emoji = "🔥" })).StatusCode);
            // Many a day at most; the same one again changes nothing and tells nobody
            for (int i = 0; i < SocialStore.ReactionsPerDay; i++)
                Assert.False(store.React($"pc:3:{i}:2026-11-20", 3, 50, "spam", "GG").Refused);
            Assert.True(store.React("pc:3:x:2026-11-20", 3, 50, "spam", "GG").Refused);
            var again = store.React("pc:3:0:2026-11-20", 3, 50, "spam", "GG");
            Assert.False(again.Refused);
            Assert.False(again.Changed);
        }

        [Fact]
        public async Task AGameRecommended_ToAFriend_IsTheirs_UntilTheyPutItAway()
        {
            var player = Client("user");
            var friend = Client("p10");
            var made = await player.PostAsJsonAsync("/social/recommendations", new { to = 10, game = new { kind = "pc", id = 793, title = "Warhammer Survivors" }, note = "  Il est génial à deux  " });
            Assert.Equal(HttpStatusCode.Created, made.StatusCode);
            string id = (await JsonOf(made)).GetProperty("id").GetString()!;

            JsonElement mine = JsonDocument.Parse(await friend.GetStringAsync("/social/recommendations/me")).RootElement;
            Assert.Equal(1, mine.GetArrayLength());
            Assert.Equal("player", mine[0].GetProperty("fromName").GetString());
            Assert.Equal("Il est génial à deux", mine[0].GetProperty("note").GetString());
            Assert.Equal(0, JsonDocument.Parse(await player.GetStringAsync("/social/recommendations/me")).RootElement.GetArrayLength());

            // The same game again: the newer one only
            await player.PostAsJsonAsync("/social/recommendations", new { to = 10, game = new { kind = "pc", id = 793, title = "Warhammer Survivors" } });
            mine = JsonDocument.Parse(await friend.GetStringAsync("/social/recommendations/me")).RootElement;
            Assert.Equal(1, mine.GetArrayLength());
            id = mine[0].GetProperty("id").GetString()!;

            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/social/recommendations", new { to = 2, game = new { kind = "pc", id = 1, title = "X" } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/social/recommendations", new { to = 10, game = new { kind = "retro", id = 1, title = "X" } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/social/recommendations", new { to = 10, game = new { kind = "pc", id = 1, title = "X" }, note = new string('a', 301) })).StatusCode);

            // Only the friend puts it away
            Assert.Equal(HttpStatusCode.NotFound, (await player.DeleteAsync($"/social/recommendations/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await friend.DeleteAsync($"/social/recommendations/{id}")).StatusCode);
            Assert.Equal(0, JsonDocument.Parse(await friend.GetStringAsync("/social/recommendations/me")).RootElement.GetArrayLength());

            // Not a flood
            for (int i = 0; i < SocialStore.RecommendationsPerDay; i++)
                Assert.Equal(HttpStatusCode.Created, (await player.PostAsJsonAsync("/social/recommendations", new { to = 10, game = new { kind = "pc", id = 1000 + i, title = "G" } })).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await player.PostAsJsonAsync("/social/recommendations", new { to = 10, game = new { kind = "pc", id = 5000, title = "G" } })).StatusCode);
        }

        [Fact]
        public async Task AReview_IsThePlayers_OnePerGame_AndAnAdministratorCanTakeItAway()
        {
            var player = Client("user");
            var other = Client("p10");
            var admin = Client("admin");
            const string game = "retro:psx:2161";
            Assert.Equal(HttpStatusCode.OK, (await player.PutAsJsonAsync($"/social/reviews/{game}/me", new { stars = 4, text = "Très bon" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await player.PutAsJsonAsync($"/social/reviews/{game}/me", new { stars = 5, text = "  Excellent  " })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await other.PutAsJsonAsync($"/social/reviews/{game}/me", new { stars = 2 })).StatusCode);

            JsonElement reviews = JsonDocument.Parse(await other.GetStringAsync($"/social/reviews/{game}")).RootElement;
            Assert.Equal(2, reviews.GetArrayLength());
            // The summary a library sorts by: (5 + 2) / 2
            JsonElement rating = JsonDocument.Parse(await other.GetStringAsync("/social/reviews")).RootElement.GetProperty(game);
            Assert.Equal(3.5, rating.GetProperty("stars").GetDouble());
            Assert.Equal(2, rating.GetProperty("count").GetInt32());
            JsonElement mine = reviews.EnumerateArray().Single(r => r.GetProperty("userId").GetInt32() == 2);
            Assert.Equal(5, mine.GetProperty("stars").GetInt32());
            Assert.Equal("Excellent", mine.GetProperty("text").GetString());

            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync($"/social/reviews/{game}/me", new { stars = 6 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync("/social/reviews/nope/me", new { stars = 3 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync($"/social/reviews/{game}/me", new { stars = 3, text = new string('a', 801) })).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/social/reviews/{game}/2")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/social/reviews/{game}/2")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync($"/social/reviews/{game}/me")).StatusCode);
            Assert.Equal(0, JsonDocument.Parse(await player.GetStringAsync($"/social/reviews/{game}")).RootElement.GetArrayLength());
        }
    }
}
