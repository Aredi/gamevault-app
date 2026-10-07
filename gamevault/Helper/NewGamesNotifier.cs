using gamevault.Localization;
using GameVault.Core;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Checks the server for games added since the last check and shows a desktop notification.
    /// The newest game id seen is stored per profile, so games added while GameVault was closed are reported too.
    /// </summary>
    internal static class NewGamesNotifier
    {
        private static Timer? timer;
        private static readonly SemaphoreSlim checking = new(1, 1);

        public static void Start()
        {
            timer ??= new Timer(async _ => await CheckAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        }

        public static async Task CheckAsync()
        {
            if (!SettingsViewModel.Instance.NotifyNewGames || !LoginManager.Instance.IsLoggedIn())
                return;
            if (!await checking.WaitAsync(0))
                return;
            try
            {
                string configFile = LoginManager.Instance.GetUserProfile().UserConfigFile;
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?sortBy=id:DESC&limit=20");
                Game[] newest = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                if (newest.Length == 0)
                    return;
                int maxId = newest.Max(g => g.ID);
                // First check of this profile: remember the current state without notifying about the whole library
                if (!int.TryParse(Preferences.Get(AppConfigKey.LastSeenGameId, configFile), out int lastSeen))
                {
                    Preferences.Set(AppConfigKey.LastSeenGameId, maxId.ToString(), configFile);
                    return;
                }
                var added = newest.Where(g => g.ID > lastSeen).OrderBy(g => g.ID).ToList();
                if (added.Count == 0)
                    return;
                Preferences.Set(AppConfigKey.LastSeenGameId, maxId.ToString(), configFile);

                string title = added.Count == 1 ? Loc.T("New game on SanctuaryVault") : Loc.F("{0} new games on SanctuaryVault", added.Count);
                string names = string.Join(", ", added.Take(5).Select(g => g.Title));
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
