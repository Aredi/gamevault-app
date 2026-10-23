using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using GameVault.Core;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Runs the server's OAuth2 login in an embedded browser window (WebView2 on Windows, WebKitGTK on Linux).
    /// The server finishes the flow on a page whose body is the JSON token response, which is read back
    /// from the page once navigation completes.
    /// </summary>
    internal static class SsoAuthenticator
    {
        // WebView2 returns script results JSON encoded, WebKitGTK returns plain strings.
        private static string Unquote(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            string trimmed = value.Trim();
            return trimmed.StartsWith("\"") ? JsonSerializer.Deserialize<string>(trimmed) ?? "" : trimmed;
        }

        internal static AuthResponse? ParseTokenJson(string body)
        {
            if (!body.TrimStart().StartsWith("{"))
                return null;
            var authResponse = JsonSerializer.Deserialize<AuthResponse>(body);
            return string.IsNullOrEmpty(authResponse?.AccessToken) ? null : authResponse;
        }

        /// <summary>The server refused the sign-in (its error page, as {"message":..,"statusCode":401}).</summary>
        internal sealed class RefusedException(int statusCode, string message) : Exception(message)
        {
            public int StatusCode { get; } = statusCode;
        }

        /// <summary>The server's error page instead of the tokens: there is nothing more to wait for.</summary>
        internal static RefusedException? ParseServerError(string body)
        {
            if (!body.TrimStart().StartsWith("{"))
                return null;
            try
            {
                using var json = JsonDocument.Parse(body);
                if (!json.RootElement.TryGetProperty("statusCode", out JsonElement code) || !code.TryGetInt32(out int status) || status < 400)
                    return null;
                string message = json.RootElement.TryGetProperty("message", out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : "";
                return new RefusedException(status, message);
            }
            catch (JsonException) { return null; }
        }

        internal static AuthResponse? TryReadTokensFromUrl(Uri? url)
        {
            if (url == null || string.IsNullOrEmpty(url.Query))
                return null;
            string? access = null, refresh = null;
            foreach (string part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = part.IndexOf('=');
                if (separator < 0)
                    continue;
                string key = part[..separator];
                string value = Uri.UnescapeDataString(part[(separator + 1)..].Replace('+', ' '));
                if (key == "access_token") access = value;
                else if (key == "refresh_token") refresh = value;
            }
            return string.IsNullOrEmpty(access) ? null : new AuthResponse { AccessToken = access, RefreshToken = refresh ?? "" };
        }

        public static async Task<AuthResponse?> AuthenticateAsync(string serverUrl, string webDataDirectory)
        {
            if (Dispatcher.UIThread.CheckAccess())
                return await RunAsync(serverUrl, webDataDirectory);
            return await await Dispatcher.UIThread.InvokeAsync<Task<AuthResponse?>>(() => RunAsync(serverUrl, webDataDirectory));
        }

        private static async Task<AuthResponse?> RunAsync(string serverUrl, string webDataDirectory)
        {
            var tcs = new TaskCompletionSource<AuthResponse?>();
            var webView = new NativeWebView();
            var window = new Window
            {
                Title = "SanctuaryVault - " + gamevault.Localization.Loc.T("Sign In"),
                Width = 800,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = webView,
            };

            webView.EnvironmentRequested += (_, e) =>
            {
                // A fresh, private session each time (the WPF client cleared all cookies before signing in).
                switch (e)
                {
                    case GtkWebViewEnvironmentRequestedEventArgs gtk:
                        gtk.EphemeralDataManager = true;
                        gtk.BaseDataDirectory = webDataDirectory;
                        break;
                    case WindowsWebView2EnvironmentRequestedEventArgs webView2:
                        webView2.UserDataFolder = webDataDirectory;
                        webView2.IsInPrivateModeEnabled = true;
                        break;
                }
            };

            gamevault.Helper.Platform.NativeWebViewPlacement.KeepPlaced(webView);
            // After the token page the server redirects to "<origin>?access_token=..&refresh_token=.." (its web UI).
            // Taking the tokens from that URL does not depend on reading the page in time.
            webView.NavigationStarted += (_, e) =>
            {
                AuthResponse? fromUrl = TryReadTokensFromUrl(e.Request);
                if (fromUrl == null || tcs.Task.IsCompleted)
                    return;
                e.Cancel = true;
                tcs.TrySetResult(fromUrl);
                window.Close();
            };
            webView.NavigationCompleted += async (_, e) =>
            {
                // WebKitGTK reports completion before the token page is parsed, and the page's JavaScript redirect
                // raises no navigation event, so the page is polled for a moment: the token JSON in its body,
                // or the tokens in the URL it redirected to.
                for (int attempt = 0; attempt < 25 && !tcs.Task.IsCompleted; attempt++)
                {
                    try
                    {
                        string body = Unquote(await webView.InvokeScript("(document.getElementById('jsonData') || document.body || {}).textContent || ''"));
                        AuthResponse? authResponse = TryReadTokensFromUrl(Uri.TryCreate(Unquote(await webView.InvokeScript("window.location.href")), UriKind.Absolute, out Uri? href) ? href : null)
                            ?? ParseTokenJson(body);
                        if (authResponse != null)
                        {
                            tcs.TrySetResult(authResponse);
                            window.Close();
                            return;
                        }
                        // A refusal (a 401 at the server's return): the window closes on it, the login is not left waiting
                        if (ParseServerError(body) is RefusedException refused)
                        {
                            tcs.TrySetException(refused);
                            window.Close();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        // The page is changing, try again.
                        Log.Ignored(ex);
                    }
                    await Task.Delay(200);
                }
            };

            window.Closed += (_, _) => tcs.TrySetResult(null);
            window.Opened += (_, _) => webView.Navigate(new Uri($"{serverUrl}/api/auth/oauth2/login"));
            window.Show();
            return await tcs.Task;
        }
    }
}
