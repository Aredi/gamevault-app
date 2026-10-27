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
            if (path.EndsWith("/api/games/6"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 6, title = "Prey" }) });
            if (path.EndsWith("/api/games") && request.Headers.Authorization?.Parameter == "admin")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        data = new object[]
                        {
                            new { id = 5, title = "Escape from Duckov", version = (string?)null, release_date = (string?)null, deleted_at = (string?)null, metadata = new { title = "Escape From Duckov" } },
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
                // More players: "p10" is the player 10
                _ when token.StartsWith('p') && int.TryParse(token[1..], out int n) => new { id = n, username = token, role = 1 },
                _ => null,
            };
            return Task.FromResult(user == null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(user) });
        }
    }

    /// <summary>PCGamingWiki's pages: Escape from Duckov plays in LAN with 4 players, Prey does not; its search; keeps the pages asked.</summary>
    internal sealed class FakeWiki : HttpMessageHandler
    {
        public readonly System.Collections.Concurrent.ConcurrentBag<string> Asked = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            if (query["list"] == "search")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { query = new { search = query["srsearch"]!.Contains("Duckov") ? new[] { new { title = "Escape from Duckov 2" }, new { title = "Escape from Duckov" } } : Array.Empty<object>() } }),
                });
            string title = query["page"] ?? "";
            Asked.Add(title);
            object page = title switch
            {
                "Escape from Duckov" => new { parse = new { wikitext = "{{Network/Multiplayer\n|local play           = false\n|lan play             = true\n|lan play players     = 4\n}}" } },
                "Prey" => new { parse = new { wikitext = "{{Network/Multiplayer\n|lan play             = false\n|lan play players     = \n}}" } },
                _ => new { error = new { code = "missingtitle" } },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page) });
        }
    }

    /// <summary>The ZeroTier controller's API: keeps the calls it receives (with the "zt-secret" token), makes networks.</summary>
    internal sealed class FakeZeroTier : HttpMessageHandler
    {
        public readonly List<string> Calls = new();
        private int networks;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.TryGetValues("X-ZT1-Auth", out var token) || token.Single() != "zt-secret")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            string path = request.RequestUri!.AbsolutePath;
            string call = $"{request.Method} {path} {(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken))}".TrimEnd();
            lock (Calls)
                Calls.Add(call);
            if (path == "/status")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { address = "c4576f0616" }) };
            if (request.Method == HttpMethod.Post && path == "/controller/network/c4576f0616______")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = $"c4576f061600000{Interlocked.Increment(ref networks)}" }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    /// <summary>The RomM-ComM bot's requests API: keeps what it receives, answers with the requester's list.</summary>
    internal sealed class FakeRequestsBot : HttpMessageHandler
    {
        public readonly List<string> Received = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Parameter != "bot-key")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            Received.Add($"{request.Method} {request.RequestUri!.PathAndQuery} {(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken))}");
            return request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { result = "created", request_id = 12 }) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new { id = 12, game_name = "Hades", status = "pending" } }) };
        }
    }

    /// <summary>RomM: one platform with one game, a cover, and the game's file sent in parts.</summary>
    internal sealed class FakeRomm : HttpMessageHandler
    {
        public static readonly byte[] Rom = System.Text.Encoding.ASCII.GetBytes("NES-ROM-CONTENT");
        /// <summary>How many times the NES games were listed.</summary>
        public int GamesListed;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Parameter != Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("viewer:pass")))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            string path = request.RequestUri!.PathAndQuery;
            if (path == "/api/roms?platform_ids=3&limit=10000&order_by=name")
                Interlocked.Increment(ref GamesListed);
            HttpResponseMessage answer = path switch
            {
                "/api/platforms" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new object[] { new { id = 3, slug = "nes", name = "Nintendo Entertainment System", display_name = "NES", rom_count = 1 }, new { id = 4, slug = "dos", name = "DOS", display_name = (string?)null, rom_count = 0 } }) },
                "/api/roms?platform_ids=3&limit=10000&order_by=name" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { items = new[] { new { id = 40, name = "Zelda", fs_name = "Zelda (E).nes", fs_size_bytes = Rom.Length, path_cover_large = "/assets/romm/resources/roms/3/40/cover/big.png?ts=1", url_cover = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1.jpg", summary = "A sword", has_multiple_files = false,
                    metadatum = new { genres = new[] { "Adventure" }, companies = new[] { "Nintendo", "Nintendo" }, first_release_date = 509328000000L, average_rating = 88.5, player_count = "1", game_modes = new[] { "Single player" } },
                    hltb_metadata = new { main_story = 32400 }, regions = new[] { "Europe" }, languages = new[] { "French" },
                    merged_screenshots = new[] { "/assets/romm/resources/roms/3/40/screenshots/0.jpg", "/assets/romm/resources/roms/3/40/../../9/secret.jpg" },
                    path_manual = "roms/3/40/manual/40.pdf", youtube_video_id = "abc123", ss_metadata = new { box2d_url = "https://example?devpassword=secret" } } } }) },
                "/api/collections/virtual?type=collection" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new object[] { new { name = "The Legend of Zelda", rom_ids = new[] { 40, 41 } }, new { name = "Alone", rom_ids = new[] { 40 } } }) },
                "/api/roms?order_by=id&order_dir=desc&limit=40" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { items = new[] { new { id = 40, name = "Zelda", platform_slug = "nes", platform_display_name = "NES", path_cover_large = "/assets/romm/resources/roms/3/40/cover/big.png?ts=1", created_at = "2026-10-20T18:00:00+00:00" } } }) },
                "/api/roms/40" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 40, fs_name = "Zelda (E).nes" }) },
                // A game in a folder: RomM names the file it sends
                "/api/roms/41" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 41, fs_name = "Bleach [RBLP8P]" }) },
                "/api/roms/41/content/Bleach%20%5BRBLP8P%5D" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Rom) { Headers = { ContentDisposition = new("attachment") { FileNameStar = "RBLP8P.wbfs" } } } },
                // A PS3 game's folder: RomM zips it as it sends it
                "/api/roms/43" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 43, fs_name = "Infamous", has_nested_single_file = true }) },
                "/api/roms/43/content/Infamous" => Part(request),
                "/api/firmware?platform_id=3" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new object[] { new { id = 7, file_name = "scph5501.bin", file_size_bytes = 4, missing_from_fs = false }, new { id = 8, file_name = "gone.bin", file_size_bytes = 4, missing_from_fs = true } }) },
                "/api/firmware/7" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 7, file_name = "scph5501.bin" }) },
                "/api/firmware/7/content/scph5501.bin" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }) },
                "/api/roms/40/content/Zelda%20%28E%29.nes" => Part(request),
                "/assets/romm/resources/roms/3/40/cover/big.png" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 0x89, 0x50 }) { Headers = { ContentType = new("image/png") } } },
                _ => new(HttpStatusCode.NotFound),
            };
            return Task.FromResult(answer);
        }

        private static HttpResponseMessage Part(HttpRequestMessage request)
        {
            long from = request.Headers.Range?.Ranges.First().From ?? 0;
            var answer = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(Rom[(int)from..]) };
            answer.Headers.AcceptRanges.Add("bytes");
            if (from > 0)
                answer.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, Rom.Length - 1, Rom.Length);
            return answer;
        }
    }

    public sealed class UploaderTests : IDisposable
    {
        private readonly string files = Path.Combine(Path.GetTempPath(), $"gv-uploader-{Guid.NewGuid():N}");
        private readonly string profiles = Path.Combine(Path.GetTempPath(), $"gv-profiles-{Guid.NewGuid():N}");
        private readonly string saves = Path.Combine(Path.GetTempPath(), $"gv-saves-{Guid.NewGuid():N}");
        private readonly string remote = Path.Combine(Path.GetTempPath(), $"gv-remote-{Guid.NewGuid():N}");
        private readonly string retroSaves = Path.Combine(Path.GetTempPath(), $"gv-retro-saves-{Guid.NewGuid():N}");
        private readonly string retroPlayers = Path.Combine(Path.GetTempPath(), $"gv-retro-players-{Guid.NewGuid():N}");
        private readonly string rooms = Path.Combine(Path.GetTempPath(), $"gv-rooms-{Guid.NewGuid():N}");
        private readonly string reports = Path.Combine(Path.GetTempPath(), $"gv-reports-{Guid.NewGuid():N}");
        private readonly FakeZeroTier zeroTier = new();
        private readonly FakeWiki wiki = new();
        private readonly FakeGameVault gameVault = new();
        private readonly FakeRequestsBot bot = new();
        private readonly FakeRomm romm = new();
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
            RommProxy.ClearCache();
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("GAMEVAULT_URL", "http://gamevault.test");
                host.UseSetting("FILES_DIRECTORY", files);
                host.UseSetting("PROFILES_DIRECTORY", profiles);
                host.UseSetting("SAVEFILES_DIRECTORY", saves);
                host.UseSetting("REMOTE_INSTALLS_DIRECTORY", remote);
                host.UseSetting("RETRO_SAVES_DIRECTORY", retroSaves);
                host.UseSetting("RETRO_PLAYERS_DIRECTORY", retroPlayers);
                host.UseSetting("ROMM_COMM_URL", "http://romm-sync.test:8095");
                host.UseSetting("ROMM_COMM_API_KEY", "bot-key");
                host.UseSetting("ROMM_URL", "http://romm.test:8080");
                host.UseSetting("ROMM_USERNAME", "viewer");
                host.UseSetting("ROMM_PASSWORD", "pass");
                host.UseSetting("ROMM_PUBLIC_URL", "https://romm.example.com/");
                host.UseSetting("ZEROTIER_CONTROLLER_URL", "http://zerotier.test:9993");
                host.UseSetting("ZEROTIER_API_SECRET", "zt-secret");
                host.UseSetting("ROOMS_DIRECTORY", rooms);
                host.UseSetting("REPORTS_DIRECTORY", reports);
                host.ConfigureServices(services =>
                {
                    services.AddHttpClient<AdminCheck>().ConfigurePrimaryHttpMessageHandler(() => gameVault);
                    services.AddHttpClient<GameVaultProxy>().ConfigurePrimaryHttpMessageHandler(() => gameVault);
                    services.AddHttpClient<GameRequestsProxy>().ConfigurePrimaryHttpMessageHandler(() => bot);
                    services.AddHttpClient<RommProxy>().ConfigurePrimaryHttpMessageHandler(() => romm);
                    services.AddHttpClient<ZeroTierController>().ConfigurePrimaryHttpMessageHandler(() => zeroTier);
                    services.AddHttpClient<PcGamingWiki>().ConfigurePrimaryHttpMessageHandler(() => wiki);
                });
            });
        }

        public void Dispose()
        {
            factory.Dispose();
            try { Directory.Delete(files, true); } catch { }
            try { Directory.Delete(profiles, true); } catch { }
            try { Directory.Delete(saves, true); } catch { }
            try { Directory.Delete(remote, true); } catch { }
            try { Directory.Delete(retroSaves, true); } catch { }
            try { Directory.Delete(retroPlayers, true); } catch { }
            try { Directory.Delete(rooms, true); } catch { }
            try { Directory.Delete(reports, true); } catch { }
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
            // Where the players link their RomM account
            Assert.Equal("https://romm.example.com", status.RootElement.GetProperty("rommUrl").GetString());
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
        public async Task GameRequests_GoToTheBot_WithThePlayerTheServiceChecked()
        {
            var player = Client("user");
            var asked = await player.PostAsJsonAsync("/game-requests", new { game = " Hades ", details = "GOG" });
            Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
            // The requester is the signed-in player, whatever the page says
            Assert.Contains("\"requester\":{\"id\":2,\"name\":\"player\"}", bot.Received[0]);
            Assert.Contains("\"game\":\"Hades\"", bot.Received[0]);

            using JsonDocument mine = JsonDocument.Parse(await player.GetStringAsync("/game-requests"));
            Assert.Equal("Hades", mine.RootElement[0].GetProperty("game_name").GetString());
            Assert.StartsWith("GET /api/requests?requester_id=2&requester_name=player", bot.Received[1]);

            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/game-requests", new { game = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/game-requests")).StatusCode);
        }

        [Fact]
        public async Task AProblemReport_IsSeenByTheAdministrators_ThenResolved()
        {
            var player = Client("user");
            var sent = await player.PostAsJsonAsync("/reports", new { kind = "retro", gameId = 12, platform = "ps2", title = " God of War ", category = "match", details = "It is the first one", context = "1.0 · Windows 11" });
            Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
            string id = JsonDocument.Parse(await sent.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
            // The reporter is the signed-in player, whatever the client says
            using (JsonDocument all = JsonDocument.Parse(await Client("admin").GetStringAsync("/reports")))
            {
                JsonElement report = all.RootElement[0];
                Assert.Equal("player", report.GetProperty("username").GetString());
                Assert.Equal("God of War", report.GetProperty("title").GetString());
                Assert.Equal(JsonValueKind.Null, report.GetProperty("resolvedAt").ValueKind);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsync($"/reports/{id}/resolve", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Client("admin").PostAsync($"/reports/{id}/resolve", null)).StatusCode);
            // The player sees theirs, resolved
            using (JsonDocument mine = JsonDocument.Parse(await player.GetStringAsync("/reports")))
                Assert.Equal("admin", mine.RootElement[0].GetProperty("resolvedBy").GetString());

            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/reports", new { kind = "pc", gameId = 3, title = "Hades", category = "other" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/reports", new { kind = "pc", gameId = 3, title = "Hades", category = "broken" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Client("admin").PostAsync("/reports/nope/resolve", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/reports")).StatusCode);
        }

        [Fact]
        public async Task AHiddenGame_IsSeenByTheAdministrators_Only()
        {
            var player = Client("user");
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/activity/me", new { gameId = 6 })).StatusCode);
            Assert.Equal("[{\"user_id\":2,\"state\":\"PLAYING\",\"game_id\":6}]", await Client("admin").GetStringAsync("/activity/hidden"));
            Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/activity/hidden")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).PutAsJsonAsync("/activity/me", new { gameId = 6 })).StatusCode);
            // Stopped, or shown to all again
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/activity/me", new { gameId = (int?)null })).StatusCode);
            Assert.Equal("[]", await Client("admin").GetStringAsync("/activity/hidden"));
            // A client gone without saying it: forgotten
            var store = new HiddenPlayStore();
            store.Set(2, 6, DateTimeOffset.UtcNow - HiddenPlayStore.Fresh);
            Assert.Empty(store.Now(DateTimeOffset.UtcNow));
        }

        [Fact]
        public async Task RemoteInstalls_GoToThePCTheyAreAskedFor_AndKeepTheirOutcome()
        {
            var player = Client("user");
            // Two PCs of the player are open
            Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsJsonAsync("/remote/devices", new { id = "salon-0001", name = "PC-SALON" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsJsonAsync("/remote/devices", new { id = "bureau-0001", name = "PC-BUREAU" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/remote/devices", new { id = "../x", name = "bad" })).StatusCode);
            using (JsonDocument devices = JsonDocument.Parse(await player.GetStringAsync("/remote/devices")))
            {
                Assert.Equal(new[] { "PC-BUREAU", "PC-SALON" }, devices.RootElement.EnumerateArray().Select(d => d.GetProperty("name").GetString()).Order());
                Assert.All(devices.RootElement.EnumerateArray(), d => Assert.True(d.GetProperty("online").GetBoolean()));
            }
            // The administrators see when the player last had the client open; the players do not
            using (JsonDocument seen = JsonDocument.Parse(await Client("admin").GetStringAsync("/users/last-seen")))
                Assert.True(DateTime.UtcNow - seen.RootElement.GetProperty("2").GetDateTime().ToUniversalTime() < TimeSpan.FromMinutes(1));
            Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/users/last-seen")).StatusCode);

            Assert.Equal(HttpStatusCode.Created, (await player.PostAsJsonAsync("/remote/installs", new { gameId = 6, title = "Prey", deviceId = "salon-0001" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsJsonAsync("/remote/installs", new { gameId = 6, title = "Prey", deviceId = "salon-0001" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/remote/installs", new { gameId = 6, title = "Prey", deviceId = "unknown-0001" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/remote/installs", new { gameId = 0, title = "", deviceId = "salon-0001" })).StatusCode);

            // Only the PC it was asked for takes it; another player sees nothing
            Assert.Equal("[]", await player.GetStringAsync("/remote/installs?device=bureau-0001"));
            string id;
            using (JsonDocument mine = JsonDocument.Parse(await player.GetStringAsync("/remote/installs?device=salon-0001")))
                id = Assert.Single(mine.RootElement.EnumerateArray()).GetProperty("id").GetString()!;
            Assert.Equal("[]", await Client("admin").GetStringAsync("/remote/installs"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/remote/installs")).StatusCode);

            // The PC says it failed: no longer to take, still shown with the reason
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync($"/remote/installs/{id}", new { status = "done" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync($"/remote/installs/{id}", new { status = "failed", message = "Not enough space" })).StatusCode);
            Assert.Equal("[]", await player.GetStringAsync("/remote/installs?device=salon-0001"));
            using (JsonDocument all = JsonDocument.Parse(await player.GetStringAsync("/remote/installs")))
            {
                JsonElement entry = Assert.Single(all.RootElement.EnumerateArray());
                Assert.Equal(("failed", "Not enough space", "PC-SALON"), (entry.GetProperty("status").GetString(), entry.GetProperty("message").GetString(), entry.GetProperty("deviceName").GetString()));
            }
            Assert.Equal(HttpStatusCode.NotFound, (await Client("admin").PutAsJsonAsync($"/remote/installs/{id}", new { status = "started" })).StatusCode);

            // Asked again: a new request; dropped by the player
            Assert.Equal(HttpStatusCode.Created, (await player.PostAsJsonAsync("/remote/installs", new { gameId = 6, title = "Prey", deviceId = "salon-0001" })).StatusCode);
            using (JsonDocument again = JsonDocument.Parse(await player.GetStringAsync("/remote/installs")))
                id = Assert.Single(again.RootElement.EnumerateArray()).GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.NoContent, (await player.DeleteAsync($"/remote/installs/{id}")).StatusCode);
            Assert.Equal("[]", await player.GetStringAsync("/remote/installs"));
        }

        [Fact]
        public async Task RetroGames_ComeFromRomm_ForSignedInPlayers()
        {
            var player = Client("user");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/retro/platforms")).StatusCode);

            // Only the platforms with games
            using (JsonDocument platforms = JsonDocument.Parse(await player.GetStringAsync("/retro/platforms")))
            {
                JsonElement nes = Assert.Single(platforms.RootElement.EnumerateArray());
                Assert.Equal((3, "nes", "NES"), (nes.GetProperty("id").GetInt32(), nes.GetProperty("slug").GetString(), nes.GetProperty("name").GetString()));
            }
            using (JsonDocument games = JsonDocument.Parse(await player.GetStringAsync("/retro/platforms/3/games")))
            {
                JsonElement zelda = Assert.Single(games.RootElement.EnumerateArray());
                Assert.Equal(("Zelda", "Zelda (E).nes", "roms/3/40/cover/big.png"), (zelda.GetProperty("name").GetString(), zelda.GetProperty("fileName").GetString(), zelda.GetProperty("cover").GetString()));
                // Its cover's version: the same as long as its source is (an admin's new match changes it)
                Assert.Equal(RommProxy.Version("https://images.igdb.com/igdb/image/upload/t_cover_big/co1.jpg"), zelda.GetProperty("coverVersion").GetString());
                Assert.NotEqual(RommProxy.Version("https://images.igdb.com/igdb/image/upload/t_cover_big/co2.jpg"), zelda.GetProperty("coverVersion").GetString());
                Assert.Equal((509328000000L, 88.5, "Adventure", "Nintendo", 32400L), (zelda.GetProperty("released").GetInt64(), zelda.GetProperty("rating").GetDouble(),
                    zelda.GetProperty("genres")[0].GetString(), zelda.GetProperty("companies")[0].GetString(), zelda.GetProperty("mainStory").GetInt64()));
                // Only RomM's own media paths; nothing of the providers' metadata (their keys) goes out
                Assert.Equal(new[] { "roms/3/40/screenshots/0.jpg" }, zelda.GetProperty("screenshots").EnumerateArray().Select(s => s.GetString()));
                Assert.Equal(("roms/3/40/manual/40.pdf", "abc123"), (zelda.GetProperty("manual").GetString(), zelda.GetProperty("video").GetString()));
                Assert.DoesNotContain("devpassword", games.RootElement.GetRawText());
                Assert.Single(zelda.GetProperty("companies").EnumerateArray());
            }
            // Asked again, by any player, even at the same time: RomM is not asked again for a while
            string[] again = await Task.WhenAll(player.GetStringAsync("/retro/platforms/3/games"), Client("admin").GetStringAsync("/retro/platforms/3/games"));
            Assert.All(again, list => Assert.Contains("Zelda (E).nes", list));
            Assert.Equal(1, romm.GamesListed);

            // Covers only: no other path of RomM goes through
            Assert.Equal("image/png", (await player.GetAsync("/retro/media/roms/3/40/cover/big.png")).Content.Headers.ContentType!.MediaType);
            Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync("/retro/media/roms/3/40/../../../api/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync("/retro/media/roms/3/40/manual/40.exe")).StatusCode);

            // The file, whole or from where a download stopped
            HttpResponseMessage whole = await player.GetAsync("/retro/games/40/content");
            Assert.Equal(FakeRomm.Rom, await whole.Content.ReadAsByteArrayAsync());
            Assert.Equal("Zelda (E).nes", whole.Content.Headers.ContentDisposition!.FileNameStar);
            using var rest = new HttpRequestMessage(HttpMethod.Get, "/retro/games/40/content");
            rest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(4, null);
            HttpResponseMessage part = await player.SendAsync(rest);
            Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
            Assert.Equal(FakeRomm.Rom[4..], await part.Content.ReadAsByteArrayAsync());
            Assert.Equal(4, part.Content.Headers.ContentRange!.From);
            Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync("/retro/games/42/content")).StatusCode);
            Assert.Equal("RBLP8P.wbfs", (await player.GetAsync("/retro/games/41/content")).Content.Headers.ContentDisposition!.FileNameStar);
            // A zip RomM makes while sending (a folder): never asked in parts, which RomM can not answer in time
            using var zipPart = new HttpRequestMessage(HttpMethod.Get, "/retro/games/43/content");
            zipPart.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(4, null);
            HttpResponseMessage zipped = await player.SendAsync(zipPart);
            Assert.Equal(HttpStatusCode.OK, zipped.StatusCode);
            Assert.Equal(FakeRomm.Rom, await zipped.Content.ReadAsByteArrayAsync());

            // The BIOS of a platform: the ones RomM still has
            using (JsonDocument firmware = JsonDocument.Parse(await player.GetStringAsync("/retro/platforms/3/firmware")))
                Assert.Equal("scph5501.bin", Assert.Single(firmware.RootElement.EnumerateArray()).GetProperty("fileName").GetString());
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await player.GetByteArrayAsync("/retro/firmware/7/content"));
        }

        [Fact]
        public async Task RetroPlays_AddUp_AndShowInTheActivityOfAllPlayers()
        {
            var player = Client("user");
            var admin = Client("admin");
            object Play(int minutes) => new { romId = 40, platform = "nes", platformName = "NES", name = "Zelda", cover = "roms/3/40/cover/big.png", minutes };
            Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsJsonAsync("/retro/players/me/plays", Play(25))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsJsonAsync("/retro/players/me/plays", Play(5))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/retro/players/me/plays", Play(0))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/retro/players/me/plays", new { romId = 1, platform = "../x", name = "x", minutes = 3 })).StatusCode);

            using (JsonDocument me = JsonDocument.Parse(await player.GetStringAsync("/retro/players/me")))
            {
                JsonElement zelda = Assert.Single(me.RootElement.GetProperty("plays").EnumerateArray());
                Assert.Equal((30, "nes:40"), (zelda.GetProperty("minutes").GetInt32(), zelda.GetProperty("key").GetString()));
            }
            // Favorites: only valid keys are kept
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/retro/players/me/favorites", new[] { "nes:40", "bad key", "nes:40" })).StatusCode);
            using (JsonDocument me = JsonDocument.Parse(await player.GetStringAsync("/retro/players/me")))
                Assert.Equal(new[] { "nes:40" }, me.RootElement.GetProperty("favorites").EnumerateArray().Select(f => f.GetString()));

            // Every player sees the others' plays (profile, recent activity)
            using (JsonDocument theirs = JsonDocument.Parse(await admin.GetStringAsync("/retro/players/2")))
                Assert.Single(theirs.RootElement.GetProperty("plays").EnumerateArray());
            using (JsonDocument activity = JsonDocument.Parse(await admin.GetStringAsync("/retro/activity")))
            {
                JsonElement recent = Assert.Single(activity.RootElement.EnumerateArray());
                Assert.Equal(("player", "Zelda", 30), (recent.GetProperty("username").GetString(), recent.GetProperty("name").GetString(), recent.GetProperty("minutes").GetInt32()));
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/retro/activity")).StatusCode);
            // Who played a game: its page shows them
            using (JsonDocument who = JsonDocument.Parse(await admin.GetStringAsync("/retro/games/nes/40/players")))
                Assert.Equal(("player", 30), (Assert.Single(who.RootElement.EnumerateArray()).GetProperty("username").GetString(), who.RootElement[0].GetProperty("minutes").GetInt32()));
            Assert.Equal("[]", await admin.GetStringAsync("/retro/games/nes/41/players"));

            // The newest games of RomM, for the notification
            using (JsonDocument latest = JsonDocument.Parse(await player.GetStringAsync("/retro/latest")))
            {
                JsonElement newest = Assert.Single(latest.RootElement.EnumerateArray());
                Assert.Equal(("nes", "roms/3/40/cover/big.png", "2026-10-20T18:00:00+00:00"), (newest.GetProperty("platform").GetString(), newest.GetProperty("cover").GetString(), newest.GetProperty("addedAt").GetString()));
            }
        }

        [Fact]
        public async Task RetroAchievements_OfEachPlayer_ShowOnTheirProfileAndInTheActivity()
        {
            var player = Client("user");
            var admin = Client("admin");
            object Game(int earned, params object[] unlocks) => new { name = "Zelda", platformName = "NES", cover = "roms/3/40/cover/big.png", earned, total = 50, unlocks };
            var first = new { title = "First Sword", badge = "https://media.retroachievements.org/Badge/1.png", date = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc) };
            // A badge elsewhere than RetroAchievements is not shown to the others
            var second = new { title = "Triforce", badge = "https://tracker.example.com/pixel.png", date = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc) };
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/retro/players/me/achievements/nes/40", Game(2, first, second))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync("/retro/players/me/achievements/nes/40", Game(51))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.PutAsJsonAsync("/retro/players/me/achievements/x..y/40", Game(1))).StatusCode);

            using (JsonDocument theirs = JsonDocument.Parse(await admin.GetStringAsync("/retro/players/2")))
            {
                JsonElement zelda = theirs.RootElement.GetProperty("achievements").GetProperty("nes:40");
                Assert.Equal((2, 50, 2), (zelda.GetProperty("earned").GetInt32(), zelda.GetProperty("total").GetInt32(), zelda.GetProperty("unlocks").GetArrayLength()));
            }
            using (JsonDocument activity = JsonDocument.Parse(await admin.GetStringAsync("/retro/activity/achievements")))
            {
                var unlocks = activity.RootElement.EnumerateArray().ToList();
                Assert.Equal(new[] { "Triforce", "First Sword" }, unlocks.Select(u => u.GetProperty("title").GetString()));
                Assert.Equal(("player", "Zelda", "nes", 40), (unlocks[0].GetProperty("username").GetString(), unlocks[0].GetProperty("name").GetString(), unlocks[0].GetProperty("platform").GetString(), unlocks[0].GetProperty("romId").GetInt32()));
                Assert.Equal(JsonValueKind.Null, unlocks[0].GetProperty("badge").ValueKind);
                Assert.Equal("https://media.retroachievements.org/Badge/1.png", unlocks[1].GetProperty("badge").GetString());
            }
            // None earned any more (account changed): the game goes
            Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/retro/players/me/achievements/nes/40", Game(0))).StatusCode);
            Assert.Equal("[]", await admin.GetStringAsync("/retro/activity/achievements"));

            // The series of games, from RomM (one game alone is no series)
            using JsonDocument series = JsonDocument.Parse(await player.GetStringAsync("/retro/series"));
            JsonElement zeldas = Assert.Single(series.RootElement.EnumerateArray());
            Assert.Equal("The Legend of Zelda", zeldas.GetProperty("name").GetString());
            Assert.Equal(new long[] { 40, 41 }, zeldas.GetProperty("romIds").EnumerateArray().Select(i => i.GetInt64()));
        }

        [Fact]
        public async Task RetroDownloads_KeepTheirBytes_UnderASpeedLimit()
        {
            using var limited = new HttpRequestMessage(HttpMethod.Get, "/retro/games/40/content");
            limited.Headers.Add("X-Download-Speed-Limit", "1");
            Assert.Equal(FakeRomm.Rom, await (await Client("user").SendAsync(limited)).Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task RetroSaves_AreKeptPerPlayer()
        {
            var player = Client("user");
            var admin = Client("admin");
            Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync("/retro/saves/retroarch/info")).StatusCode);

            byte[] zip = { 0x50, 0x4B, 3, 4, 9 };
            HttpResponseMessage put = await player.PutAsync("/retro/saves/retroarch", new ByteArrayContent(zip));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            long savedAt = JsonDocument.Parse(await put.Content.ReadAsStringAsync()).RootElement.GetProperty("savedAt").GetInt64();
            using (JsonDocument info = JsonDocument.Parse(await player.GetStringAsync("/retro/saves/retroarch/info")))
                Assert.Equal(savedAt, info.RootElement.GetProperty("savedAt").GetInt64());
            Assert.Equal(zip, await player.GetByteArrayAsync("/retro/saves/retroarch"));

            // Saved again: the previous one goes to the history, and can be taken back
            await Task.Delay(20);
            Assert.Equal(HttpStatusCode.OK, (await player.PutAsync("/retro/saves/retroarch", new ByteArrayContent(new byte[] { 0x50, 0x4B, 3, 4, 10 }))).StatusCode);
            using (JsonDocument history = JsonDocument.Parse(await player.GetStringAsync("/retro/saves/retroarch/history")))
            {
                JsonElement older = Assert.Single(history.RootElement.EnumerateArray());
                Assert.Equal(savedAt, older.GetProperty("savedAt").GetInt64());
                Assert.Equal(zip, await player.GetByteArrayAsync($"/retro/saves/retroarch/history/{older.GetProperty("name").GetString()}"));
            }
            Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync("/retro/saves/retroarch/history/..%2F..%2Fother.zip")).StatusCode);

            // Another player has their own; a bad name or no sign-in gets nothing
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/retro/saves/retroarch")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await player.GetAsync("/retro/saves/..%2Fprofiles")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/retro/saves/retroarch")).StatusCode);
        }

        [Fact]
        public async Task TheWebPage_IsAlsoServedUnderTheGameVaultSite()
        {
            var noRedirect = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var moved = await noRedirect.GetAsync("/sanctuary?install=6");
            Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);
            Assert.Equal("/sanctuary/?install=6", moved.Headers.Location!.OriginalString);
            Assert.Contains("app.js", await Client(null).GetStringAsync("/sanctuary/"));
            Assert.Equal(HttpStatusCode.OK, (await Client(null).GetAsync("/sanctuary/inject.js")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Client(null).GetAsync("/sanctuary/status")).StatusCode);
            // The game asked from a game page of the GameVault site
            Assert.Contains("\"Prey\"", await Client("user").GetStringAsync("/sanctuary/games/6"));
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
            // The invitation, without signing in
            string invite = await Client(null).GetStringAsync("/invite");
            Assert.Contains("Rejoins SanctuaryVault", invite);
            Assert.Contains("releases/latest/download/GameVault-win-Setup.exe", invite);

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

        /// <summary>
        /// A LAN room: made with its ZeroTier network (broadcast on) and its owner's PC let in; a private room is joined
        /// by its code only; the 8th player fills it; leaving shuts the PC out, passes the room on, then deletes the network.
        /// </summary>
        [Fact]
        public async Task ARoom_LetsItsPlayersPcsInItsNetwork_ThenGoesWithTheLast()
        {
            HttpClient owner = Client("user"), other = Client("admin");
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/rooms", new { name = "Soirée", nodeId = "not-a-node" })).StatusCode);
            var made = await owner.PostAsJsonAsync("/rooms", new { name = "Soirée AoE", gameId = 6, gameTitle = "Age of Empires II", @public = false, nodeId = "aaaaaaaa01" });
            Assert.Equal(HttpStatusCode.Created, made.StatusCode);
            string id, code;
            using (JsonDocument room = JsonDocument.Parse(await made.Content.ReadAsStringAsync()))
            {
                id = room.RootElement.GetProperty("id").GetString()!;
                code = room.RootElement.GetProperty("code").GetString()!;
                Assert.Equal("10.147.1.1", Assert.Single(room.RootElement.GetProperty("members").EnumerateArray()).GetProperty("ip").GetString());
            }
            Assert.Equal("c4576f0616000001", id);
            Assert.Contains(zeroTier.Calls, c => c.StartsWith("POST /controller/network/c4576f0616______") && c.Contains("\"enableBroadcast\":true") && c.Contains("10.147.1.0/24"));
            Assert.Contains("POST /controller/network/c4576f0616000001/member/aaaaaaaa01 {\"authorized\":true,\"ipAssignments\":[\"10.147.1.1\"]}", zeroTier.Calls);

            // Private: the others do not see it, nor join it by its id; by its code (any case, any dash) they do
            Assert.Equal("[]", await other.GetStringAsync("/rooms"));
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/rooms/join", new { roomId = id, nodeId = "bbbbbbbb02" })).StatusCode);
            var joined = await other.PostAsJsonAsync("/rooms/join", new { code = code[..3].ToLowerInvariant() + "-" + code[3..], nodeId = "bbbbbbbb02" });
            Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
            Assert.Contains("POST /controller/network/c4576f0616000001/member/bbbbbbbb02 {\"authorized\":true,\"ipAssignments\":[\"10.147.1.2\"]}", zeroTier.Calls);
            using (JsonDocument mine = JsonDocument.Parse(await other.GetStringAsync("/rooms")))
                Assert.Equal(2, Assert.Single(mine.RootElement.EnumerateArray()).GetProperty("members").GetArrayLength());

            // Full at 8
            for (int n = 10; n < 16; n++)
                Assert.Equal(HttpStatusCode.OK, (await Client($"p{n}").PostAsJsonAsync("/rooms/join", new { code, nodeId = $"cccccccc{n}" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Client("p16").PostAsJsonAsync("/rooms/join", new { code, nodeId = "cccccccc16" })).StatusCode);

            // Only the owner chooses the game; the owner leaves: shut out, the room passes on
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync($"/rooms/{id}/game", new { gameId = 5, gameTitle = "Duckov" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/rooms/{id}/leave", null)).StatusCode);
            Assert.Contains("POST /controller/network/c4576f0616000001/member/aaaaaaaa01 {\"authorized\":false}", zeroTier.Calls);
            Assert.Contains("DELETE /controller/network/c4576f0616000001/member/aaaaaaaa01", zeroTier.Calls);
            Assert.Equal("[]", await owner.GetStringAsync("/rooms"));
            Assert.Equal(HttpStatusCode.NoContent, (await other.PutAsJsonAsync($"/rooms/{id}/game", new { gameId = 5, gameTitle = "Duckov" })).StatusCode);

            // The last one leaves: the network goes
            for (int n = 10; n < 16; n++)
                Assert.Equal(HttpStatusCode.NoContent, (await Client($"p{n}").PostAsync($"/rooms/{id}/leave", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsync($"/rooms/{id}/leave", null)).StatusCode);
            Assert.Contains("DELETE /controller/network/c4576f0616000001", zeroTier.Calls);
            Assert.Equal("[]", await other.GetStringAsync("/rooms"));

            // A public room: seen and joined by its id
            using (JsonDocument room = JsonDocument.Parse(await (await owner.PostAsJsonAsync("/rooms", new { name = "Ouverte", @public = true, nodeId = "aaaaaaaa01" })).Content.ReadAsStringAsync()))
                id = room.RootElement.GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/rooms/join", new { roomId = id, nodeId = "bbbbbbbb02" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/rooms")).StatusCode);
        }

        [Fact]
        public async Task TheGamesPlayableInLan_AreFoundOnPcGamingWiki_ThenAnAdministratorDecides()
        {
            // The library read on the wiki in the background, each game once, by its metadata's title: "Escape From Duckov" has
            // no page, the wiki's search finds "Escape from Duckov" (not its sequel), in LAN with 4 players
            string found = await Client("admin").GetStringAsync("/lan-games");
            for (int i = 0; i < 200 && (found != """[{"id":5,"players":4}]""" || wiki.Asked.Count < 3); i++)
            {
                await Task.Delay(50);
                found = await Client("admin").GetStringAsync("/lan-games");
            }
            Assert.Equal("""[{"id":5,"players":4}]""", found);
            Assert.Equal(new[] { "Escape From Duckov", "Escape from Duckov", "Prey" }, wiki.Asked.Order(StringComparer.Ordinal));

            // Only an administrator decides, and wins over the wiki both ways
            Assert.Equal(HttpStatusCode.Forbidden, (await Client("user").PutAsJsonAsync("/lan-games/6", new { lan = true })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Client("admin").PutAsJsonAsync("/lan-games/6", new { lan = true })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Client("admin").PutAsJsonAsync("/lan-games/5", new { lan = false })).StatusCode);
            Assert.Equal("""[{"id":6,"players":null}]""", await Client("user").GetStringAsync("/lan-games"));
        }
    }
}
