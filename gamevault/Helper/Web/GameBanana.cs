using GameVault.Core;
using GameVault.Core.Retro;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>A mod on GameBanana, as its lists give it.</summary>
    public sealed class GameBananaMod
    {
        [JsonPropertyName("_idRow")] public int Id { get; set; }
        [JsonPropertyName("_sName")] public string Name { get; set; } = "";
        [JsonPropertyName("_nLikeCount")] public int Likes { get; set; }
        [JsonPropertyName("_bHasContentRatings")] public bool HasContentRatings { get; set; }
        [JsonPropertyName("_bIsObsolete")] public bool IsObsolete { get; set; }
        [JsonPropertyName("_sModelName")] public string Model { get; set; } = "Mod";
        [JsonPropertyName("_aRootCategory")] public GameBananaCategory? Category { get; set; }
        [JsonPropertyName("_aPreviewMedia")] public GameBananaMedia? Media { get; set; }

        [JsonIgnore] public string? Image => Media?.Images?.FirstOrDefault() is { } image ? $"{image.BaseUrl}/{image.File220 ?? image.File}" : null;
        [JsonIgnore] public string CategoryName => Category?.Name ?? "";
        [JsonIgnore] public bool Installed { get; set; }
    }

    public sealed class GameBananaCategory
    {
        [JsonPropertyName("_sName")] public string Name { get; set; } = "";
    }

    public sealed class GameBananaMedia
    {
        [JsonPropertyName("_aImages")] public List<GameBananaImage>? Images { get; set; }
    }

    public sealed class GameBananaImage
    {
        [JsonPropertyName("_sBaseUrl")] public string BaseUrl { get; set; } = "";
        [JsonPropertyName("_sFile")] public string File { get; set; } = "";
        [JsonPropertyName("_sFile220")] public string? File220 { get; set; }
    }

    /// <summary>A file of a mod: GameBanana checks each with an antivirus, the client takes the clean ones only.</summary>
    public sealed class GameBananaFile
    {
        [JsonPropertyName("_idRow")] public int Id { get; set; }
        [JsonPropertyName("_sFile")] public string Name { get; set; } = "";
        [JsonPropertyName("_nFilesize")] public long Size { get; set; }
        [JsonPropertyName("_sDescription")] public string? Description { get; set; }
        [JsonPropertyName("_tsDateAdded")] public long AddedAt { get; set; }
        [JsonPropertyName("_sDownloadUrl")] public string Url { get; set; } = "";
        [JsonPropertyName("_sMd5Checksum")] public string? Md5 { get; set; }
        [JsonPropertyName("_sAvResult")] public string? Antivirus { get; set; }

        [JsonIgnore] public bool IsClean => string.Equals(Antivirus, "clean", StringComparison.OrdinalIgnoreCase);
        [JsonIgnore] public SwitchMods.ModFile AsModFile => new(Id, Name, Size, Description, AddedAt);
        /// <summary>"ultracam_261.zip · Version 2.6.1 · 127 KB".</summary>
        [JsonIgnore] public string Text => string.Join(" · ", new[] { Description, Name, Converter.GameSizeConverter.Text(Size) }.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    public sealed class GameBananaModPage
    {
        [JsonPropertyName("_idRow")] public int Id { get; set; }
        [JsonPropertyName("_sName")] public string Name { get; set; } = "";
        [JsonPropertyName("_sDescription")] public string? Description { get; set; }
        [JsonPropertyName("_sProfileUrl")] public string? Url { get; set; }
        [JsonPropertyName("_aFiles")] public List<GameBananaFile> Files { get; set; } = new();
    }

    /// <summary>
    /// The mods the players publish on GameBanana (gamebanana.com/apiv11, read without an account): the game found by
    /// its name, its mods by the most downloaded, a mod's files.
    /// </summary>
    internal static class GameBanana
    {
        /// <summary>Another address in the tests.</summary>
        public static string Api { get; set; } = "https://gamebanana.com/apiv11";

        private sealed class Page<T>
        {
            [JsonPropertyName("_aMetadata")] public PageInfo Metadata { get; set; } = new();
            [JsonPropertyName("_aRecords")] public List<T> Records { get; set; } = new();
        }

        private sealed class PageInfo
        {
            [JsonPropertyName("_bIsComplete")] public bool IsComplete { get; set; }
        }

        private sealed class Found
        {
            [JsonPropertyName("_idRow")] public int Id { get; set; }
            [JsonPropertyName("_sName")] public string Name { get; set; } = "";
        }

        private static readonly ConcurrentDictionary<string, int?> games = new();

        private static async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
        {
            using HttpResponseMessage response = await HttpClients.Shared.GetAsync(Api + path, ct);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(ct));
        }

        /// <summary>"Pokémon Sword & Shield" → "pokemonswordandshield": accents taken off, then all but letters and digits.</summary>
        private static string Simple(string name)
        {
            string plain = new string(name.Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
            return Regex.Replace(plain.ToLowerInvariant().Replace("&", "and"), "[^a-z0-9]", "");
        }

        /// <summary>GameBanana's game of this name ("The Legend of Zelda Tears of the Kingdom"): the same name, else one that holds it; null when none.</summary>
        public static async Task<int?> FindGameAsync(string name)
        {
            string simple = Simple(name);
            // No Latin letter or digit ("ポケモン"): nothing to compare, any result would "hold" it
            if (simple.Length == 0)
                return null;
            if (games.TryGetValue(simple, out int? known))
                return known;
            var found = await GetAsync<Page<Found>>($"/Util/Search/Results?_sModelName=Game&_sSearchString={Uri.EscapeDataString(name)}&_nPage=1");
            var records = found?.Records ?? new();
            int? id = (records.FirstOrDefault(g => Simple(g.Name) == simple)
                ?? records.FirstOrDefault(g => Simple(g.Name).Contains(simple) || (simple.Length > 0 && simple.Contains(Simple(g.Name)) && Simple(g.Name).Length >= 6)))?.Id;
            games[simple] = id;
            return id;
        }

        /// <summary>
        /// The game's mods, the most downloaded first (or found by words), without the adult ones nor the obsolete ones.
        /// </summary>
        /// <returns>A page of them, and whether there are more.</returns>
        public static async Task<(List<GameBananaMod> Mods, bool More)> ModsAsync(int gameId, string? search, int page)
        {
            string path = string.IsNullOrWhiteSpace(search)
                ? $"/Mod/Index?_nPage={page}&_nPerpage=20&_aFilters%5BGeneric_Game%5D={gameId}&_sSort=Generic_MostDownloaded"
                : $"/Util/Search/Results?_sModelName=Mod&_sSearchString={Uri.EscapeDataString(search.Trim())}&_idGameRow={gameId}&_nPage={page}";
            var result = await GetAsync<Page<GameBananaMod>>(path);
            var mods = (result?.Records ?? new()).Where(m => m.Model == "Mod" && !m.HasContentRatings && !m.IsObsolete).ToList();
            return (mods, result is { Metadata.IsComplete: false } && result.Records.Count > 0);
        }

        /// <summary>A mod's description and its files, the clean ones only.</summary>
        public static async Task<GameBananaModPage?> ModAsync(int modId)
        {
            var page = await GetAsync<GameBananaModPage>($"/Mod/{modId}/ProfilePage");
            if (page != null)
                page.Files = page.Files.Where(f => f.IsClean && !string.IsNullOrEmpty(f.Url)).ToList();
            return page;
        }

        /// <summary>The file in this folder, checked against GameBanana's checksum.</summary>
        public static async Task<string> DownloadAsync(GameBananaFile file, string folder, IProgress<double>? progress, CancellationToken ct)
        {
            if (!file.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !file.Url.StartsWith(Api, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Not a GameBanana download");
            Directory.CreateDirectory(folder);
            string name = SwitchMods.FolderName(Path.GetFileName(file.Name));
            string target = Path.Combine(folder, name);
            await RetroArchManager.DownloadAsync(file.Url, target, done => progress?.Report(Math.Min(1, done)), ct);
            if (file.Md5 is { Length: 32 } expected)
            {
                await using FileStream written = File.OpenRead(target);
                string actual = Convert.ToHexString(await MD5.HashDataAsync(written, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    written.Close();
                    File.Delete(target);
                    throw new InvalidDataException(Localization.Loc.T("The mod's file arrived damaged: try again."));
                }
            }
            return target;
        }
    }
}
