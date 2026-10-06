using GameVault.Core;
using GameVault.Core.Diagnostics;
using GameVault.Core.Library;
using gamevault.Helper.Platform;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// A zip a user sends to the administrator when something goes wrong: versions, settings, installed games,
    /// downloads, tools and the recent logs. Passwords, tokens and API keys are never part of it (see <see cref="Redactor"/>).
    /// </summary>
    internal static class ProblemReport
    {
        public static async Task<string> CreateAsync(string? folder = null)
        {
            folder ??= DefaultFolder();
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"GameVault-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            var entries = new Dictionary<string, string> { ["report.txt"] = await DescribeAsync() };

            if (Log.LogFile != null)
            {
                if (File.Exists(Log.LogFile))
                    entries["logs/gamevault.log"] = Redactor.Tail(Log.LogFile, 2 * 1024 * 1024);
                if (File.Exists(Log.LogFile + ".1"))
                    entries["logs/gamevault.log.1"] = Redactor.Tail(Log.LogFile + ".1", 1024 * 1024);
            }
            if (Directory.Exists(ProfileManager.ErrorLogDir))
            {
                // Crash reports of the last two weeks
                foreach (var crash in new DirectoryInfo(ProfileManager.ErrorLogDir).GetFiles("GameVault_ErrorLog_*.txt")
                    .Where(f => f.LastWriteTime > DateTime.Now.AddDays(-14)).OrderByDescending(f => f.LastWriteTime).Take(10))
                    entries[$"crashes/{crash.Name}"] = Redactor.Tail(crash.FullName, 256 * 1024);
            }

            await Task.Run(() =>
            {
                string temp = file + ".tmp";
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    foreach (var (name, content) in entries)
                    {
                        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                        writer.Write(Redactor.Redact(content));
                    }
                }
                File.Move(temp, file, true);
            });
            Log.Info($"Problem report created: {file}");
            return file;
        }

        private static string DefaultFolder()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Directory.Exists(desktop) ? desktop : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static async Task<string> DescribeAsync()
        {
            var text = new StringBuilder();
            void Line(string label, object? value) => text.AppendLine($"{label,-28}{value}");
            void Section(string title) => text.AppendLine().AppendLine($"== {title} ==");
            var settings = SettingsViewModel.Instance;

            Section("Application");
            Line("Version", settings.Version);
            Line("Installed by", AppUpdater.IsInstalled ? $"installer (updates itself, {AppUpdater.CurrentVersion})" : "portable / development build");
            Line("Language", Localization.Loc.Language);
            Line("System", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            Line(".NET", RuntimeInformation.FrameworkDescription);
            Line("Report created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));

            Section("Server");
            Line("Address", settings.ServerUrl);
            Line("Signed in", LoginManager.Instance.IsLoggedIn() ? $"yes, as {LoginManager.Instance.GetCurrentUser()?.Username} ({LoginManager.Instance.GetCurrentUser()?.Role})" : "no (offline)");
            Line("Offline mode", MainWindowViewModel.Instance.IsOffline);
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                Line("Status", (await client.GetStringAsync($"{settings.ServerUrl}/api/status")).Trim());
            }
            catch (Exception ex) { Line("Status", $"not reachable: {ex.Message}"); }

            Section("Settings");
            foreach (var root in settings.RootDirectories ?? new())
            {
                var drive = PlatformInfo.GetDriveForPath(root.Uri);
                string free = drive != null ? GameVault.Core.Storage.StorageCleanup.FormatSize(drive.AvailableFreeSpace) : "?";
                Line("Root directory", $"{root.Uri} ({free} free{(Directory.Exists(root.Uri) ? "" : ", MISSING")})");
            }
            Line("Auto extract / install", $"{settings.AutoExtract} / {settings.AutoInstallPortable}");
            Line("Simultaneous downloads", settings.MaxConcurrentDownloads == 0 ? "unlimited" : settings.MaxConcurrentDownloads);
            Line("Connections per download", settings.DownloadConnections);
            Line("Download limit (KB/s)", settings.DownloadLimit);
            Line("Download schedule", settings.DownloadScheduleEnabled ? $"{settings.DownloadScheduleStart}-{settings.DownloadScheduleEnd}" : "off");
            Line("Cloud saves", settings.CloudSaves);
            Line("Ignored executables", (settings.IgnoreList ?? Array.Empty<string>()).Length);
            if (PlatformInfo.IsLinux)
            {
                Line("Default compatibility tool", CompatibilityManager.DisplayName(CompatibilitySettings.DefaultToolId));
                Line("Shared Wine prefix", CompatibilitySettings.EffectiveWinePrefix);
            }

            Section("Tools");
            Line("7-Zip", ToolLocator.SevenZip() ?? "not found");
            Line("Ludusavi (cloud saves)", ToolLocator.Ludusavi()?.FileName ?? "not found");
            if (PlatformInfo.IsLinux)
            {
                Line("wine", PlatformInfo.FindInPath("wine") ?? "not found");
                Line("umu-run", PlatformInfo.FindInPath("umu-run") ?? "not found");
                Line("winetricks", PlatformInfo.FindInPath("winetricks") ?? "not found");
            }

            Section($"Installed games ({InstallViewModel.Instance.InstalledGames.Count})");
            foreach (var (game, directory) in InstallViewModel.Instance.InstalledGames.ToList())
            {
                InstalledVersion installed = InstalledGameState.Read(directory);
                string settingsFile = Path.Combine(directory, "gamevault-exec");
                string executable = File.Exists(settingsFile) ? Preferences.Get(AppConfigKey.Executable, settingsFile) : "";
                text.AppendLine($"#{game.ID} {game.Title} [{game.Type}] installed {installed.Version ?? "?"}, server {game.Version ?? "?"}{(InstalledGameState.HasUpdate(game, directory) ? " (update available)" : "")}");
                text.AppendLine($"    folder: {directory}");
                text.AppendLine($"    executable: {(executable == "" ? "not chosen yet" : executable + (File.Exists(executable) ? "" : " (MISSING)"))}");
                if (PlatformInfo.IsLinux)
                    text.AppendLine($"    compatibility tool: {CompatibilityManager.DisplayName(GameCompatibility.ForInstallation(directory).ToolId)}");
            }

            Section($"Downloads ({DownloadsViewModel.Instance.DownloadedGames.Count})");
            foreach (var download in DownloadsViewModel.Instance.DownloadedGames.ToList())
                text.AppendLine($"#{download.GetGameId()} {download.GameTitle}: {download.StateText}{(download.IsDownloading() ? $" ({download.GetDownloadProgress()} %)" : "")}{(DownloadQueue.IsWaiting(download) ? " [queued]" : "")}");

            return text.ToString();
        }
    }
}
