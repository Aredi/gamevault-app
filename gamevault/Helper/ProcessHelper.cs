using GameVault.Core;
using gamevault.Helper.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace gamevault.Helper
{
    public enum LinuxRunner
    {
        /// <summary>Proton through umu-run if installed, otherwise Wine.</summary>
        Auto,
        Proton,
        Wine,
        /// <summary>User supplied command line, e.g. "gamemoderun wine {exe} {args}".</summary>
        Custom,
    }

    /// <summary>
    /// Settings of the Linux compatibility layer. Filled from the user config by SettingsViewModel.
    /// </summary>
    internal static class CompatibilitySettings
    {
        public static LinuxRunner Runner { get; set; } = LinuxRunner.Auto;
        /// <summary>WINEPREFIX used for all games; empty = ~/.local/share/GameVault/prefix.</summary>
        public static string WinePrefix { get; set; } = "";
        /// <summary>PROTONPATH passed to umu-run; empty = let umu download GE-Proton.</summary>
        public static string ProtonPath { get; set; } = "";
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
        /// Proton (umu-run) or Wine, native ones directly.
        /// </summary>
        internal static Process StartApp(string fileName, string parameter = "", bool asAdmin = false)
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
                ? CreateCompatibilityStartInfo(fileName, parameter)
                : CreateNativeStartInfo(fileName, parameter);
            Log.Info($"Starting {info.FileName} {string.Join(' ', info.ArgumentList)}");
            return Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}");
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
            if (Path.GetExtension(fileName).Equals(".sh", StringComparison.OrdinalIgnoreCase))
            {
                info.FileName = "/bin/sh";
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

        private static ProcessStartInfo CreateCompatibilityStartInfo(string fileName, string parameter)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            var programArgs = new List<string>();
            // msiexec / cmd are Wine builtins, so installers and batch files work with both runners.
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
            programArgs.AddRange(SplitArguments(parameter));

            string prefix = CompatibilitySettings.EffectiveWinePrefix;
            Directory.CreateDirectory(prefix);

            var info = new ProcessStartInfo
            {
                WorkingDirectory = Path.GetDirectoryName(fileName),
                UseShellExecute = false,
            };
            info.Environment["WINEPREFIX"] = prefix;

            LinuxRunner runner = ResolveRunner();
            switch (runner)
            {
                case LinuxRunner.Custom:
                    {
                        // {exe} is replaced by the program (and its arguments), {args} by the launch parameters only.
                        string template = CompatibilitySettings.CustomCommand.Trim();
                        var parts = SplitArguments(template).ToList();
                        if (parts.Count == 0)
                            throw new InvalidOperationException("The custom compatibility command is empty (Settings → Linux).");
                        info.FileName = parts[0];
                        bool usedExe = false;
                        foreach (string part in parts.Skip(1))
                        {
                            if (part == "{exe}")
                            {
                                usedExe = true;
                                foreach (string a in programArgs.Take(programArgs.Count - SplitArguments(parameter).Count()))
                                    info.ArgumentList.Add(a);
                            }
                            else if (part == "{args}")
                            {
                                foreach (string a in SplitArguments(parameter))
                                    info.ArgumentList.Add(a);
                            }
                            else
                            {
                                info.ArgumentList.Add(part);
                            }
                        }
                        if (!usedExe)
                        {
                            foreach (string a in programArgs)
                                info.ArgumentList.Add(a);
                        }
                        break;
                    }
                case LinuxRunner.Proton:
                    {
                        info.FileName = PlatformInfo.FindInPath("umu-run") ?? throw new InvalidOperationException("umu-run was not found. Install umu-launcher or choose Wine in Settings → Linux.");
                        info.Environment["GAMEID"] = "umu-default";
                        if (!string.IsNullOrWhiteSpace(CompatibilitySettings.ProtonPath))
                            info.Environment["PROTONPATH"] = CompatibilitySettings.ProtonPath;
                        foreach (string a in programArgs)
                            info.ArgumentList.Add(a);
                        break;
                    }
                default:
                    {
                        info.FileName = PlatformInfo.FindInPath("wine") ?? throw new InvalidOperationException("Neither umu-run nor wine was found. Install Wine (e.g. 'sudo apt install wine') to run Windows games.");
                        foreach (string a in programArgs)
                            info.ArgumentList.Add(a);
                        break;
                    }
            }
            return info;
        }

        internal static LinuxRunner ResolveRunner()
        {
            if (CompatibilitySettings.Runner != LinuxRunner.Auto)
                return CompatibilitySettings.Runner;
            return PlatformInfo.FindInPath("umu-run") != null ? LinuxRunner.Proton : LinuxRunner.Wine;
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
