using GameVault.Core;
using GameVault.Core.Library;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The players' customized profiles, kept by the SanctuaryVault service next to the server (the one "Publish a
    /// Game" uses). Without it every profile is the default one and can not be customized.
    /// </summary>
    internal static class ProfileService
    {
        public record LoadResult(ProfileDocument Profile, bool Available, string? Problem);

        private static string? resolved;
        private static DateTime missingUntil;
        private static readonly Dictionary<int, ProfileDocument> cache = new();

        /// <summary>The address entered in "Publish a Game" or on the profile page.</summary>
        public static string ConfiguredUrl
        {
            get
            {
                try { return Preferences.Get(AppConfigKey.PublishUploaderUrl, LoginManager.Instance.GetUserProfile().UserConfigFile)?.Trim() ?? ""; }
                catch (Exception ex) { Log.Ignored(ex); return ""; }
            }
            set
            {
                Preferences.Set(AppConfigKey.PublishUploaderUrl, value.Trim(), LoginManager.Instance.GetUserProfile().UserConfigFile);
                resolved = null;
                missingUntil = default;
                cache.Clear();
            }
        }

        /// <summary>The service: the address set, else the server's host on the service's usual port (7477).</summary>
        private static async Task<string?> ResolveAsync()
        {
            if (resolved != null)
                return resolved;
            // Not looked for again at every profile shown
            if (DateTime.UtcNow < missingUntil)
                return null;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(ConfiguredUrl))
                candidates.Add(ConfiguredUrl);
            else if (Uri.TryCreate(SettingsViewModel.Instance.ServerUrl, UriKind.Absolute, out Uri? server))
                candidates.Add($"http://{server.Host}:7477");
            foreach (string candidate in candidates)
            {
                if (await HasProfilesAsync(candidate))
                    return resolved = candidate.TrimEnd('/');
            }
            missingUntil = DateTime.UtcNow.AddMinutes(1);
            return null;
        }

        /// <summary>Whether the service at this address keeps profiles.</summary>
        public static async Task<bool> HasProfilesAsync(string url)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                using JsonDocument status = JsonDocument.Parse(await client.GetStringAsync(url.TrimEnd('/') + "/status"));
                return status.RootElement.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.True;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return false;
            }
        }

        public static async Task<LoadResult> LoadAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(userId, out ProfileDocument? known))
                return new LoadResult(known.Clone(), true, null);
            string? service = await ResolveAsync();
            if (service == null)
                return new LoadResult(ProfileDocument.Default(), false, Loc.T("The SanctuaryVault service was not found: profiles can not be customized."));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{service}/profiles/{userId}");
                using HttpResponseMessage response = await WebHelper.SendLongAsync(request, cancellationToken);
                ProfileDocument profile = response.StatusCode == HttpStatusCode.NotFound
                    ? ProfileDocument.Default()
                    : response.IsSuccessStatusCode
                        ? ProfileDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
                        : throw new HttpRequestException(await ErrorOf(response), null, response.StatusCode);
                cache[userId] = profile;
                return new LoadResult(profile.Clone(), true, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Ignored(ex);
                return new LoadResult(ProfileDocument.Default(), false, ex.Message);
            }
        }

        public static async Task SaveAsync(int userId, ProfileDocument profile)
        {
            string service = await ResolveAsync() ?? throw new InvalidOperationException(Loc.T("The SanctuaryVault service was not found: profiles can not be customized."));
            ProfileDocument clean = profile.Sanitized();
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{service}/profiles/{userId}")
            {
                Content = new StringContent(clean.ToJson(), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await WebHelper.SendLongAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(await ErrorOf(response), null, response.StatusCode);
            cache[userId] = clean;
        }

        private static async Task<string> ErrorOf(HttpResponseMessage response)
        {
            try
            {
                using JsonDocument error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (error.RootElement.TryGetProperty("error", out var message))
                    return message.GetString() ?? response.ReasonPhrase ?? "";
            }
            catch (JsonException) { }
            return $"{(int)response.StatusCode} {response.ReasonPhrase}";
        }

        /// <summary>Forgets what was read (reload of a profile).</summary>
        public static void Forget(int userId) => cache.Remove(userId);
    }
}
