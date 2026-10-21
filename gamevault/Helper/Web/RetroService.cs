using Avalonia.Media;
using Avalonia.Media.Imaging;
using gamevault.Localization;
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
    internal sealed class RetroPlatform : ViewModelBase
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

        private bool isSelected;
        /// <summary>The platform whose games the page shows (its chip is checked).</summary>
        public bool IsSelected { get => isSelected; set { isSelected = value; OnPropertyChanged(); } }
    }

    /// <summary>A line of a game's details: "Developers · Namco, Full Fat".</summary>
    internal sealed record RetroDetail(string Label, string Value);

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
        /// <summary>First release, in unix milliseconds.</summary>
        [JsonPropertyName("released")]
        public long? Released { get; set; }
        /// <summary>0 to 100.</summary>
        [JsonPropertyName("rating")]
        public double? Rating { get; set; }
        [JsonPropertyName("genres")]
        public string[] Genres { get; set; } = Array.Empty<string>();
        [JsonPropertyName("companies")]
        public string[] Companies { get; set; } = Array.Empty<string>();
        [JsonPropertyName("franchises")]
        public string[] Franchises { get; set; } = Array.Empty<string>();
        [JsonPropertyName("modes")]
        public string[] Modes { get; set; } = Array.Empty<string>();
        [JsonPropertyName("ageRatings")]
        public string[] AgeRatings { get; set; } = Array.Empty<string>();
        [JsonPropertyName("players")]
        public string? Players { get; set; }
        [JsonPropertyName("regions")]
        public string[] Regions { get; set; } = Array.Empty<string>();
        [JsonPropertyName("languages")]
        public string[] Languages { get; set; } = Array.Empty<string>();
        [JsonPropertyName("screenshots")]
        public string[] Screenshots { get; set; } = Array.Empty<string>();
        /// <summary>The manual (a PDF kept by RomM).</summary>
        [JsonPropertyName("manual")]
        public string? Manual { get; set; }
        /// <summary>A YouTube video id.</summary>
        [JsonPropertyName("video")]
        public string? Video { get; set; }
        /// <summary>Seconds to finish the main story (HowLongToBeat).</summary>
        [JsonPropertyName("mainStory")]
        public long? MainStory { get; set; }

        [JsonIgnore]
        public DateTime? ReleaseDate => Released is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
        /// <summary>Under the card: "2001 · Adventure".</summary>
        [JsonIgnore]
        public string Info => string.Join(" · ", new[] { ReleaseDate?.Year.ToString(), Genres.FirstOrDefault() }.Where(p => !string.IsNullOrEmpty(p)));
        [JsonIgnore]
        public string RatingText => Rating is double rating ? $"{rating:0} %" : "—";
        [JsonIgnore]
        public string PlayersText => string.IsNullOrEmpty(Players) || Players == "null" ? "—" : Players;
        [JsonIgnore]
        public string DurationText => MainStory is long seconds ? Loc.F("{0} h", Math.Max(1, Math.Round(seconds / 3600.0))) : "—";
        [JsonIgnore]
        public bool HasManual => Manual != null;
        [JsonIgnore]
        public bool HasVideo => !string.IsNullOrEmpty(Video);

        /// <summary>The details shown beside the game, the empty ones left out.</summary>
        [JsonIgnore]
        public IReadOnlyList<RetroDetail> Details => new (string Label, string? Value)[]
        {
            (Loc.T("Release date"), ReleaseDate?.ToString("D", System.Globalization.CultureInfo.CurrentCulture)),
            (Loc.T("Companies"), string.Join(", ", Companies)),
            (Loc.T("Series"), string.Join(", ", Franchises)),
            (Loc.T("Game modes"), string.Join(", ", Modes)),
            (Loc.T("Regions"), string.Join(", ", Regions)),
            (Loc.T("Languages"), string.Join(", ", Languages)),
            (Loc.T("Age rating"), string.Join(", ", AgeRatings)),
            (Loc.T("File"), FileName),
        }.Where(d => !string.IsNullOrWhiteSpace(d.Value)).Select(d => new RetroDetail(d.Label, d.Value!)).ToList();

        private Color? accent;
        /// <summary>The cover's color, for the card's glow and the details' tint.</summary>
        [JsonIgnore]
        public Color? Accent { get => accent; private set { accent = value; OnPropertyChanged(); } }

        private List<Bitmap>? screenshotImages;
        /// <summary>Loaded when the game is shown in the details.</summary>
        [JsonIgnore]
        public List<Bitmap>? ScreenshotImages { get => screenshotImages; private set { screenshotImages = value; OnPropertyChanged(); OnPropertyChanged(nameof(Banner)); } }
        [JsonIgnore]
        public Bitmap? Banner => ScreenshotImages?.FirstOrDefault() ?? CoverImage;

        public async Task LoadScreenshotsAsync()
        {
            if (ScreenshotImages != null)
                return;
            var images = new List<Bitmap>();
            foreach (string screenshot in Screenshots)
            {
                try { images.Add(await RetroService.MediaImageAsync(screenshot, 640)); }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            ScreenshotImages = images;
        }

        [JsonIgnore]
        public RetroPlatform Platform { get; set; } = null!;
        [JsonIgnore]
        public string SizeText => new Converter.GameSizeConverter().Convert(Size, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture) as string ?? "";

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
                coverImage = await RetroService.MediaImageAsync(Cover!, 240);
                OnPropertyChanged(nameof(CoverImage));
                OnPropertyChanged(nameof(Banner));
                Bitmap cover = coverImage;
                Accent = await Task.Run(() => CoverColors.Get(cover, "retro:" + Cover));
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

        /// <summary>A cover, screenshot or manual of RomM, kept on disk: each is downloaded once.</summary>
        public static async Task<string> MediaFileAsync(string media)
        {
            string file = Path.Combine(PlatformInfo.LocalDataDirectory, "retro-media", media.Replace('/', '_'));
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await WebHelper.DownloadImageFromUrlAsync($"{Service}/retro/media/{media}", file);
            }
            return file;
        }

        public static async Task<Bitmap> MediaImageAsync(string media, int width)
        {
            string file = await MediaFileAsync(media);
            return await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(file);
                var full = new Bitmap(stream);
                // Small pictures (a GBA screenshot is 240 pixels wide) stay as they are: the view scales them
                if (full.PixelSize.Width <= width)
                    return full;
                using (full)
                    return full.CreateScaledBitmap(new Avalonia.PixelSize(width, Math.Max(1, full.PixelSize.Height * width / full.PixelSize.Width)));
            });
        }
    }
}
