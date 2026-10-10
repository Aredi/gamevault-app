using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using gamevault;
using gamevault.Models;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>
    /// The Playnite extension (Phalcode/gamevault-playnite-integration) talks to the client through the "GameVault" named
    /// pipe: one URI per connection, one line back. These requests are sent exactly as its PipelineHelper sends them.
    /// </summary>
    public class PlayniteScenarios
    {
        /// <summary>Like PipelineHelper.SendPipeMessage of the extension (UTF-8 with its byte order mark, a line back).</summary>
        private static async Task<string?> SendLikePlaynite(string message)
        {
            using var client = new NamedPipeClientStream("GameVault");
            await client.ConnectAsync(5000);
            using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(message);
            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            return await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        [AvaloniaFact]
        public async Task ThePlayniteExtension_GetsTheVersion_TheGames_AndTheInstalls()
        {
            var session = await TestSession.GetAsync();
            PipeServiceHandler.StartInstance();
            PipeServiceHandler.Instance!.IsReadyForCommands = true;
            Game game = session.Server.AddGame(9901, "Playnite Pilgrimage", GameType.WINDOWS_PORTABLE, new byte[64], "Playnite Pilgrimage (W_P).zip");
            string directory = Path.Combine(session.LibraryRoot, "GameVault", "Installations", "(9901)Playnite Pilgrimage");
            Directory.CreateDirectory(directory);
            InstallViewModel.Instance.InstalledGames.Add(new KeyValuePair<Game, string>(game, directory));
            try
            {
                // EnsureRunning: the extension waits for the version
                string? version = await Task.Run(() => SendLikePlaynite("gamevault://query?query=getappversion"));
                Assert.Equal(SettingsViewModel.Instance.Version, version);

                // Import: every game of the server, as base64 JSON
                string? all = await Task.Run(() => SendLikePlaynite("gamevault://query?query=getallgames"));
                Assert.False(string.IsNullOrEmpty(all));
                using JsonDocument games = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(all!)));
                Assert.Contains(games.RootElement.GetProperty("data").EnumerateArray(), g => g.GetProperty("id").GetInt32() == 9901 && g.GetProperty("title").GetString() == "Playnite Pilgrimage");

                // Install state and folder, for each game
                Assert.Equal("True", await Task.Run(() => SendLikePlaynite("gamevault://query?query=installed&gameid=9901")));
                Assert.Equal(directory, await Task.Run(() => SendLikePlaynite("gamevault://query?query=getinstalldirectory&gameid=9901")));
                Assert.True(bool.TryParse(await Task.Run(() => SendLikePlaynite("gamevault://query?query=installed&gameid=123456")), out bool other) && !other);
                Assert.Equal("True", await Task.Run(() => SendLikePlaynite("gamevault://query?query=exists&gameid=9901")));
            }
            finally
            {
                InstallViewModel.Instance.InstalledGames.Remove(InstallViewModel.Instance.InstalledGames.First(g => g.Key.ID == 9901));
            }
        }
    }
}
