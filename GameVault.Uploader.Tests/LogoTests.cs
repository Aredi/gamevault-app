using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameVault.Uploader.Tests
{
    public sealed partial class UploaderTests
    {
        private async Task<byte[]?> LogoAsync(string path, string token = "user")
        {
            using HttpResponseMessage response = await Client(token).GetAsync(path);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            return await response.Content.ReadAsByteArrayAsync();
        }

        [Fact]
        public async Task ARetroGamesLogo_ComesFromRomm_ThenFromSteamGridDb_AndIsFetchedOnce()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/retro/games/45/logo")).StatusCode);

            // ScreenScraper's address, which carries RomM's keys: fetched here, the player gets the picture only
            Assert.Equal(FakeLogos.Png(45), await LogoAsync("/retro/games/45/logo"));
            Assert.Equal(new[] { "neoclone.screenscraper.fr/api2/mediaJeu.php" }, logoSites.Asked);
            // Kept: not asked a second time
            Assert.Equal(FakeLogos.Png(45), await LogoAsync("/retro/games/45/logo", "admin"));
            Assert.Single(logoSites.Asked);

            // The picture RomM kept itself comes first
            Assert.Equal(FakeLogos.Png(46), await LogoAsync("/retro/games/46/logo"));
            Assert.Single(logoSites.Asked);

            // No logo in RomM: SteamGridDB's, by the id RomM matched (its first logo is not a picture: the next one)
            logoSites.Asked.Clear();
            Assert.Equal(FakeLogos.Png(777 % 256), await LogoAsync("/retro/games/47/logo"));
            Assert.Equal(new[] { "www.steamgriddb.com/api/v2/logos/game/777", "cdn2.steamgriddb.com/logo/broken.png", "cdn2.steamgriddb.com/logo/777.png" }, logoSites.Asked);
            // Or by the game's name
            logoSites.Asked.Clear();
            Assert.Equal(FakeLogos.Png(888 % 256), await LogoAsync("/retro/games/48/logo"));
            Assert.Equal("www.steamgriddb.com/api/v2/search/autocomplete/Metroid", logoSites.Asked[0]);

            // A game nobody has a logo for; an address that is not ScreenScraper's and a path out of the game's folder are not followed
            logoSites.Asked.Clear();
            Assert.Null(await LogoAsync("/retro/games/49/logo"));
            Assert.Equal(new[] { "www.steamgriddb.com/api/v2/search/autocomplete/Unknown%20Game" }, logoSites.Asked);
            // Remembered for a week, then asked again
            Assert.Null(await LogoAsync("/retro/games/49/logo"));
            Assert.Single(logoSites.Asked);
            var store = factory.Services.GetRequiredService<GameLogos>();
            Assert.Null(await store.RetroAsync(49, default));
            Assert.Single(logoSites.Asked);
            store.Clock = () => DateTime.UtcNow.AddDays(8);
            Assert.Null(await store.RetroAsync(49, default));
            Assert.Equal(2, logoSites.Asked.Count);
            // A rom RomM does not have
            Assert.Null(await LogoAsync("/retro/games/999/logo"));
        }

        [Fact]
        public async Task ARetroGamesHero_ComesFromSteamGridDb_ByTheIdRommMatched_AndIsFetchedOnce()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/retro/games/47/hero")).StatusCode);
            logoSites.Asked.Clear();
            Assert.Equal(FakeLogos.Png(777 % 256), await LogoAsync("/retro/games/47/hero"));
            Assert.Equal(new[] { "www.steamgriddb.com/api/v2/heroes/game/777", "cdn2.steamgriddb.com/hero/777.png" }, logoSites.Asked);
            // Kept apart from its logo, and not asked again
            Assert.Equal(FakeLogos.Png(777 % 256), await LogoAsync("/retro/games/47/hero"));
            Assert.Equal(2, logoSites.Asked.Count);
            // By the name when RomM matched none; a game nobody knows; a rom RomM does not have
            Assert.Equal(FakeLogos.Png(888 % 256), await LogoAsync("/retro/games/48/hero"));
            Assert.Null(await LogoAsync("/retro/games/49/hero"));
            Assert.Null(await LogoAsync("/retro/games/999/hero"));
        }

        [Fact]
        public async Task ALibraryGamesLogo_IsSearchedByTheTitleTheServerHas()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/logos/pc/8")).StatusCode);
            Assert.Equal(FakeLogos.Png(999 % 256), await LogoAsync("/logos/pc/8"));
            Assert.Equal("www.steamgriddb.com/api/v2/search/autocomplete/Hades", logoSites.Asked[0]);
            int asked = logoSites.Asked.Count;
            Assert.Equal(FakeLogos.Png(999 % 256), await LogoAsync("/logos/pc/8"));
            Assert.Equal(asked, logoSites.Asked.Count);
            // A game SteamGridDB does not know, a game the server does not have
            Assert.Null(await LogoAsync("/logos/pc/6"));
            Assert.Null(await LogoAsync("/logos/pc/12345"));
        }

        [Fact]
        public async Task NoKey_OrAKeySteamGridDbRefuses_IsNotRememberedAsNoLogo()
        {
            string folder = Path.Combine(Path.GetTempPath(), "gv-logos-" + Guid.NewGuid().ToString("N"));
            RommProxy romm = factory.Services.GetRequiredService<RommProxy>();
            GameLogos With(string? key) => new(new HttpClient(logoSites), new UploaderOptions { GameVaultUrl = "http://gamevault.test", FilesDirectory = folder, LogosDirectory = folder, SteamGridDbKey = key }, romm,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GameLogos>.Instance);
            try
            {
                // Before the key is given, and with a key SteamGridDB refuses: nothing is said of these games
                Assert.Null(await With(null).RetroAsync(47, default));
                Assert.Null(await With("wrong-key").RetroAsync(47, default));
                Assert.Null(await With("wrong-key").PcAsync(8, "Hades", default));
                Assert.Empty(Directory.Exists(folder) ? Directory.GetFiles(folder, "*.none") : Array.Empty<string>());

                // The right key: their logos at once, not a week later
                Assert.NotNull(await With("sgdb-key").RetroAsync(47, default));
                Assert.NotNull(await With("sgdb-key").PcAsync(8, "Hades", default));
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void OnlyPictures_AndOnlyScreenScrapersAddresses()
        {
            Assert.Equal("image/png", GameLogos.TypeOf(FakeLogos.Png(1)));
            Assert.Equal("image/jpeg", GameLogos.TypeOf(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
            Assert.Equal("image/webp", GameLogos.TypeOf("RIFF1234WEBPVP8 "u8));
            Assert.Null(GameLogos.TypeOf("<html>"u8));
            Assert.Null(GameLogos.TypeOf(ReadOnlySpan<byte>.Empty));
            Assert.True(GameLogos.IsScreenScraper("https://neoclone.screenscraper.fr/api2/mediaJeu.php?x=1"));
            Assert.True(GameLogos.IsScreenScraper("https://screenscraper.fr/a.png"));
            Assert.False(GameLogos.IsScreenScraper("http://neoclone.screenscraper.fr/a.png"));
            Assert.False(GameLogos.IsScreenScraper("https://screenscraper.fr.evil.example/a.png"));
            Assert.False(GameLogos.IsScreenScraper("https://evilscreenscraper.fr/a.png"));
            Assert.False(GameLogos.IsScreenScraper("file:///etc/passwd"));
        }
    }
}
