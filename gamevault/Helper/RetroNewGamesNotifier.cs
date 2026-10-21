using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The retro games added to RomM since the last check, told as the library's new games (same setting, same
    /// notification). The newest RomM id seen is stored per profile.
    /// </summary>
    internal static class RetroNewGamesNotifier
    {
        private static Timer? timer;
        private static readonly SemaphoreSlim checking = new(1, 1);

        private sealed class Latest
        {
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("platform")] public string Platform { get; set; } = "";
            [JsonPropertyName("platformName")] public string? PlatformName { get; set; }
        }

        public static void Start()
        {
            timer ??= new Timer(async _ => await CheckAsync(), null, TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(15));
        }

        public static async Task CheckAsync()
        {
            if (!SettingsViewModel.Instance.NotifyNewGames || !LoginManager.Instance.IsLoggedIn())
                return;
            if (!await checking.WaitAsync(0))
                return;
            try
            {
                var newest = (await RetroService.TryGetAsync<List<Latest>>("/retro/latest") ?? new())
                    .Where(g => RetroSystems.All.ContainsKey(g.Platform)).ToList();
                if (newest.Count == 0)
                    return;
                string configFile = LoginManager.Instance.GetUserProfile().UserConfigFile;
                int maxId = newest.Max(g => g.Id);
                // First check of this profile: remember the current state without telling about the whole of RomM
                if (!int.TryParse(Preferences.Get(AppConfigKey.LastSeenRetroGameId, configFile), out int lastSeen))
                {
                    Preferences.Set(AppConfigKey.LastSeenRetroGameId, maxId.ToString(), configFile);
                    return;
                }
                var added = newest.Where(g => g.Id > lastSeen).OrderBy(g => g.Id).ToList();
                if (added.Count == 0)
                    return;
                Preferences.Set(AppConfigKey.LastSeenRetroGameId, maxId.ToString(), configFile);

                string title = added.Count == 1 ? Loc.T("New retro game on SanctuaryVault") : Loc.F("{0} new retro games on SanctuaryVault", added.Count);
                string names = string.Join(", ", added.Take(5).Select(g => $"{g.Name} ({g.PlatformName ?? g.Platform})"));
                if (added.Count > 5)
                    names += ", ...";
                ToastMessageHelper.CreateToastMessage(title, names);
                MainWindowViewModel.Instance.AppBarText = Loc.F("{0}: {1}", title, names);
                Log.Info($"{title}: {names}");
            }
            catch (Exception ex) { Log.Ignored(ex); }
            finally { checking.Release(); }
        }
    }
}
