using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameVault.Core.Captures
{
    /// <summary>A screenshot or a video taken while a game ran. <see cref="Game"/>: "pc:793", "retro:psx:2161".</summary>
    public sealed record Capture(
        [property: JsonPropertyName("game")] string Game,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("taken")] DateTime Taken)
    {
        [JsonIgnore]
        public bool IsVideo => CaptureFiles.IsVideo(Path);
    }

    public static class CaptureFiles
    {
        // .jxr: the Game Bar's screenshots of a game in HDR
        private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".jxr" };
        private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".webm", ".mov" };

        public static bool IsCapture(string path) => Images.Contains(System.IO.Path.GetExtension(path)) || IsVideo(path);
        public static bool IsVideo(string path) => Videos.Contains(System.IO.Path.GetExtension(path));
        /// <summary>The client shows these images itself; the others (a video, an HDR image) open in the system's player.</summary>
        public static bool IsShownImage(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
    }

    /// <summary>
    /// The captures of a profile's games, in a JSON file beside its config. The files stay where they were taken (the
    /// Game Bar's folder, OBS's, the emulator's): only their game is kept here, and a file deleted since is not shown.
    /// </summary>
    public sealed class CaptureIndex
    {
        private readonly string file;
        private readonly object gate = new();
        private List<Capture>? captures;

        public CaptureIndex(string file) => this.file = file;

        /// <summary>A game's captures still on the disk, the newest first.</summary>
        public IReadOnlyList<Capture> Of(string game)
        {
            lock (gate)
                return Loaded().Where(c => c.Game == game && File.Exists(c.Path)).OrderByDescending(c => c.Taken).ToList();
        }

        /// <summary>False when that file is already a capture (seen twice: created, then renamed).</summary>
        public bool Add(Capture capture)
        {
            lock (gate)
            {
                var list = Loaded();
                if (list.Any(c => string.Equals(c.Path, capture.Path, StringComparison.OrdinalIgnoreCase)))
                    return false;
                // The files deleted since are forgotten as the list is written; not the ones of a drive away (a NAS off, a USB disk out)
                list.RemoveAll(c => !File.Exists(c.Path) && Path.GetPathRoot(c.Path) is { Length: > 0 } root && Directory.Exists(root));
                list.Add(capture);
                Save(list);
                return true;
            }
        }

        private List<Capture> Loaded()
        {
            if (captures != null)
                return captures;
            try { captures = File.Exists(file) ? JsonSerializer.Deserialize<List<Capture>>(File.ReadAllText(file)) ?? new() : new(); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { captures = new(); }
            return captures;
        }

        private void Save(List<Capture> list)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            string written = file + ".tmp";
            File.WriteAllText(written, JsonSerializer.Serialize(list));
            File.Move(written, file, overwrite: true);
        }
    }
}
