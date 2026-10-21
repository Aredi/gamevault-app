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

        public string Core => RetroArchLayout.Cores[Slug];
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
            return platforms.Where(p => RetroArchLayout.Cores.ContainsKey(p.Slug)).OrderBy(p => p.Name).ToList();
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

        /// <summary>The game's file in one of the library folders; null while it is not (completely) downloaded.</summary>
        public static string? InstalledRom(RetroGame game)
        {
            foreach (var root in SettingsViewModel.Instance.RootDirectories ?? new())
            {
                string folder = Folder(game, root.Uri);
                if (Directory.Exists(folder) && !File.Exists(Path.Combine(folder, DownloadMetadata)))
                    return Directory.EnumerateFiles(folder).FirstOrDefault();
            }
            return null;
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
