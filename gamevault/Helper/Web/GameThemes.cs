using GameVault.Core;
using gamevault.Helper.Platform;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace gamevault.Helper
{
    /// <summary>
    /// A game's theme, as a console plays it on the selected game: the first YouTube video found for its name and "main
    /// theme" that is a piece of music (not a whole soundtrack of an hour, not a 10-second clip), its sound alone.
    /// The videos found are kept on the PC and searched for in the background at start (as Playnite's metadata): a
    /// game chosen only asks YouTube for its sound's address, and that as soon as it is chosen.
    /// </summary>
    internal static class GameThemes
    {
        private static readonly TimeSpan Shortest = TimeSpan.FromSeconds(40), Longest = TimeSpan.FromMinutes(8);
        // A sound's address lasts some hours on YouTube: asked again after this
        private static readonly TimeSpan SoundLasts = TimeSpan.FromHours(4);
        // Between two searches in the background: YouTube is not hurried, the PC not slowed
        internal static TimeSpan PreloadPause { get; set; } = TimeSpan.FromSeconds(2);

        private static readonly object gate = new();
        // The video found by search ("": none), as kept in the file
        private static Dictionary<string, string>? known;
        private static readonly ConcurrentDictionary<string, Task<string?>> searching = new();
        private static readonly ConcurrentDictionary<string, (DateTime Asked, Task<string?> Sound)> sounds = new();
        private static int preloading;
        private static YoutubeClient? youtube;

        /// <summary>The tests' themes instead of YouTube's (the sound's address for a search).</summary>
        internal static Func<string, Task<string?>>? Instead { get; set; }
        /// <summary>The tests' search instead of YouTube's (the video for a search).</summary>
        internal static Func<string, Task<string?>>? SearchInstead { get; set; }

        internal static string File => Path.Combine(PlatformInfo.LocalDataDirectory, "cache", "themes.json");

        /// <summary>The tests start from what the file holds.</summary>
        internal static void Forget()
        {
            lock (gate)
                known = null;
            searching.Clear();
            sounds.Clear();
        }

        /// <summary>The sound's address of the theme found for this search, or null; asked once for some hours.</summary>
        public static Task<string?> SoundOfAsync(string search)
        {
            if (Instead != null)
                return Instead(search);
            if (sounds.TryGetValue(search, out var asked) && DateTime.UtcNow - asked.Asked < SoundLasts)
                return asked.Sound;
            Task<string?> sound = AskSoundAsync(search);
            sounds[search] = (DateTime.UtcNow, sound);
            return sound;
        }

        private static async Task<string?> AskSoundAsync(string search)
        {
            try
            {
                if (await VideoOfAsync(search) is not string video)
                    return null;
                youtube ??= new YoutubeClient();
                var manifest = await youtube.Videos.Streams.GetManifestAsync(video);
                // WebM (Opus): its silences can be told without decoding it
                var sounds = manifest.GetAudioOnlyStreams().OrderByDescending(s => s.Container == Container.WebM).ThenByDescending(s => s.Bitrate.BitsPerSecond).ToList();
                if (sounds.FirstOrDefault() is not IStreamInfo sound)
                    return null;
                TimeSpan? start = sound.Container == Container.WebM ? await StartOfAsync(video, sound.Url) : null;
                // A media fragment: the player starts there
                return start is TimeSpan at ? $"{sound.Url}#t={at.TotalSeconds.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)}" : sound.Url;
            }
            catch (Exception ex)
            {
                // No network a moment: asked again next time
                sounds.TryRemove(search, out _);
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>The sound's first bytes (some 25 seconds) read once by video: where it starts to be heard, kept with the videos.</summary>
        internal static Func<string, Task<byte[]>>? BeginningInstead { get; set; }

        private static async Task<TimeSpan?> StartOfAsync(string video, string url)
        {
            string key = "start:" + video;
            lock (gate)
            {
                if (Known().TryGetValue(key, out string? kept) && double.TryParse(kept, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
                    return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
            }
            try
            {
                byte[] beginning;
                if (BeginningInstead != null)
                    beginning = await BeginningInstead(url);
                else
                {
                    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 399_999);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var response = await HttpClients.Shared.SendAsync(request, timeout.Token);
                    beginning = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                }
                TimeSpan? start = GameVault.Core.Couch.QuietStart.Of(beginning);
                lock (gate)
                    Known()[key] = (start?.TotalSeconds ?? 0).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
                Save();
                return start;
            }
            catch (Exception ex)
            {
                // Played from its start this time; measured again next time
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>The video kept for this search, else searched for (once at a time) and kept.</summary>
        internal static async Task<string?> VideoOfAsync(string search)
        {
            Dictionary<string, string> kept = Known();
            lock (gate)
            {
                if (kept.TryGetValue(search, out string? video))
                    return video.Length > 0 ? video : null;
            }
            try
            {
                string? found = await searching.GetOrAdd(search, FindAsync);
                lock (gate)
                    kept[search] = found ?? "";
                Save();
                return found;
            }
            finally { searching.TryRemove(search, out _); }
        }

        /// <summary>
        /// In the background at start: the games' themes not searched yet, one after the other, the first given first.
        /// Once a session; stopped by the token.
        /// </summary>
        public static async Task PreloadAsync(IEnumerable<string> searches, CancellationToken stop = default)
        {
            if (Interlocked.Exchange(ref preloading, 1) == 1)
                return;
            try
            {
                foreach (string search in searches.Distinct().ToList())
                {
                    if (stop.IsCancellationRequested)
                        return;
                    bool kept;
                    lock (gate)
                        kept = Known().ContainsKey(search);
                    if (kept)
                        continue;
                    try { await VideoOfAsync(search); }
                    catch (Exception ex)
                    {
                        // No network: the rest another time
                        Log.Ignored(ex);
                        return;
                    }
                    await Task.Delay(PreloadPause, stop).ContinueWith(_ => { });
                }
            }
            finally { Interlocked.Exchange(ref preloading, 0); }
        }

        private static Dictionary<string, string> Known()
        {
            lock (gate)
            {
                if (known != null)
                    return known;
                try { known = System.IO.File.Exists(File) ? JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(File)) : null; }
                catch (Exception ex) { Log.Ignored(ex); }
                return known ??= new Dictionary<string, string>();
            }
        }

        private static void Save()
        {
            try
            {
                // Written whole, one at a time
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                    System.IO.File.WriteAllText(File + ".part", JsonSerializer.Serialize(known));
                    System.IO.File.Move(File + ".part", File, true);
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private static async Task<string?> FindAsync(string search)
        {
            if (SearchInstead != null)
                return await SearchInstead(search);
            youtube ??= new YoutubeClient();
            int seen = 0;
            await foreach (var video in youtube.Search.GetVideosAsync(search))
            {
                if (video.Duration is TimeSpan length && length >= Shortest && length <= Longest)
                    return video.Id.Value;
                if (++seen >= 8)
                    break;
            }
            return null;
        }

        /// <summary>What is searched: the name, the console for a retro game (a "Zelda" has many), and "main theme".</summary>
        public static string SearchOf(string title, string? console) =>
            string.Join(' ', new[] { title.Trim(), console?.Trim(), "main theme" }.Where(p => !string.IsNullOrEmpty(p)));
    }
}
