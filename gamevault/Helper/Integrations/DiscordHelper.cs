using DiscordRPC;
using gamevault.Converter;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Integrations;
using GameVault.Core.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// Discord Rich Presence: "Playing SanctuaryVault" with the game, its total play time, a timer for the session
    /// and its cover. The activity's name is the name of the Discord application whose id is used.
    /// </summary>
    internal class DiscordHelper
    {
        #region Singleton
        private static DiscordHelper? instance;
        private static readonly object padlock = new object();

        public static DiscordHelper Instance
        {
            get
            {
                lock (padlock)
                {
                    return instance ??= new DiscordHelper();
                }
            }
        }
        #endregion

        /// <summary>The SanctuaryVault Discord application: "Playing SanctuaryVault".</summary>
        public const string DefaultApplicationId = "1557729594478559242";

        private DiscordRpcClient? client;
        private string? clientApplicationId;
        private int currentGameId = -1;
        private DateTime sessionStartUtc;
        private int minutesBeforeSession;

        /// <summary>The application id from the settings, or the default one.</summary>
        public static string ApplicationId
        {
            get
            {
                string? configured = null;
                try { configured = Preferences.Get(AppConfigKey.DiscordApplicationId, LoginManager.Instance.GetUserProfile().UserConfigFile)?.Trim(); }
                catch (Exception ex) { Log.Ignored(ex); }
                return !string.IsNullOrEmpty(configured) && configured.All(char.IsDigit) ? configured : DefaultApplicationId;
            }
        }

        private DiscordRpcClient? Client()
        {
            string applicationId = ApplicationId;
            if (client != null && clientApplicationId == applicationId)
                return client;
            Dispose();
            try
            {
                client = new DiscordRpcClient(applicationId);
                client.Initialize();
                clientApplicationId = applicationId;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                client = null;
            }
            return client;
        }

        /// <summary>A game was started from SanctuaryVault: show it at once instead of at the next minute.</summary>
        internal void GameStarted(int gameId)
        {
            if (SettingsViewModel.Instance.SyncDiscordPresence)
                Show(gameId);
        }

        /// <summary>Called every minute with the games running: shows the first one, clears when none runs.</summary>
        internal void SyncGameWithDiscordPresence(List<int> trackedGameIds, Dictionary<int, string> installedGames)
        {
            try
            {
                if (!SettingsViewModel.Instance.SyncDiscordPresence)
                {
                    Clear();
                    return;
                }
                if (trackedGameIds.Count == 0)
                {
                    Clear();
                    return;
                }
                int gameId = trackedGameIds.Contains(currentGameId) ? currentGameId : trackedGameIds[0];
                Show(gameId, installedGames.GetValueOrDefault(gameId));
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        private void Show(int gameId, string? installFolder = null)
        {
            try
            {
                if (gameId != currentGameId)
                {
                    currentGameId = gameId;
                    sessionStartUtc = DateTime.UtcNow;
                    minutesBeforeSession = InstallViewModel.Instance.PlayRecords.TryGetValue(gameId, out PlayRecord? record) ? record.MinutesPlayed : 0;
                }
                Game? game = InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key?.ID == gameId).Key;
                string title = game?.Metadata?.Title is { Length: > 0 } mapped ? mapped
                    : game?.Title ?? FolderTitle(gameId, installFolder);
                var activity = DiscordActivity.Create(title, minutesBeforeSession, sessionStartUtc, DateTime.UtcNow, game?.Metadata?.Cover?.Source,
                    minutes => Loc.F("{0} played in total", (string)new GameTimeConverter().Convert(minutes, typeof(string), null, CultureInfo.CurrentCulture)!));

                Client()?.SetPresence(new RichPresence
                {
                    Details = activity.Details,
                    State = activity.State,
                    Timestamps = new Timestamps(activity.SessionStartUtc),
                    Assets = new Assets
                    {
                        LargeImageKey = activity.LargeImage,
                        LargeImageText = activity.LargeImageText,
                        SmallImageKey = activity.LargeImage == DiscordActivity.LogoAsset ? null : DiscordActivity.LogoAsset,
                        SmallImageText = "SanctuaryVault",
                    },
                    Buttons = new[] { new Button { Label = "SanctuaryVault", Url = $"https://github.com/{AppRepository.Owner}/{AppRepository.Name}" } },
                });
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>"(12)Hades" → "Hades", when the game is not known (offline without cache).</summary>
        private static string FolderTitle(int gameId, string? folder) =>
            folder == null ? "" : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)).Replace($"({gameId})", "").Trim();

        internal void Clear()
        {
            if (currentGameId == -1)
                return;
            currentGameId = -1;
            try { client?.ClearPresence(); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>The settings changed (turned off, other application id).</summary>
        internal void SettingsChanged()
        {
            int game = currentGameId;
            Clear();
            if (clientApplicationId != ApplicationId)
                Dispose();
            if (game != -1 && SettingsViewModel.Instance.SyncDiscordPresence)
                Show(game);
        }

        internal void Dispose()
        {
            try { client?.Dispose(); }
            catch (Exception ex) { Log.Ignored(ex); }
            client = null;
            clientApplicationId = null;
        }
    }
}
