using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GameVault.Uploader.Tests
{
    public sealed partial class UploaderTests
    {
        [Fact]
        public async Task ARecomp_IsKeptWithItsProgramAndItsArguments_ForAGameOfAnyConsole()
        {
            var admin = Client("admin");
            var recomp = new { name = "Zelda 64 Recompiled", console = "n64", kind = "recomp", windows = "https://github.com/x/Zelda64Recomp/releases/download/v1/Zelda64.zip", linux = (string?)null, mac = (string?)null, executable = (string?)"Zelda64Recompiled.exe", arguments = (string?)" --rom {rom} " };
            // Its program is named (only an AppImage starts itself), its arguments are one short line, its kind is known
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { executable = null })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { executable = "..\\evil.exe" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { arguments = "a\nb" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { arguments = new string('a', 301) })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { kind = "other" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { console = "Nintendo 64" })).StatusCode);
            // A fork stays a Switch emulator, without arguments
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { kind = "emulator" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp with { kind = null, console = "switch" })).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/retro/emulators/zelda64", recomp)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/retro/emulators/appimage", recomp with { windows = null, linux = "https://x/Zelda64-x86_64.AppImage", executable = null, arguments = null })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/retro/emulators/games/77", new { emulator = "zelda64" })).StatusCode);

            using JsonDocument seen = JsonDocument.Parse(await Client("user").GetStringAsync("/retro/emulators"));
            JsonElement kept = seen.RootElement.GetProperty("emulators").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "zelda64");
            Assert.Equal("recomp", kept.GetProperty("kind").GetString());
            Assert.Equal("n64", kept.GetProperty("console").GetString());
            Assert.Equal("Zelda64Recompiled.exe", kept.GetProperty("executable").GetString());
            Assert.Equal("--rom {rom}", kept.GetProperty("arguments").GetString());
            Assert.Equal("zelda64", seen.RootElement.GetProperty("games").GetProperty("77").GetString());
        }
    }
}
