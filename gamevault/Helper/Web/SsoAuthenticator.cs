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
                Title = "GameVault - Sign in",
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

            webView.NavigationCompleted += async (_, e) =>
            {
                if (tcs.Task.IsCompleted)
                    return;
                try
                {
                    string? content = await webView.InvokeScript("document.body.innerText");
                    if (string.IsNullOrWhiteSpace(content))
                        return;
                    // WebView2 returns the script result JSON encoded, WebKitGTK returns the plain string.
                    string body = content.TrimStart().StartsWith("\"") ? JsonSerializer.Deserialize<string>(content) ?? "" : content;
                    if (!body.TrimStart().StartsWith("{"))
                        return;
                    var authResponse = JsonSerializer.Deserialize<AuthResponse>(body);
                    if (!string.IsNullOrEmpty(authResponse?.AccessToken))
                    {
                        tcs.TrySetResult(authResponse);
                        window.Close();
                    }
                }
                catch (Exception ex)
                {
                    // Not the token page yet, keep navigating.
                    Log.Ignored(ex);
                }
            };

            window.Closed += (_, _) => tcs.TrySetResult(null);
            window.Opened += (_, _) => webView.Navigate(new Uri($"{serverUrl}/api/auth/oauth2/login"));
            window.Show();
            return await tcs.Task;
        }
    }
}
