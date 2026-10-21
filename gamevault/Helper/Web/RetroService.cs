using Avalonia.Media.Imaging;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    internal sealed class RetroPlatform
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("count")]
        public int Count { get; set; }

        public RetroSystem System => RetroSystems.All[Slug];
        public override string ToString() => $"{Name} ({Count})";
    }

    /// <summary>A game of RomM, with what the page shows of it on this PC.</summary>
    internal sealed class RetroGame : ViewModelBase
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";
        [JsonPropertyName("size")]
        public long Size { get; set; }
        [JsonPropertyName("cover")]
        public string? Cover { get; set; }
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
        /// <summary>Several files (a CD game with its tracks...): they come as one zip, unpacked here.</summary>
        [JsonPropertyName("multi")]
        public bool Multi { get; set; }

        [JsonIgnore]
        public RetroPlatform Platform { get; set; } = null!;
        [JsonIgnore]
        public string SizeText => GameVault.Core.Storage.StorageCleanup.FormatSize(Size);

        private Bitmap? coverImage;
        private bool coverAsked;
        /// <summary>Asked for when a card shows it: the page only realizes the cards on screen.</summary>
        [JsonIgnore]
        public Bitmap? CoverImage
        {
            get
            {
                if (!coverAsked && Cover != null)
                {
                    coverAsked = true;
                    _ = LoadCoverAsync();
                }
                return coverImage;
            }
        }

        private async Task LoadCoverAsync()
        {
            try
            {
                coverImage = await RetroService.CoverAsync(Cover!);
                OnPropertyChanged(nameof(CoverImage));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private string status = "";
        [JsonIgnore]
        public string Status { get => status; set { status = value; OnPropertyChanged(); } }

        private bool busy;
        [JsonIgnore]
        public bool Busy { get => busy; set { busy = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public string? RomFile => RetroService.InstalledRom(this);
        [JsonIgnore]
        public bool Installed => RomFile != null;

        public void Refresh()
        {
            OnPropertyChanged(nameof(Installed));
            OnPropertyChanged(nameof(RomFile));
        }
    }

    /// <summary>
    /// The games of the other platforms, from RomM through the SanctuaryVault service. They are kept in
    /// &lt;library folder&gt;/GameVault/Retro/&lt;platform&gt;/&lt;RomM id&gt;/, one file each.
    /// </summary>
    internal static class RetroService
    {
        private static string Service => SanctuaryService.Current.TrimEnd('/');
        internal const string DownloadMetadata = "gamevault-metadata";

        /// <summary>The platforms with games that RetroArch plays here; null when the server has none.</summary>
        public static async Task<List<RetroPlatform>?> ListPlatformsAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/retro/platforms");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (response.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            var platforms = JsonSerializer.Deserialize<List<RetroPlatform>>(await response.Content.ReadAsStringAsync()) ?? new();
            return platforms.Where(p => RetroSystems.All.ContainsKey(p.Slug)).OrderBy(p => p.Name).ToList();
        }

        public static async Task<List<RetroGame>> ListGamesAsync(RetroPlatform platform)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/retro/platforms/{platform.Id}/games");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            var games = JsonSerializer.Deserialize<List<RetroGame>>(await response.Content.ReadAsStringAsync()) ?? new();
            foreach (RetroGame game in games)
                game.Platform = platform;
            return games;
        }

        public static string ContentUrl(RetroGame game) => $"{Service}/retro/games/{game.Id}/content";

        public static string Folder(RetroGame game, string rootDirectory) =>
            Path.Combine(rootDirectory, "GameVault", "Retro", game.Platform.Slug, game.Id.ToString());

        /// <summary>The game's folder in one of the library folders, once its download is complete.</summary>
        public static string? InstalledFolder(RetroGame game) => (SettingsViewModel.Instance.RootDirectories ?? new())
            .Select(root => Folder(game, root.Uri))
            .FirstOrDefault(folder => Directory.Exists(folder) && !File.Exists(Path.Combine(folder, DownloadMetadata)));

        /// <summary>The file the emulator starts; null while the game is not installed.</summary>
        public static string? InstalledRom(RetroGame game) => InstalledFolder(game) is string folder
            ? RetroSystems.LaunchFile(game.Platform.System, Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            : null;

        /// <summary>The start of a game's file (a CIA's header), without downloading it all.</summary>
        public static async Task<byte[]> ReadStartAsync(RetroGame game, int bytes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ContentUrl(game));
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, bytes - 1);
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            await using Stream body = await response.Content.ReadAsStreamAsync();
            byte[] start = new byte[bytes];
            int read = 0, last;
            while (read < bytes && (last = await body.ReadAsync(start.AsMemory(read))) > 0)
                read += last;
            return start[..read];
        }

        internal sealed record Firmware([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("fileName")] string FileName, [property: JsonPropertyName("size")] long Size);

        /// <summary>The BIOS RomM keeps for the platform.</summary>
        public static async Task<List<Firmware>> ListFirmwareAsync(RetroPlatform platform)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/retro/platforms/{platform.Id}/firmware");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<List<Firmware>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        public static async Task DownloadFirmwareAsync(Firmware firmware, string file)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/retro/firmware/{firmware.Id}/content");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            await File.WriteAllBytesAsync(file + ".part", await response.Content.ReadAsByteArrayAsync());
            File.Move(file + ".part", file, overwrite: true);
        }

        /// <summary>When the player's saves were last sent; null when the server has none (or keeps none).</summary>
        public static async Task<long?> SavesInfoAsync(string set)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/retro/saves/{set}/info");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.ServiceUnavailable)
                return null;
            response.EnsureSuccessStatusCode();
            using JsonDocument info = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return info.RootElement.GetProperty("savedAt").GetInt64();
        }

        public static async Task<byte[]> DownloadSavesAsync(string set)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Service}/retro/saves/{set}");
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <returns>When the server saved them.</returns>
        public static async Task<long> UploadSavesAsync(string set, Stream zip)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Service}/retro/saves/{set}") { Content = new StreamContent(zip) };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            using JsonDocument saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return saved.RootElement.GetProperty("savedAt").GetInt64();
        }

        /// <summary>Kept on disk: a cover is downloaded once.</summary>
        public static async Task<Bitmap> CoverAsync(string cover)
        {
            string file = Path.Combine(PlatformInfo.LocalDataDirectory, "retro-covers", cover.Replace('/', '_'));
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await WebHelper.DownloadImageFromUrlAsync($"{Service}/retro/covers/{cover}", file);
            }
            return await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(file);
                return Bitmap.DecodeToWidth(stream, 240);
            });
        }
    }
}
