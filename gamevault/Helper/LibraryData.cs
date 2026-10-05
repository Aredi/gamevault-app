using GameVault.Core;
using GameVault.Core.Library;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Library data that is not part of the server's game list: the profile's collections and the
    /// current user's play records.
    /// </summary>
    internal static class LibraryData
    {
        public static GameCollectionStore Collections => new GameCollectionStore(LoginManager.Instance.GetUserProfile().CollectionsFile);

        /// <summary>Raised after a collection was created, renamed, deleted or a game was added/removed.</summary>
        public static event EventHandler? CollectionsChanged;

        public static void NotifyCollectionsChanged() => CollectionsChanged?.Invoke(null, EventArgs.Empty);

        /// <summary>Play records of the logged in user (one per game that has been started at least once).</summary>
        public static async Task<List<PlayRecord>> GetMyPlayRecordsAsync()
        {
            int? userId = LoginManager.Instance.GetCurrentUser()?.ID;
            if (userId == null)
                return new List<PlayRecord>();
            string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/progresses?filter.user.id=$eq:{userId}&limit=-1");
            var progresses = JsonSerializer.Deserialize<PaginatedData<Progress>>(json)?.Data ?? Array.Empty<Progress>();
            return progresses
                .Where(p => p.Game != null && (p.MinutesPlayed > 0 || p.LastPlayedAt != null))
                .Select(p => new PlayRecord(p.Game!.ID, p.LastPlayedAt, p.MinutesPlayed ?? 0))
                .ToList();
        }
    }
}
