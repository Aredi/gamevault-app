using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVault.Core.CloudSaves
{
    /// <summary>The newest save on the server: uploaded at <see cref="UploadedAt"/> by the installation <see cref="InstallationId"/>.</summary>
    public record ServerSave(DateTime UploadedAt, string InstallationId)
    {
        /// <summary>GameVault names saves "&lt;upload time in ms&gt;_&lt;installation id&gt;.zip".</summary>
        public static ServerSave? FromFileName(string? fileName)
        {
            Match match = Regex.Match(Path.GetFileName(fileName ?? "").Trim('"'), @"^(\d+)_([0-9a-fA-F-]+)\.zip$");
            if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long milliseconds))
                return null;
            return new ServerSave(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime, match.Groups[2].Value);
        }
    }

    public enum SaveSyncAction
    {
        /// <summary>The server has this computer's save, or no save: nothing to do.</summary>
        UpToDate,
        /// <summary>Another computer saved since, and nothing changed here: take the server's save.</summary>
        Restore,
        /// <summary>Both changed since they were last in sync: the user chooses.</summary>
        Conflict,
    }

    public static class SaveSync
    {
        /// <summary>File times are not exact (FAT, network shares, archive restore): changes closer than this count as the same.</summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(5);

        /// <summary>Before a game starts.</summary>
        /// <param name="lastSync">When this installation last uploaded or restored, null if never.</param>
        /// <param name="localLastChange">Newest local save file, null when the game has no local saves.</param>
        public static SaveSyncAction BeforePlaying(ServerSave? server, string installationId, DateTime? lastSync, DateTime? localLastChange)
        {
            if (server == null || server.InstallationId.Equals(installationId, StringComparison.OrdinalIgnoreCase))
                return SaveSyncAction.UpToDate;
            if (localLastChange == null)
                return SaveSyncAction.Restore;
            // Played here since the last sync? Without a sync, compare with the server's save itself.
            DateTime reference = lastSync ?? server.UploadedAt;
            return localLastChange.Value > reference + Tolerance ? SaveSyncAction.Conflict : SaveSyncAction.Restore;
        }

        /// <summary>Before the save made while playing replaces the server's: another computer uploaded meanwhile.</summary>
        public static bool ServerChangedMeanwhile(ServerSave? server, string installationId, DateTime? lastSync) =>
            server != null
            && !server.InstallationId.Equals(installationId, StringComparison.OrdinalIgnoreCase)
            && (lastSync == null || server.UploadedAt > lastSync.Value + Tolerance);

        /// <summary>
        /// The save files Ludusavi found for a game ("backup --preview --api" output). Each file is reported with its
        /// local path; registry entries are left out.
        /// </summary>
        public static List<string> SaveFilesFromLudusaviPreview(string json)
        {
            var files = new List<string>();
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("games", out JsonElement games) || games.ValueKind != JsonValueKind.Object)
                return files;
            foreach (JsonProperty game in games.EnumerateObject())
            {
                if (!game.Value.TryGetProperty("files", out JsonElement entries) || entries.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (JsonProperty file in entries.EnumerateObject())
                {
                    if (file.Value.ValueKind == JsonValueKind.Object && file.Value.TryGetProperty("ignored", out JsonElement ignored) && ignored.ValueKind == JsonValueKind.True)
                        continue;
                    files.Add(file.Name);
                }
            }
            return files;
        }

        /// <summary>Newest write time (UTC) of the files that exist, null when none does.</summary>
        public static DateTime? LastChange(IEnumerable<string> files)
        {
            DateTime? newest = null;
            foreach (string file in files)
            {
                try
                {
                    if (!File.Exists(file))
                        continue;
                    DateTime time = File.GetLastWriteTimeUtc(file);
                    if (newest == null || time > newest)
                        newest = time;
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            return newest;
        }
    }
}
