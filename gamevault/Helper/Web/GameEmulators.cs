using GameVault.Core;
using gamevault.Localization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The emulators an administrator gives some retro games instead of their console's (SanctuaryVault service): read when
    /// the Retro page opens and before a game starts; without an answer, the last one read is kept.
    /// </summary>
    internal static class GameEmulators
    {
        public sealed class Emulator
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            /// <summary>The RomM platform's slug: "switch-2".</summary>
            [JsonPropertyName("console")] public string Console { get; set; } = "";
            [JsonPropertyName("windows")] public string? Windows { get; set; }
            [JsonPropertyName("linux")] public string? Linux { get; set; }
            [JsonPropertyName("mac")] public string? Mac { get; set; }
            /// <summary>Its program in a Windows or Mac build ("lichii.exe"); null: named as the console emulator's.</summary>
            [JsonPropertyName("executable")] public string? Executable { get; set; }
        }

        public sealed class Catalog
        {
            [JsonPropertyName("emulators")] public List<Emulator> Emulators { get; set; } = new();
            /// <summary>A RomM game's emulator, by the game's id.</summary>
            [JsonPropertyName("games")] public Dictionary<int, string> Games { get; set; } = new();
        }

        private static Catalog? current;
        /// <summary>One by emulator and link: its installation is not done twice at once.</summary>
        private static readonly ConcurrentDictionary<string, CustomSwitchEmulator> managers = new();

        private static string Service => SanctuaryService.Current.TrimEnd('/');

        /// <summary>Null when the service offers none.</summary>
        public static async Task<Catalog?> LoadAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/retro/emulators");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using HttpResponseMessage response = await WebHelper.SendLongAsync(request, timeout.Token);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    return current = null;
                response.EnsureSuccessStatusCode();
                return current = JsonSerializer.Deserialize<Catalog>(await response.Content.ReadAsStringAsync()) ?? new();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return current;
            }
        }

        /// <summary>The emulators that can play a game of this console.</summary>
        public static IEnumerable<Emulator> OfConsole(string slug) => current?.Emulators.Where(e => e.Console == slug) ?? Enumerable.Empty<Emulator>();

        /// <summary>"Windows, Linux": the systems it has a build for.</summary>
        public static string SystemsOf(Emulator emulator) => string.Join(", ", new[] { ("Windows", emulator.Windows), ("Linux", emulator.Linux), ("Mac", emulator.Mac) }
            .Where(s => !string.IsNullOrWhiteSpace(s.Item2)).Select(s => s.Item1));

        public static string? IdOf(RetroGame game) => current?.Games.TryGetValue(game.Id, out string? id) == true ? id : null;

        /// <summary>The game's own emulator when it has a build for this system; else null (its console's plays it).</summary>
        public static StandaloneEmulator? For(RetroGame game)
        {
            if (IdOf(game) is not string id || current!.Emulators.FirstOrDefault(e => e.Id == id && e.Console == game.Platform.Slug) is not Emulator emulator)
                return null;
            if (StandaloneEmulator.Of(game.Platform.System) is not SwitchEmulator console)
                return null;
            var manager = managers.GetOrAdd($"{emulator.Id}\n{emulator.Windows}\n{emulator.Linux}\n{emulator.Mac}\n{emulator.Executable}", _ => new CustomSwitchEmulator(emulator, console));
            return manager.Layout.IsAvailable ? manager : null;
        }

        public static Task SetAsync(Emulator emulator) => SendAsync(HttpMethod.Put, $"retro/emulators/{Uri.EscapeDataString(emulator.Id)}",
            new { name = emulator.Name, console = emulator.Console, windows = emulator.Windows, linux = emulator.Linux, mac = emulator.Mac, executable = emulator.Executable });

        public static Task DeleteAsync(string id) => SendAsync(HttpMethod.Delete, $"retro/emulators/{Uri.EscapeDataString(id)}", null);

        /// <summary>Null: the console's emulator again.</summary>
        public static async Task SetGameAsync(RetroGame game, string? emulatorId)
        {
            await SendAsync(HttpMethod.Put, $"retro/emulators/games/{game.Id}", new { emulator = emulatorId });
            if (current != null)
            {
                if (emulatorId == null)
                    current.Games.Remove(game.Id);
                else
                    current.Games[game.Id] = emulatorId;
            }
        }

        private static async Task SendAsync(HttpMethod method, string path, object? body)
        {
            using var request = new HttpRequestMessage(method, $"{Service}/{path}");
            if (body != null)
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                string? error = null;
                try { error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString(); }
                catch (Exception ex) { Log.Ignored(ex); }
                throw new InvalidOperationException(error ?? Loc.F("The SanctuaryVault service answered {0}.", (int)response.StatusCode));
            }
        }
    }
}
