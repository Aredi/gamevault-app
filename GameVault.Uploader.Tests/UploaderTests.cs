using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameVault.Uploader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Uploader.Tests
{
    /// <summary>Answers /api/users/me like the GameVault server: tokens "admin" and "user" are known.</summary>
    internal sealed class FakeGameVault : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            string path = request.RequestUri!.AbsolutePath;
            // Sign-in: "admin:secret" is the only valid account
            if (path.EndsWith("/api/auth/basic/login"))
                return Task.FromResult(request.Headers.Authorization?.Parameter == Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("admin:secret"))
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "admin", refresh_token = "refresh-admin" }) }
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new { message = "Unauthorized" }) });
            if (path.EndsWith("/api/auth/refresh"))
                return Task.FromResult(request.Headers.Authorization?.Parameter == "refresh-admin"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "admin", refresh_token = "refresh-admin" }) }
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized));
            if (path.EndsWith("/api/games") && request.Headers.Authorization?.Parameter == "admin")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        data = new object[]
                        {
                            new { id = 5, title = "Escape from Duckov", version = (string?)null, release_date = (string?)null, deleted_at = (string?)null },
                            new { id = 6, title = "Prey", version = "v1.0", release_date = "2017-01-01T00:00:00.000Z", deleted_at = (string?)null },
                            new { id = 7, title = "Old", version = (string?)null, release_date = (string?)null, deleted_at = "2025-01-01T00:00:00.000Z" },
                        },
                    }),
                });
            string token = request.Headers.Authorization?.Parameter ?? "";
            object? user = token switch
            {
                "admin" => new { id = 1, username = "admin", role = 3 },
                "user" => new { id = 2, username = "player", role = 1 },
                _ => null,
            };
            return Task.FromResult(user == null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(user) });
        }
    }

    public sealed class UploaderTests : IDisposable
    {
        private readonly string files = Path.Combine(Path.GetTempPath(), $"gv-uploader-{Guid.NewGuid():N}");
        private readonly string profiles = Path.Combine(Path.GetTempPath(), $"gv-profiles-{Guid.NewGuid():N}");
        private readonly string saves = Path.Combine(Path.GetTempPath(), $"gv-saves-{Guid.NewGuid():N}");
        private readonly FakeGameVault gameVault = new();
        private readonly WebApplicationFactory<Program> factory;

        public UploaderTests()
        {
            Directory.CreateDirectory(files);
            // The server's savefiles volume: player 2 has three saves of game 7, player 1 one
            string game7 = Path.Combine(saves, "users", "2", "games", "7");
            Directory.CreateDirectory(game7);
            File.WriteAllText(Path.Combine(game7, "1760000000000_aaaa-1111.zip"), "oldest");
            File.WriteAllText(Path.Combine(game7, "1760100000000_bbbb-2222.zip"), "middle");
            File.WriteAllText(Path.Combine(game7, "1760200000000_aaaa-1111.zip"), "newest");
            File.WriteAllText(Path.Combine(game7, "notes.txt"), "not a save");
            Directory.CreateDirectory(Path.Combine(saves, "users", "1", "games", "7"));
            File.WriteAllText(Path.Combine(saves, "users", "1", "games", "7", "1760300000000_cccc-3333.zip"), "admin's");
            AdminCheck.ClearCache();
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("GAMEVAULT_URL", "http://gamevault.test");
                host.UseSetting("FILES_DIRECTORY", files);
                host.UseSetting("PROFILES_DIRECTORY", profiles);
                host.UseSetting("SAVEFILES_DIRECTORY", saves);
                host.ConfigureServices(services =>
                {
                    services.AddHttpClient<AdminCheck>().ConfigurePrimaryHttpMessageHandler(() => gameVault);
                    services.AddHttpClient<GameVaultProxy>().ConfigurePrimaryHttpMessageHandler(() => gameVault);
                });
            });
        }

        public void Dispose()
        {
            factory.Dispose();
            try { Directory.Delete(files, true); } catch { }
            try { Directory.Delete(profiles, true); } catch { }
            try { Directory.Delete(saves, true); } catch { }
        }

        private HttpClient Client(string? token)
        {
            HttpClient client = factory.CreateClient();
            if (token != null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private static async Task<long> Received(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("received").GetInt64();

        [Fact]
        public async Task AnAdministrator_UploadsInChunks_AndTheFileAppearsOnlyWhenComplete()
        {
            var client = Client("admin");
            byte[] archive = new byte[3_000_000];
            Random.Shared.NextBytes(archive);
            const string name = "Hades (v1.38) (W_P) (2020).zip";
            string url = Uri.EscapeDataString(name);

            for (int offset = 0; offset < archive.Length; offset += 1_000_000)
            {
                var put = await client.PutAsync($"/uploads/{url}?offset={offset}&total={archive.Length}", new ByteArrayContent(archive, offset, 1_000_000));
                Assert.Equal(HttpStatusCode.OK, put.StatusCode);
                Assert.Equal(offset + 1_000_000, await Received(put));
                // Invisible for the GameVault server until it is complete
                Assert.False(File.Exists(Path.Combine(files, name)));
            }
            var complete = await client.PostAsync($"/uploads/{url}/complete?size={archive.Length}", null);

            Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
            Assert.Equal(archive, File.ReadAllBytes(Path.Combine(files, name)));
            Assert.False(File.Exists(Path.Combine(files, name + ".partial")));
            // The admin check is cached, not repeated for every chunk
            Assert.Equal(1, gameVault.Calls);
        }

        [Fact]
        public async Task AnInterruptedUpload_ContinuesAtTheReceivedSize()
        {
            var client = Client("admin");
            await client.PutAsync("/uploads/Game.7z?offset=0", new ByteArrayContent(new byte[500]));

            var state = await client.GetFromJsonAsync<JsonElement>("/uploads/Game.7z");
            Assert.Equal(500, state.GetProperty("received").GetInt64());
            // A chunk sent twice (the answer got lost) is refused with the real position
            var again = await client.PutAsync("/uploads/Game.7z?offset=0", new ByteArrayContent(new byte[500]));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(500, await Received(again));
            // Completing too early is refused too
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/uploads/Game.7z/complete?size=1000", null)).StatusCode);
        }

        [Fact]
        public async Task AnExistingGame_IsOnlyReplacedWhenAsked()
        {
            File.WriteAllText(Path.Combine(files, "Celeste (L_P).zip"), "old");
            var client = Client("admin");
            await client.PutAsync("/uploads/Celeste%20(L_P).zip?offset=0", new StringContent("new"));

            Assert.True((await client.GetFromJsonAsync<JsonElement>("/uploads/Celeste%20(L_P).zip")).GetProperty("exists").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/uploads/Celeste%20(L_P).zip/complete?size=3", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/uploads/Celeste%20(L_P).zip/complete?size=3&overwrite=true", null)).StatusCode);
            Assert.Equal("new", File.ReadAllText(Path.Combine(files, "Celeste (L_P).zip")));
        }

        [Fact]
        public async Task OnlyAdministratorsMayUpload()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).PutAsync("/uploads/a.zip?offset=0", new StringContent("x"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client("forged").PutAsync("/uploads/a.zip?offset=0", new StringContent("x"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client("user").PutAsync("/uploads/a.zip?offset=0", new StringContent("x"))).StatusCode);
            Assert.Empty(Directory.GetFiles(files));
            // The status is public: the client checks the address with it
            Assert.Equal(HttpStatusCode.OK, (await Client(null).GetAsync("/status")).StatusCode);
        }

        [Theory]
        [InlineData("..%2F..%2Fetc%2Fpasswd.zip")]
        [InlineData("sub%2Fgame.zip")]
        [InlineData(".hidden.zip")]
        [InlineData("game.exe")]
        [InlineData("game.zip.partial")]
        [InlineData("a%3Ab.zip")]
        public async Task FileNamesCanNotLeaveTheGamesFolder(string name)
        {
            var response = await Client("admin").PutAsync($"/uploads/{name}?offset=0", new StringContent("x"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(Directory.GetFiles(files, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ArchiveNames_AreAccepted()
        {
            foreach (string name in new[] { "Hades (v1.38) (W_P) (2020).zip", "Celeste (L_P).tar.gz", "Game [GOG].7z", "Disc.iso" })
                Assert.True(UploadStore.IsValidName(name, out string error), $"{name}: {error}");
        }

        [Fact]
        public async Task Profiles_AreSeenByEveryPlayer_AndChangedOnlyByTheirOwner()
        {
            const string profile = "{\"tagline\":\"Speedrunner du dimanche\",\"modules\":[{\"type\":\"showcase\",\"games\":[4,8]}]}";
            Assert.Equal(HttpStatusCode.NotFound, (await Client("user").GetAsync("/profiles/2")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Client("user").PutAsync("/profiles/2", new StringContent(profile))).StatusCode);
            // Another player (here the administrator) sees it; nobody signed out does
            Assert.Equal(profile, await (await Client("admin").GetAsync("/profiles/2")).Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/profiles/2")).StatusCode);
            // A player can not change someone else's profile; an administrator can
            Assert.Equal(HttpStatusCode.Forbidden, (await Client("user").PutAsync("/profiles/1", new StringContent("{}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Client("admin").PutAsync("/profiles/2", new StringContent("{}"))).StatusCode);
            Assert.Equal("{}", await (await Client("user").GetAsync("/profiles/2")).Content.ReadAsStringAsync());
            using JsonDocument status = JsonDocument.Parse(await Client(null).GetStringAsync("/status"));
            Assert.True(status.RootElement.GetProperty("profiles").GetBoolean());
        }

        [Fact]
        public async Task Profiles_MustBeSmallJsonObjects()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Client("user").PutAsync("/profiles/2", new StringContent("not json"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Client("user").PutAsync("/profiles/2", new StringContent("[1,2]"))).StatusCode);
            string huge = "{\"about\":\"" + new string('a', ProfileStore.MaxSize) + "\"}";
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Client("user").PutAsync("/profiles/2", new StringContent(huge))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Client("user").GetAsync("/profiles/2")).StatusCode);
            Assert.Empty(Directory.GetFiles(profiles, "*.json"));
        }

        [Fact]
        public async Task ThePlayersSaves_AreListedNewestFirst_AndOnlyTheirOwn()
        {
            using JsonDocument list = JsonDocument.Parse(await Client("user").GetStringAsync("/savefiles/7"));
            var names = list.RootElement.EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
            Assert.Equal(new[] { "1760200000000_aaaa-1111.zip", "1760100000000_bbbb-2222.zip", "1760000000000_aaaa-1111.zip" }, names);
            Assert.Equal("bbbb-2222", list.RootElement[1].GetProperty("installationId").GetString());
            Assert.Equal("middle", await Client("user").GetStringAsync("/savefiles/7/1760100000000_bbbb-2222.zip"));
            // Another player's save, a path, or nothing signed in: refused
            Assert.Equal(HttpStatusCode.NotFound, (await Client("user").GetAsync("/savefiles/7/1760300000000_cccc-3333.zip")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Client("user").GetAsync("/savefiles/7/..%2F..%2F1%2Fgames%2F7%2F1760300000000_cccc-3333.zip")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/savefiles/7")).StatusCode);
            Assert.Equal("[]", await Client("user").GetStringAsync("/savefiles/99"));
            using JsonDocument status = JsonDocument.Parse(await Client(null).GetStringAsync("/status"));
            Assert.True(status.RootElement.GetProperty("saveHistory").GetBoolean());
        }

        [Fact]
        public async Task TheWebPage_IsServed_AndSignsInWithTheGameVaultAccount()
        {
            string page = await Client(null).GetStringAsync("/");
            Assert.Contains("Publier un jeu", page);
            Assert.Contains("app.js", page);
            Assert.Equal(HttpStatusCode.OK, (await Client(null).GetAsync("/app.js")).StatusCode);

            var bad = await Client(null).PostAsJsonAsync("/auth/login", new { username = "admin", password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
            var good = await Client(null).PostAsJsonAsync("/auth/login", new { username = "admin", password = "secret" });
            Assert.Equal(HttpStatusCode.OK, good.StatusCode);
            using JsonDocument pair = JsonDocument.Parse(await good.Content.ReadAsStringAsync());
            Assert.Equal("admin", pair.RootElement.GetProperty("access_token").GetString());

            var refreshing = Client("refresh-admin");
            Assert.Equal(HttpStatusCode.OK, (await refreshing.PostAsync("/auth/refresh", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).PostAsync("/auth/refresh", null)).StatusCode);

            using JsonDocument me = JsonDocument.Parse(await Client("admin").GetStringAsync("/me"));
            Assert.True(me.RootElement.GetProperty("admin").GetBoolean());
            using JsonDocument player = JsonDocument.Parse(await Client("user").GetStringAsync("/me"));
            Assert.False(player.RootElement.GetProperty("admin").GetBoolean());
        }

        [Fact]
        public async Task TheGamesOfTheServer_AreFoundForTheWebPage()
        {
            using JsonDocument games = JsonDocument.Parse(await Client("admin").GetStringAsync("/games?search=escape"));
            var list = games.RootElement.EnumerateArray().ToList();
            Assert.Equal(2, list.Count);// the deleted one is left out
            Assert.Equal("Escape from Duckov", list[0].GetProperty("title").GetString());
            Assert.Equal(2017, list[1].GetProperty("year").GetInt32());
            Assert.Equal("[]", await Client("admin").GetStringAsync("/games?search=e"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/games?search=escape")).StatusCode);
        }
    }
}
