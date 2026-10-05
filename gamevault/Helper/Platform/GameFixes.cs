using GameVault.Core;
using GameVault.Core.Compatibility;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper.Platform
{
    /// <summary>
    /// Automatic per-game fixes on Linux, done right before a Windows game or installer starts:
    /// the game is looked up in the umu database (protonfixes) and missing winetricks components are installed.
    /// </summary>
    internal static class GameFixes
    {
        /// <summary>Looks the game up once (cached in its settings) and installs pending winetricks verbs.</summary>
        public static async Task PrepareAsync(string? installationDirectory, string? title, Action<string>? status = null)
        {
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(installationDirectory))
                return;
            var compatibility = GameCompatibility.ForInstallation(installationDirectory);
            if (compatibility.UmuIdSetting == "" && compatibility.DetectedUmuId == "" && !string.IsNullOrWhiteSpace(title))
                await LookUpAsync(compatibility, title);

            string[] pending = compatibility.PendingWinetricks;
            if (pending.Length > 0)
            {
                status?.Invoke($"Installing {string.Join(", ", pending)} with winetricks...");
                await RunWinetricksAsync(compatibility, pending);
            }
        }

        public static async Task<string?> LookUpAsync(GameCompatibility compatibility, string title)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                string? id = await UmuDatabase.LookupAsync(title, timeout.Token);
                compatibility.SetDetectedUmuId(id);
                Log.Info(id == null ? $"umu database: no entry for '{title}'" : $"umu database: '{title}' is {id}");
                return id;
            }
            catch (Exception ex)
            {
                // Offline or database unreachable: try again next time
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>
        /// Installs <paramref name="verbs"/> into the game's prefix with its own tool: "umu-run winetricks" for Proton
        /// when umu is installed, otherwise the system winetricks with WINE pointing at the tool's wine.
        /// </summary>
        public static async Task RunWinetricksAsync(GameCompatibility compatibility, string[] verbs)
        {
            CompatibilityTool tool = compatibility.ResolveTool();
            string prefix = compatibility.PrefixPath;
            Directory.CreateDirectory(prefix);
            var info = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = prefix };
            info.Environment["WINEPREFIX"] = prefix;
            string? umu = PlatformInfo.FindInPath("umu-run");
            bool proton = tool.Kind is CompatibilityToolKind.Proton or CompatibilityToolKind.UmuLatest;
            if (proton && umu != null)
            {
                info.FileName = umu;
                info.Environment["GAMEID"] = compatibility.EffectiveUmuId;
                if (tool.Kind == CompatibilityToolKind.Proton)
                    info.Environment["PROTONPATH"] = tool.Path!;
                info.ArgumentList.Add("winetricks");
            }
            else
            {
                info.FileName = PlatformInfo.FindInPath("winetricks")
                    ?? throw new InvalidOperationException("winetricks was not found. Install it (e.g. 'sudo apt install winetricks') to add components to the prefix.");
                string? wine = tool.Kind switch
                {
                    CompatibilityToolKind.Proton => Path.Combine(tool.Path!, "files", "bin", "wine"),
                    CompatibilityToolKind.Wine when tool.Id == CompatibilityToolId.SystemWine => tool.Path,
                    CompatibilityToolKind.Wine => Path.Combine(tool.Path!, "bin", "wine"),
                    _ => null,
                };
                if (wine != null)
                {
                    info.Environment["WINE"] = wine;
                    info.Environment["WINESERVER"] = Path.Combine(Path.GetDirectoryName(wine)!, "wineserver");
                }
            }
            info.ArgumentList.Add("-q");
            foreach (string verb in verbs)
                info.ArgumentList.Add(verb);

            Log.Info($"Starting {info.FileName} {string.Join(' ', info.ArgumentList)}");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("winetricks could not be started");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"winetricks failed (exit code {process.ExitCode}) for: {string.Join(", ", verbs)}");
            compatibility.MarkWinetricksApplied(verbs);
        }
    }
}
