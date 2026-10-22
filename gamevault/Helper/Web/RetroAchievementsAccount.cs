using GameVault.Core;
using gamevault.Localization;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The player's RetroAchievements account: signed in once here (the password is sent to RetroAchievements and
    /// forgotten, its token kept), RetroArch then unlocks the achievements as the games are played. With RomM linked,
    /// RomM gets the account's name and shows what was earned (the games' pages too).
    /// </summary>
    internal static class RetroAchievementsAccount
    {
        private const string Api = "https://retroachievements.org/dorequest.php";
        private static string Config => LoginManager.Instance.GetUserProfile().UserConfigFile;
        public static string User => Preferences.Get(AppConfigKey.RetroAchievementsUser, Config);
        private static string Token => Preferences.Get(AppConfigKey.RetroAchievementsToken, Config, true);
        public static bool IsSignedIn => User.Length > 0 && Token.Length > 0;
        /// <summary>For RetroArch's configuration; null when not signed in.</summary>
        public static (string User, string Token)? Account => IsSignedIn ? (User, Token) : null;

        /// <returns>Null when signed in, else why not.</returns>
        public static async Task<string?> SignInAsync(string user, string password)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["r"] = "login2", ["u"] = user.Trim(), ["p"] = password });
            using HttpResponseMessage response = await HttpClients.Shared.PostAsync(Api, form);
            string body = await response.Content.ReadAsStringAsync();
            JsonElement answer;
            try { answer = JsonDocument.Parse(body).RootElement.Clone(); }
            catch (JsonException) { return Loc.F("RetroAchievements answered {0}.", (int)response.StatusCode); }
            if (!(answer.TryGetProperty("Success", out JsonElement success) && success.ValueKind == JsonValueKind.True)
                || !answer.TryGetProperty("Token", out JsonElement token) || token.ValueKind != JsonValueKind.String)
                return answer.TryGetProperty("Error", out JsonElement error) && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : Loc.T("RetroAchievements refused the sign-in.");
            string name = answer.TryGetProperty("User", out JsonElement canonical) && canonical.ValueKind == JsonValueKind.String ? canonical.GetString()! : user.Trim();
            Preferences.Set(AppConfigKey.RetroAchievementsUser, name, Config);
            Preferences.Set(AppConfigKey.RetroAchievementsToken, token.GetString()!, Config, true);
            await SyncRommAsync();
            return null;
        }

        public static void SignOut()
        {
            Preferences.DeleteKey(AppConfigKey.RetroAchievementsUser, Config);
            Preferences.DeleteKey(AppConfigKey.RetroAchievementsToken, Config);
        }

        /// <summary>After a game: its achievements as RomM has them now, for the player's profile and the activity.</summary>
        public static async Task ShareAsync(RetroGame game)
        {
            if (!IsSignedIn || !RommAccount.IsLinked)
                return;
            try
            {
                var rom = await RommAccount.RomAsync(game.Id);
                var me = await RommAccount.MeAsync();
                await RetroLibrary.ShareAchievementsAsync(game, GameVault.Core.Retro.RommProgress.AchievementsOf(rom, me));
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>RomM takes the account's progression again (after a game, after the sign-in); nothing without RomM linked.</summary>
        public static async Task SyncRommAsync()
        {
            if (!IsSignedIn || !RommAccount.IsLinked)
                return;
            try { await RommAccount.SyncAchievementsAsync(User); }
            catch (Exception ex) { Log.Ignored(ex); }
        }
    }
}
