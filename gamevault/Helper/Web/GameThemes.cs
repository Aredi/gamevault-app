using GameVault.Core;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace gamevault.Helper
{
    /// <summary>
    /// A game's theme, as a console plays it on the selected game: the first YouTube video found for its name and "main
    /// theme" that is a piece of music (not a whole soundtrack of an hour, not a 10-second clip), its sound alone.
    /// </summary>
    internal static class GameThemes
    {
        private static readonly TimeSpan Shortest = TimeSpan.FromSeconds(40), Longest = TimeSpan.FromMinutes(8);
        // The video found by search (null: none): asked once a session; the sound's address is asked each time (it expires)
        private static readonly ConcurrentDictionary<string, Task<string?>> found = new();
        private static YoutubeClient? youtube;

        /// <summary>The tests' themes instead of YouTube's.</summary>
        internal static Func<string, Task<string?>>? Instead { get; set; }

        /// <summary>The sound's address of the theme found for this search, or null.</summary>
        public static async Task<string?> SoundOfAsync(string search)
        {
            if (Instead != null)
                return await Instead(search);
            try
            {
                if (await found.GetOrAdd(search, FindAsync) is not string video)
                    return null;
                youtube ??= new YoutubeClient();
                var manifest = await youtube.Videos.Streams.GetManifestAsync(video);
                return manifest.GetAudioOnlyStreams().GetWithHighestBitrate()?.Url;
            }
            catch (Exception ex)
            {
                // No network a moment: asked again later
                found.TryRemove(search, out _);
                Log.Ignored(ex);
                return null;
            }
        }

        private static async Task<string?> FindAsync(string search)
        {
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
