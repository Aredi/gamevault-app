using GameVault.Core;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// The ZeroTier One service of this PC (installed once, with the administrator's rights), through its local JSON API:
    /// its address, the rooms' networks it joins and leaves, how it reaches the other players' PCs. The API asks for the
    /// service's token, which only an administrator reads: a copy is made once for this player.
    /// </summary>
    internal static class ZeroTierAgent
    {
        public const string WindowsInstaller = "https://download.zerotier.com/dist/ZeroTier%20One.msi";
        public const string LinuxInstall = "curl -s https://install.zerotier.com | sudo bash";

        private static string Home => PlatformInfo.IsWindows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZeroTier", "One")
            : "/var/lib/zerotier-one";
        private static string OwnToken => Path.Combine(PlatformInfo.LocalDataDirectory, "zerotier-authtoken");

        /// <summary>The service's API; the tests' fake one.</summary>
        internal static string ApiUrl { get; set; } = "http://127.0.0.1:9993";
        /// <summary>The tests' fake service: installed, its token known.</summary>
        internal static string? TestToken { get; set; }

        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static bool IsInstalled => TestToken != null || Directory.Exists(Home);

        /// <summary>The token: this player's copy, else one the service lets users read (Linux's zerotier-cli).</summary>
        private static string? Token()
        {
            if (TestToken != null)
                return TestToken;
            foreach (string file in new[] { OwnToken, Path.Combine(Home, "authtoken.secret"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zeroTierOneAuthToken") })
            {
                try
                {
                    if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } token)
                        return token;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            }
            return null;
        }

        public static bool HasAccess => Token() != null;

        /// <summary>Copies the service's token for this player, asking for the administrator's rights (Windows' prompt, pkexec).</summary>
        public static async Task GrantAccessAsync()
        {
            Directory.CreateDirectory(PlatformInfo.LocalDataDirectory);
            string source = Path.Combine(Home, "authtoken.secret");
            if (OperatingSystem.IsWindows())
            {
                // Written by the administrator in the player's folder: the player reads it
                await RunAsAdministratorAsync($"Copy-Item -LiteralPath '{source}' -Destination '{OwnToken.Replace("'", "''")}' -Force");
            }
            else
            {
                var info = new ProcessStartInfo("pkexec", new[] { "cat", source }) { RedirectStandardOutput = true, UseShellExecute = false };
                using Process process = Process.Start(info) ?? throw new InvalidOperationException(Loc.T("The administrator's rights were not given."));
                string token = (await process.StandardOutput.ReadToEndAsync()).Trim();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 || token.Length == 0)
                    throw new InvalidOperationException(Loc.T("The administrator's rights were not given."));
                await File.WriteAllTextAsync(OwnToken, token);
                File.SetUnixFileMode(OwnToken, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            if (!HasAccess)
                throw new InvalidOperationException(Loc.T("The administrator's rights were not given."));
        }

        /// <summary>Windows: ZeroTier's installer, from ZeroTier's site, with the administrator's rights.</summary>
        public static async Task InstallAsync(Action<string> status)
        {
            string msi = Path.Combine(Path.GetTempPath(), "ZeroTierOne.msi");
            status(Loc.T("Downloading ZeroTier..."));
            using (HttpResponseMessage response = await new HttpClient { Timeout = TimeSpan.FromMinutes(5) }.GetAsync(WindowsInstaller))
            {
                response.EnsureSuccessStatusCode();
                await using FileStream file = File.Create(msi);
                await response.Content.CopyToAsync(file);
            }
            status(Loc.T("Installing ZeroTier: accept Windows' prompt..."));
            using Process process = Process.Start(new ProcessStartInfo("msiexec", $"/i \"{msi}\" /qb /norestart") { UseShellExecute = true, Verb = "runas" })
                ?? throw new InvalidOperationException(Loc.T("ZeroTier could not be installed."));
            await process.WaitForExitAsync();
            // 3010: installed, a restart is advised
            if (process.ExitCode is not (0 or 3010))
                throw new InvalidOperationException(Loc.T("ZeroTier could not be installed."));
        }

        /// <summary>
        /// Windows: the rooms' network adapters first (the games announce their parties on the first one) and private
        /// (the firewall lets the LAN games in), in one prompt of the administrator's rights.
        /// </summary>
        public static Task PrioritizeAsync() => PlatformInfo.IsWindows
            ? RunAsAdministratorAsync("Get-NetAdapter | Where-Object InterfaceDescription -like 'ZeroTier*' | ForEach-Object { Set-NetIPInterface -InterfaceIndex $_.ifIndex -InterfaceMetric 1; Set-NetConnectionProfile -InterfaceIndex $_.ifIndex -NetworkCategory Private }")
            : Task.CompletedTask;

        private static async Task RunAsAdministratorAsync(string script)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var info = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            Process? process;
            try { process = Process.Start(info); }
            // The player said no to Windows' prompt
            catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException(Loc.T("The administrator's rights were not given.")); }
            if (process == null)
                throw new InvalidOperationException(Loc.T("The administrator's rights were not given."));
            using (process)
                await process.WaitForExitAsync();
        }

        private static async Task<JsonElement> SendAsync(HttpMethod method, string path, bool withBody = false)
        {
            string token = Token() ?? throw new InvalidOperationException(Loc.T("SanctuaryVault can not use ZeroTier yet."));
            using var request = new HttpRequestMessage(method, $"{ApiUrl}/{path}") { Content = withBody ? new StringContent("{}", Encoding.UTF8, "application/json") : null };
            request.Headers.Add("X-ZT1-Auth", token);
            using HttpResponseMessage response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string text = await response.Content.ReadAsStringAsync();
            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        }

        /// <summary>This PC's ZeroTier address (10 hexadecimal digits), and whether it reaches ZeroTier's servers.</summary>
        public static async Task<(string Address, bool Online)> StatusAsync()
        {
            JsonElement status = await SendAsync(HttpMethod.Get, "status");
            return (status.GetProperty("address").GetString() ?? "", status.TryGetProperty("online", out JsonElement online) && online.GetBoolean());
        }

        public static Task JoinAsync(string networkId) => SendAsync(HttpMethod.Post, $"network/{networkId}", withBody: true);

        public static async Task LeaveAsync(string networkId)
        {
            try { await SendAsync(HttpMethod.Delete, $"network/{networkId}"); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }

        /// <summary>The joined networks: "OK" when this PC got its address, else "REQUESTING_CONFIGURATION", "ACCESS_DENIED"...</summary>
        public static async Task<Dictionary<string, string>> NetworksAsync() =>
            (await SendAsync(HttpMethod.Get, "network")).EnumerateArray().ToDictionary(n => n.GetProperty("id").GetString() ?? "", n => n.GetProperty("status").GetString() ?? "");

        public sealed record Peer(int Latency, bool Direct);

        /// <summary>The PCs this one talks to, by ZeroTier address: their latency, and whether straight or through a relay.</summary>
        public static async Task<Dictionary<string, Peer>> PeersAsync() =>
            (await SendAsync(HttpMethod.Get, "peer")).EnumerateArray().ToDictionary(
                p => p.GetProperty("address").GetString() ?? "",
                p => new Peer(p.TryGetProperty("latency", out JsonElement latency) ? latency.GetInt32() : -1,
                              p.TryGetProperty("paths", out JsonElement paths) && paths.EnumerateArray().Any(path => path.TryGetProperty("active", out JsonElement active) && active.GetBoolean())));
    }
}
