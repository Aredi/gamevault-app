using gamevault.Models;
using Xunit;

namespace GameVault.UiTests
{
    /// <summary>
    /// Not a test: the fake server kept running for a device to sign in to it by hand (the Android app in the emulator,
    /// after "adb reverse"). Runs only with SV_DEV_SERVER_MINUTES set; its address is written to SV_DEV_SERVER_FILE.
    /// </summary>
    public class DevServer
    {
        [Fact]
        public async Task KeepsTheFakeServerRunning()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("SV_DEV_SERVER_MINUTES"), out int minutes))
                return;
            using var server = new FakeGameVaultServer(int.TryParse(Environment.GetEnvironmentVariable("SV_DEV_SERVER_PORT"), out int port) ? port : null);
            // A real NES game to play (SV_DEV_SERVER_NES: a .nes file, e.g. the 240p test suite)
            if (Environment.GetEnvironmentVariable("SV_DEV_SERVER_NES") is { Length: > 0 } nes)
                server.NesRom = File.ReadAllBytes(nes);
            server.Users[1] = FakeGameVaultServer.Admin;
            server.Users[2] = new User { ID = 2, Username = "Léa", Role = PERMISSION_ROLE.USER, Activated = true };
            server.Users[3] = new User { ID = 3, Username = "Hugo", Role = PERMISSION_ROLE.USER, Activated = true };
            server.ChatFrom(2, "Léa", "Salut ! Tu as essayé le nouveau Zelda ?");
            server.ChatFrom(3, "Hugo", "On se fait une partie ce soir ?");
            server.ChallengeState = new
            {
                week = "2026-W47",
                startsAt = DateTime.UtcNow.AddDays(-2),
                endsAt = DateTime.UtcNow.AddDays(5),
                challenge = new { kind = "retro", goal = 300, setBy = "admin" },
                board = new object[]
                {
                    new { userId = 2, username = "Léa", value = 320, doneAt = (DateTime?)DateTime.UtcNow.AddHours(-3) },
                    new { userId = 1, username = "admin", value = 90, doneAt = (DateTime?)null },
                },
                allTime = new object[] { new { userId = 2, username = "Léa", completed = 3, first = 2 } },
                past = Array.Empty<object>(),
            };
            if (Environment.GetEnvironmentVariable("SV_DEV_SERVER_FILE") is { Length: > 0 } file)
                File.WriteAllText(file, server.Url);
            await Task.Delay(TimeSpan.FromMinutes(minutes));
        }
    }
}
