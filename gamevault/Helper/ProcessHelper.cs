using gamevault.Localization;
using GameVault.Core;
using gamevault.Helper.Platform;
using GameVault.Core.Compatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// Global settings of the Linux compatibility layer. Filled from the user config by SettingsViewModel;
    /// each game can override the tool and the prefix (see <see cref="GameCompatibility"/>).
    /// </summary>
    internal static class CompatibilitySettings
    {
        /// <summary>Tool used by games that have no tool of their own, see <see cref="CompatibilityToolId"/>.</summary>
        public static string DefaultToolId { get; set; } = CompatibilityToolId.Auto;
        /// <summary>Shared WINEPREFIX; empty = ~/.local/share/GameVault/prefix.</summary>
        public static string WinePrefix { get; set; } = "";
        public static string CustomCommand { get; set; } = "";

        public static string EffectiveWinePrefix => string.IsNullOrWhiteSpace(WinePrefix)
            ? Path.Combine(PlatformInfo.LocalDataDirectory, "prefix")
            : WinePrefix;
    }

    internal class ProcessHelper
    {
        private static readonly string[] WindowsExtensions = { ".exe", ".bat", ".cmd", ".msi", ".com", ".lnk", ".url" };

        /// <summary>
        /// Starts a game, installer or uninstaller. On Linux, Windows programs are started through
        /// Proton or Wine, native ones directly. <paramref name="installationDirectory"/> selects the game's own
        /// compatibility tool and prefix; without it the global defaults are used.
        /// </summary>
        internal static Process StartApp(string fileName, string parameter = "", bool asAdmin = false, string? installationDirectory = null)
        {
            parameter ??= "";
            if (PlatformInfo.IsWindows)
            {
                ProcessStartInfo app = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = parameter,
                    WorkingDirectory = Path.GetDirectoryName(fileName),
                    UseShellExecute = true,
                };
                if (asAdmin)
                {
                    app.Verb = "runas";
                }
                return Process.Start(app)!;
            }

            ProcessStartInfo info = IsWindowsProgram(fileName)
                ? CreateCompatibilityStartInfo(fileName, parameter, GameCompatibility.ForInstallation(installationDirectory))
                : CreateNativeStartInfo(fileName, parameter);
            Log.Info($"Starting {info.FileName} {string.Join(' ', info.ArgumentList)}");
            return Process.Start(info) ?? throw new InvalidOperationException(Loc.F("Could not start {0}", fileName));
        }

        internal static bool IsWindowsProgram(string fileName)
        {
            return WindowsExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
        }

        private static ProcessStartInfo CreateNativeStartInfo(string fileName, string parameter)
        {
            PlatformInfo.MakeExecutable(fileName);
            var info = new ProcessStartInfo
            {
                WorkingDirectory = Path.GetDirectoryName(fileName),
                UseShellExecute = false,
            };
            if (Path.GetExtension(fileName).Equals(".sh", StringComparison.OrdinalIgnoreCase) && !HasShebang(fileName))
            {
                // Without "#!" the kernel can't start it; most launch scripts of games are bash scripts
                info.FileName = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
                info.ArgumentList.Add(fileName);
            }
            else
            {
                info.FileName = fileName;
            }
            foreach (string arg in SplitArguments(parameter))
                info.ArgumentList.Add(arg);
            return info;
        }

        /// <summary>The script names its interpreter ("#!/bin/bash"): started directly, it gets the right one.</summary>
        private static bool HasShebang(string file)
        {
            try
            {
                using var stream = File.OpenRead(file);
                return stream.ReadByte() == '#' && stream.ReadByte() == '!';
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return false;
            }
        }

        /// <summary>
        /// Starts a Wine builtin (winecfg, regedit, ...) with the game's tool and prefix.
        /// </summary>
        internal static Process StartWineTool(string tool, string? installationDirectory)
        {
            var compatibility = GameCompatibility.ForInstallation(installationDirectory);
            ProcessStartInfo info = CreateCompatibilityStartInfo(tool, "", compatibility, isBuiltin: true);
            info.WorkingDirectory = compatibility.PrefixPath;
            Log.Info($"Starting {info.FileName} {string.Join(' ', info.ArgumentList)}");
            return Process.Start(info) ?? throw new InvalidOperationException(Loc.F("Could not start {0}", tool));
        }

        private static ProcessStartInfo CreateCompatibilityStartInfo(string fileName, string parameter, GameCompatibility compatibility, bool isBuiltin = false)
        {
            string extension = isBuiltin ? "" : Path.GetExtension(fileName).ToLowerInvariant();
            var programArgs = new List<string>();
            // msiexec / cmd are Wine builtins, so installers and batch files work with every tool.
            if (extension == ".msi")
            {
                programArgs.AddRange(new[] { "msiexec", "/i", fileName });
            }
            else if (extension is ".bat" or ".cmd")
            {
                programArgs.AddRange(new[] { "cmd", "/c", fileName });
            }
            else if (extension is ".lnk" or ".url")
            {
                programArgs.AddRange(new[] { "start", "/unix", fileName });
            }
            else
            {
                programArgs.Add(fileName);
            }
            List<string> launchArgs = SplitArguments(parameter).ToList();

            string prefix = compatibility.PrefixPath;
            Directory.CreateDirectory(prefix);

            var info = new ProcessStartInfo
            {
                WorkingDirectory = isBuiltin ? prefix : Path.GetDirectoryName(fileName),
                UseShellExecute = false,
            };
            info.Environment["WINEPREFIX"] = prefix;

            CompatibilityTool tool = compatibility.ResolveTool();
            switch (tool.Kind)
            {
                case CompatibilityToolKind.Custom:
                    {
                        // {exe} is replaced by the program, {args} by the launch parameters.
                        var parts = SplitArguments(CompatibilitySettings.CustomCommand.Trim()).ToList();
                        if (parts.Count == 0)
                            throw new InvalidOperationException(Loc.T("The custom compatibility command is empty (Settings → Linux)."));
                        info.FileName = parts[0];
                        bool usedExe = false;
                        foreach (string part in parts.Skip(1))
                        {
                            if (part == "{exe}")
                            {
                                usedExe = true;
                                programArgs.ForEach(info.ArgumentList.Add);
                            }
                            else if (part == "{args}")
                            {
                                launchArgs.ForEach(info.ArgumentList.Add);
                            }
                            else
                            {
                                info.ArgumentList.Add(part);
                            }
                        }
                        if (!usedExe)
                        {
                            programArgs.ForEach(info.ArgumentList.Add);
                            launchArgs.ForEach(info.ArgumentList.Add);
                        }
                        return info;
                    }
                case CompatibilityToolKind.UmuLatest:
                case CompatibilityToolKind.Proton:
                    {
                        string? umu = PlatformInfo.FindInPath("umu-run");
                        if (umu != null)
                        {
                            // umu-run runs Proton inside the Steam Linux Runtime, like Steam does
                            info.FileName = umu;
                            // umu applies the protonfixes known for this id
                            info.Environment["GAMEID"] = compatibility.EffectiveUmuId;
                            if (tool.Kind == CompatibilityToolKind.Proton)
                                info.Environment["PROTONPATH"] = tool.Path!;
                        }
                        else if (tool.Kind == CompatibilityToolKind.Proton)
                        {
                            // Without umu, Proton is started directly. Its prefix is $STEAM_COMPAT_DATA_PATH/pfx;
                            // "pfx -> ." keeps the same layout as umu so both can use the prefix.
                            string pfx = Path.Combine(prefix, "pfx");
                            if (!Directory.Exists(pfx) && !File.Exists(pfx))
                                File.CreateSymbolicLink(pfx, ".");
                            info.FileName = Path.Combine(tool.Path!, "proton");
                            info.ArgumentList.Add("waitforexitandrun");
                            info.Environment["STEAM_COMPAT_DATA_PATH"] = prefix;
                            // GE-Proton runs the protonfixes of UMU_ID by itself
                            info.Environment["UMU_ID"] = compatibility.EffectiveUmuId;
                            string steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "root");
                            info.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = Directory.Exists(steam) ? steam : prefix;
                        }
                        else
                        {
                            throw new InvalidOperationException(Loc.T("umu-run was not found. Install umu-launcher or choose another compatibility tool."));
                        }
                        break;
                    }
                default:
                    {
                        // Wine build: <dir>/bin/wine, system Wine: the binary itself
                        info.FileName = tool.Id == CompatibilityToolId.SystemWine ? tool.Path! : Path.Combine(tool.Path!, "bin", "wine");
                        break;
                    }
            }
            programArgs.ForEach(info.ArgumentList.Add);
            launchArgs.ForEach(info.ArgumentList.Add);
            return info;
        }

        /// <summary>
        /// Splits a command line the way Windows programs expect: whitespace separated, double quotes group.
        /// </summary>
        internal static IEnumerable<string> SplitArguments(string? commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                yield break;

            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;
            foreach (char c in commandLine)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (hasToken)
                    {
                        yield return current.ToString();
                        current.Clear();
                        hasToken = false;
                    }
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }
            if (hasToken)
                yield return current.ToString();
        }
    }
}
