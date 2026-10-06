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
        private readonly FakeGameVault gameVault = new();
        private readonly WebApplicationFactory<Program> factory;

        public UploaderTests()
        {
            Directory.CreateDirectory(files);
            AdminCheck.ClearCache();
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("GAMEVAULT_URL", "http://gamevault.test");
                host.UseSetting("FILES_DIRECTORY", files);
                host.ConfigureServices(services => services.AddHttpClient<AdminCheck>().ConfigurePrimaryHttpMessageHandler(() => gameVault));
            });
        }

        public void Dispose()
        {
            factory.Dispose();
            try { Directory.Delete(files, true); } catch { }
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
    }
}
