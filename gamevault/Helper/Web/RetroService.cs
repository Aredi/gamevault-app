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
        public string DurationText => MainStory is long seconds ? Loc.F("{0} h", Math.Max(1, Math.Round(seconds / 3600.0))) : "—";
        /// <summary>The player's time on it (all PCs), as the library's PLAYTIME.</summary>
        [JsonIgnore]
        public int MinutesPlayed => RetroLibrary.MinutesOf(this);
        [JsonIgnore]
        public string PlaytimeText => MinutesPlayed > 0
            ? (string)new Converter.GameTimeConverter().Convert(MinutesPlayed, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture)!
            : "—";
        [JsonIgnore]
        public bool IsFavorite
        {
            get => RetroLibrary.IsFavorite(this);
            set { if (value != IsFavorite) _ = RetroLibrary.SetFavoriteAsync(this, value); OnPropertyChanged(); }
        }

        /// <summary>The time played or the favorites changed.</summary>
        public void RefreshPlayer()
        {
            OnPropertyChanged(nameof(MinutesPlayed));
            OnPropertyChanged(nameof(PlaytimeText));
            OnPropertyChanged(nameof(IsFavorite));
        }

        [JsonIgnore]
        public bool HasManual => Manual != null;
        [JsonIgnore]
        public bool HasVideo => !string.IsNullOrEmpty(Video);

        private Color? accent;
        /// <summary>The cover's color, for the card's glow and the page's tint.</summary>
        [JsonIgnore]
        public Color? Accent { get => accent; private set { accent = value; OnPropertyChanged(); } }

        private List<Bitmap>? screenshotImages;
        /// <summary>Loaded when the game's page opens.</summary>
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

        private bool isSelected;
        /// <summary>Selected with the keyboard (the card gets the library's selection frame).</summary>
        [JsonIgnore]
        public bool IsSelected { get => isSelected; set { isSelected = value; OnPropertyChanged(); } }

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

        #region Requests to the service
        internal static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, Service + path) { Content = content };
            return await WebHelper.SendLongAsync(request, CancellationToken.None);
        }

        /// <summary>A failure throws (the page tells why).</summary>
        internal static async Task<T> GetAsync<T>(string path) where T : new()
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        /// <summary>Nothing on a refusal (an older service, a feature it does not keep): what goes along without it.</summary>
        internal static async Task<T?> TryGetAsync<T>(string path) where T : class
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path);
            return response.IsSuccessStatusCode ? JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync()) : null;
        }

        private static async Task<byte[]> GetBytesAsync(string path)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        internal static StringContent Json(object value) => new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
        #endregion

        /// <summary>The platforms with games that RetroArch plays here; null when the server has none.</summary>
        public static async Task<List<RetroPlatform>?> ListPlatformsAsync()
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/retro/platforms");
            if (response.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            var platforms = JsonSerializer.Deserialize<List<RetroPlatform>>(await response.Content.ReadAsStringAsync()) ?? new();
            return platforms.Where(p => RetroSystems.All.ContainsKey(p.Slug)).OrderBy(p => p.Name).ToList();
        }

        public static async Task<List<RetroGame>> ListGamesAsync(RetroPlatform platform)
        {
            var games = await GetAsync<List<RetroGame>>($"/retro/platforms/{platform.Id}/games");
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

        /// <summary>
        /// The retro games in the library folders, from the description written beside each: the installed ones, or the
        /// ones whose download stopped half way.
        /// </summary>
        public static IEnumerable<(string Folder, RetroGame Game)> SavedGames(bool downloading)
        {
            foreach (var root in SettingsViewModel.Instance.RootDirectories ?? new())
            {
                string retro = Path.Combine(root.Uri, "GameVault", "Retro");
                if (!Directory.Exists(retro))
                    continue;
                foreach (string folder in Directory.EnumerateDirectories(retro).SelectMany(Directory.EnumerateDirectories))
                {
                    RetroGame? game = null;
                    try
                    {
                        string info = Path.Combine(folder, RetroDownload.InfoFile);
                        if (File.Exists(info) && File.Exists(Path.Combine(folder, DownloadMetadata)) == downloading
                            && JsonSerializer.Deserialize<RetroDownload.SavedGame>(File.ReadAllText(info)) is { } saved && RetroSystems.All.ContainsKey(saved.Platform.Slug))
                        {
                            saved.Game.Platform = saved.Platform;
                            game = saved.Game;
                        }
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                    if (game != null)
                        yield return (folder, game);
                }
            }
        }

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
        public static Task<List<Firmware>> ListFirmwareAsync(RetroPlatform platform) => GetAsync<List<Firmware>>($"/retro/platforms/{platform.Id}/firmware");

        public static async Task DownloadFirmwareAsync(Firmware firmware, string file)
        {
            await File.WriteAllBytesAsync(file + ".part", await GetBytesAsync($"/retro/firmware/{firmware.Id}/content"));
            File.Move(file + ".part", file, overwrite: true);
        }

        /// <summary>When the player's saves were last sent; null when the server has none (or keeps none).</summary>
        public static async Task<long?> SavesInfoAsync(string set)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"/retro/saves/{set}/info");
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.ServiceUnavailable)
                return null;
            response.EnsureSuccessStatusCode();
            using JsonDocument info = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return info.RootElement.GetProperty("savedAt").GetInt64();
        }

        public static Task<byte[]> DownloadSavesAsync(string set) => GetBytesAsync($"/retro/saves/{set}");

        internal sealed record SavesVersion([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("savedAt")] long SavedAt, [property: JsonPropertyName("size")] long Size);

        /// <summary>The older versions of the player's saves, newest first.</summary>
        public static Task<List<SavesVersion>> SavesHistoryAsync(string set) => GetAsync<List<SavesVersion>>($"/retro/saves/{set}/history");

        public static Task<byte[]> DownloadSavesVersionAsync(string set, string name) => GetBytesAsync($"/retro/saves/{set}/history/{Uri.EscapeDataString(name)}");

        /// <returns>When the server saved them.</returns>
        public static async Task<long> UploadSavesAsync(string set, Stream zip)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/retro/saves/{set}", new StreamContent(zip));
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
