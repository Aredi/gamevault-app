using gamevault.Localization;
using GameVault.Core;
using GameVault.Core.Compatibility;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace gamevault.Helper.Platform
{
    /// <summary>
    /// Compatibility tools (Proton / Wine builds) on Linux: which ones are installed, which one a game uses,
    /// and where the game's Wine prefix is. Comparable to the "Force a specific compatibility tool"
    /// option of Steam on Linux / the Steam Deck.
    /// </summary>
    internal static class CompatibilityManager
    {
        /// <summary>Tools downloaded by GameVault (~/.local/share/GameVault/compatibilitytools.d).</summary>
        public static string ManagedToolsDirectory => Path.Combine(PlatformInfo.LocalDataDirectory, "compatibilitytools.d");
        /// <summary>Per-game prefixes (~/.local/share/GameVault/prefixes/&lt;game id&gt;), like Steam's compatdata.</summary>
        public static string PrefixesDirectory => Path.Combine(PlatformInfo.LocalDataDirectory, "prefixes");

        private static List<CompatibilityTool>? cachedTools;
        private static string? systemWineVersion;

        /// <summary>All selectable tools: automatic choice, umu, system Wine, every detected build, custom command.</summary>
        public static IReadOnlyList<CompatibilityTool> GetTools(bool refresh = false)
        {
            if (cachedTools != null && !refresh)
                return cachedTools;

            var tools = new List<CompatibilityTool>();
            string? umu = PlatformInfo.FindInPath("umu-run");
            string? wine = PlatformInfo.FindInPath("wine");
            tools.Add(new CompatibilityTool(CompatibilityToolId.Auto, Loc.T("Automatic"), umu != null ? CompatibilityToolKind.UmuLatest : CompatibilityToolKind.Wine, null,
                umu != null ? Loc.T("latest GE-Proton through umu-run") : Loc.T("system Wine"), false));
            if (umu != null)
                tools.Add(new CompatibilityTool(CompatibilityToolId.UmuLatest, Loc.T("Latest GE-Proton (umu)"), CompatibilityToolKind.UmuLatest, null, Loc.T("downloaded and updated by umu-run"), false));
            if (wine != null)
                tools.Add(new CompatibilityTool(CompatibilityToolId.SystemWine, Loc.T("System Wine") + (SystemWineVersion(wine) is string v ? $" ({v})" : ""), CompatibilityToolKind.Wine, wine, Loc.T("System"), false));
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                tools.AddRange(CompatibilityToolScanner.Scan(ToolSearchLocations.ForUser(home, ManagedToolsDirectory)));
            }
            catch (Exception ex) { Log.Error(ex, "Scanning compatibility tools failed"); }
            tools.Add(new CompatibilityTool(CompatibilityToolId.Custom, Loc.T("Custom command"), CompatibilityToolKind.Custom, null, Loc.T("Settings → Linux"), false));
            cachedTools = tools;
            return tools;
        }

        public static CompatibilityTool? Find(string id) => GetTools().FirstOrDefault(t => t.Id == id)
            // A build that disappeared since the last scan (or was installed meanwhile)
            ?? GetTools(refresh: true).FirstOrDefault(t => t.Id == id);

        /// <summary>
        /// The tool that really starts a program for <paramref name="id"/>: "Automatic" becomes umu or the system Wine.
        /// </summary>
        public static CompatibilityTool Resolve(string id)
        {
            if (string.IsNullOrEmpty(id))
                id = CompatibilitySettings.DefaultToolId;
            if (id == CompatibilityToolId.Auto)
                id = PlatformInfo.FindInPath("umu-run") != null ? CompatibilityToolId.UmuLatest : CompatibilityToolId.SystemWine;

            CompatibilityTool? tool = Find(id);
            if (tool != null)
                return tool;
            if (CompatibilityToolId.IsProton(id, out string protonDir) || CompatibilityToolId.IsWineBuild(id, out protonDir))
                throw new InvalidOperationException(Loc.F("The compatibility tool '{0}' was not found. Choose another one in the game settings or in Settings → Linux.", protonDir));
            if (id == CompatibilityToolId.UmuLatest)
                throw new InvalidOperationException(Loc.T("umu-run was not found. Install umu-launcher or choose another compatibility tool in Settings → Linux."));
            throw new InvalidOperationException(Loc.T("Wine was not found. Install Wine (e.g. 'sudo apt install wine') or download a Wine/Proton build in Settings → Linux."));
        }

        public static string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return Loc.T("Default");
            if (Find(id) is CompatibilityTool tool)
                return tool.Name;
            return CompatibilityToolId.IsProton(id, out string dir) || CompatibilityToolId.IsWineBuild(id, out dir)
                ? Loc.F("{0} (missing)", Path.GetFileName(dir))
                : id;
        }

        public static void Delete(CompatibilityTool tool)
        {
            if (!tool.IsManaged || tool.Path == null)
                throw new InvalidOperationException(Loc.F("{0} was not installed by GameVault and is left untouched.", tool.Name));
            Directory.Delete(tool.Path, true);
            GetTools(refresh: true);
        }

        private static string? SystemWineVersion(string wine)
        {
            if (systemWineVersion != null)
                return systemWineVersion.Length == 0 ? null : systemWineVersion;
            systemWineVersion = "";
            try
            {
                using var process = Process.Start(new ProcessStartInfo(wine, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                if (process != null && process.WaitForExit(3000))
                    systemWineVersion = process.StandardOutput.ReadToEnd().Trim();
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return systemWineVersion.Length == 0 ? null : systemWineVersion;
        }
    }

    public enum WinePrefixMode
    {
        /// <summary>The prefix of Settings → Linux, shared by all games.</summary>
        Shared,
        /// <summary>A prefix of its own (~/.local/share/GameVault/prefixes/&lt;game id&gt;).</summary>
        Game,
    }

    /// <summary>
    /// Compatibility settings of one installed game, stored in its gamevault-exec file next to the launch parameters.
    /// </summary>
    internal class GameCompatibility
    {
        public string? InstallationDirectory { get; private init; }
        public int? GameId { get; private init; }
        /// <summary>Tool chosen for this game, empty = the global default.</summary>
        public string ToolId { get; private set; } = CompatibilityToolId.Default;
        public WinePrefixMode PrefixMode { get; private set; } = WinePrefixMode.Shared;

        private string? SettingsFile => InstallationDirectory == null ? null : Path.Combine(InstallationDirectory, "gamevault-exec");

        /// <summary>
        /// Settings of the game installed in <paramref name="installationDirectory"/> ("…/Installations/(42)Title"),
        /// or the global defaults when it is null (e.g. programs started outside of a game).
        /// </summary>
        public static GameCompatibility ForInstallation(string? installationDirectory)
        {
            if (string.IsNullOrEmpty(installationDirectory))
                return new GameCompatibility();
            var compatibility = new GameCompatibility
            {
                InstallationDirectory = installationDirectory,
                GameId = ParseGameId(installationDirectory),
            };
            string file = compatibility.SettingsFile!;
            if (File.Exists(file))
            {
                compatibility.ToolId = Preferences.Get(AppConfigKey.GameCompatibilityTool, file);
                compatibility.PrefixMode = Preferences.Get(AppConfigKey.GameWinePrefixMode, file) == WinePrefixMode.Game.ToString() ? WinePrefixMode.Game : WinePrefixMode.Shared;
            }
            return compatibility;
        }

        public static int? ParseGameId(string installationDirectory)
        {
            Match match = Regex.Match(Path.GetFileName(Path.TrimEndingDirectorySeparator(installationDirectory)), @"^\((\d+)\)");
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }

        public void SetTool(string toolId)
        {
            ToolId = toolId ?? CompatibilityToolId.Default;
            if (SettingsFile != null)
                Preferences.Set(AppConfigKey.GameCompatibilityTool, ToolId, SettingsFile);
        }

        public void SetPrefixMode(WinePrefixMode mode)
        {
            PrefixMode = mode;
            if (SettingsFile != null)
                Preferences.Set(AppConfigKey.GameWinePrefixMode, mode.ToString(), SettingsFile);
        }

        public CompatibilityTool ResolveTool() => CompatibilityManager.Resolve(ToolId);

        #region Game fixes
        public const string NoFixes = "none";
        /// <summary>"" = automatic (umu database), <see cref="NoFixes"/>, or an umu id chosen by the user.</summary>
        public string UmuIdSetting => Read(AppConfigKey.GameUmuId);
        /// <summary>Result of the last umu database lookup ("-" = unknown game, "" = not looked up yet).</summary>
        public string DetectedUmuId => Read(AppConfigKey.GameUmuIdDetected);

        /// <summary>GAMEID for umu-run / UMU_ID for Proton: selects the protonfixes applied to the game.</summary>
        public string EffectiveUmuId
        {
            get
            {
                string setting = UmuIdSetting;
                if (setting == NoFixes)
                    return UmuDatabase.DefaultGameId;
                if (UmuDatabase.IsValidId(setting))
                    return setting;
                return UmuDatabase.IsValidId(DetectedUmuId) ? DetectedUmuId : UmuDatabase.DefaultGameId;
            }
        }

        /// <summary>Winetricks verbs installed once into the prefix before the game starts (e.g. "vcrun2019 d3dx9").</summary>
        public string[] WinetricksVerbs => SplitVerbs(Read(AppConfigKey.GameWinetricks));
        /// <summary>Verbs already installed, per prefix ("prefix|verb").</summary>
        public string[] AppliedWinetricks => Read(AppConfigKey.GameWinetricksApplied).Split(';', StringSplitOptions.RemoveEmptyEntries);
        public string[] PendingWinetricks => WinetricksVerbs.Where(v => !AppliedWinetricks.Contains($"{PrefixPath}|{v}")).ToArray();

        public void SetUmuId(string value) => Write(AppConfigKey.GameUmuId, value?.Trim() ?? "");
        public void SetDetectedUmuId(string? id) => Write(AppConfigKey.GameUmuIdDetected, string.IsNullOrEmpty(id) ? "-" : id);
        public void SetWinetricksVerbs(string verbs) => Write(AppConfigKey.GameWinetricks, string.Join(' ', SplitVerbs(verbs)));
        public void MarkWinetricksApplied(IEnumerable<string> verbs) =>
            Write(AppConfigKey.GameWinetricksApplied, string.Join(';', AppliedWinetricks.Concat(verbs.Select(v => $"{PrefixPath}|{v}")).Distinct()));

        public static string[] SplitVerbs(string? verbs) =>
            (verbs ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(v => Regex.IsMatch(v, @"^[A-Za-z0-9_.=-]+$"))
                .Distinct().ToArray();

        private string Read(AppConfigKey key) => SettingsFile != null && File.Exists(SettingsFile) ? Preferences.Get(key, SettingsFile) : "";
        private void Write(AppConfigKey key, string value)
        {
            if (SettingsFile != null)
                Preferences.Set(key, value, SettingsFile);
        }
        #endregion

        public string PrefixPath => PrefixMode == WinePrefixMode.Game && GameId != null
            ? Path.Combine(CompatibilityManager.PrefixesDirectory, GameId.Value.ToString())
            : CompatibilitySettings.EffectiveWinePrefix;

        /// <summary>
        /// Windows user folder inside the prefix: Proton prefixes always use "steamuser", Wine the Linux user name.
        /// </summary>
        public string PrefixUserFolder
        {
            get
            {
                string steamUser = Path.Combine(PrefixPath, "drive_c", "users", "steamuser");
                return Directory.Exists(steamUser) ? steamUser : Path.Combine(PrefixPath, "drive_c", "users", Environment.UserName);
            }
        }
    }
}
