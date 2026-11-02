using GameVault.Core;
using GameVault.Core.Diagnostics;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>A line of a diagnostic, as the player and the administrators read it.</summary>
    public sealed record DiagnosticFact(string Key, string Value);

    /// <summary>What a problem report joins of this PC (see <see cref="GameDiagnostic"/>), and how it reads.</summary>
    internal static class GameDiagnostics
    {
        private static readonly Lazy<(string Processor, string Graphics)> Hardware = new(() => (Text(Processor), Text(Graphics)));

        private static string Text(Func<string?> read)
        {
            try { return read() ?? ""; }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return "";
            }
        }

        /// <summary>A retro game: its emulator, and how it failed when it just did.</summary>
        public static Task<GameDiagnostic> OfAsync(RetroGame game) => Task.Run(() =>
        {
            StandaloneEmulator? standalone = StandaloneEmulator.Of(game.Platform.System);
            string emulator = standalone != null
                ? $"{standalone.Name} {VersionIn(standalone.Layout.VersionFile, standalone.Layout.Version)}"
                : $"RetroArch {VersionIn(RetroArchManager.Layout.VersionFile, RetroArchLayout.Version)} · {game.Platform.System.Core}";
            string? log = standalone != null ? standalone.Layout.LogFile : RetroArchManager.Layout.LogFile;
            GameFailure? failure = game.Failure;
            if (failure is { ExitCode: null })
                log = Log.LogFile;
            // Older than the failed start: the emulator stopped before it wrote anything
            else if (failure != null && (log == null || File.GetLastWriteTimeUtc(log) < failure.StartedUtc))
                log = null;
            else if (failure == null && (log == null || !File.Exists(log)))
                log = Log.LogFile;
            return Collect(failure, emulator, log);
        });

        /// <summary>A game of the library: started by the system, nothing tells how it failed; the client's log says what it tried.</summary>
        public static Task<GameDiagnostic> OfAsync(Game game) => Task.Run(() => Collect(null, null, Log.LogFile));

        private static GameDiagnostic Collect(GameFailure? failure, string? emulator, string? logFile)
        {
            bool hasLog = logFile != null && File.Exists(logFile);
            return new GameDiagnostic
            {
                ExitCode = failure?.ExitCode,
                Seconds = failure?.Seconds,
                Failure = failure?.Message,
                Emulator = emulator,
                Client = $"SanctuaryVault {SettingsViewModel.Instance.Version}",
                System = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
                Processor = Hardware.Value.Processor,
                Graphics = Hardware.Value.Graphics,
                LogName = hasLog ? Path.GetFileName(logFile) : null,
                Log = hasLog ? GameDiagnostic.LogEnd(logFile!) : null,
            }.Cleaned(Environment.UserName, Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), Environment.MachineName);
        }

        private static string VersionIn(string file, string known) => File.Exists(file) ? File.ReadAllText(file).Trim() : known;

        public static IReadOnlyList<DiagnosticFact> Facts(GameDiagnostic diagnostic)
        {
            var facts = new List<DiagnosticFact>();
            void Add(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                    facts.Add(new DiagnosticFact(key, value));
            }
            Add(Loc.T("Failure"), diagnostic.CodeText is string code ? Loc.F("code {0} after {1} s", code, diagnostic.Seconds ?? 0) : diagnostic.Failure);
            Add(Loc.T("Emulator"), diagnostic.Emulator);
            Add(Loc.T("Client"), diagnostic.Client);
            Add(Loc.T("System"), diagnostic.System);
            Add(Loc.T("Processor"), diagnostic.Processor);
            Add(Loc.T("Graphics card"), diagnostic.Graphics);
            Add(Loc.T("Log"), diagnostic.LogName ?? Loc.T("No log available"));
            return facts;
        }

        /// <summary>All of it as text: what the player reads before sending it, what an administrator copies.</summary>
        public static string Text(GameDiagnostic diagnostic)
        {
            var text = new StringBuilder();
            foreach (DiagnosticFact fact in Facts(diagnostic))
                text.AppendLine($"{fact.Key} : {fact.Value}");
            if (diagnostic.Log != null)
                text.AppendLine().AppendLine($"--- {diagnostic.LogName} ---").AppendLine(diagnostic.Log);
            return text.ToString();
        }

        /// <summary>What its code tells, for the administrators; none without a failure.</summary>
        public static (string Title, string Detail)? Hint(GameDiagnostic diagnostic)
        {
            if (diagnostic.Failure == null)
                return null;
            string emulator = diagnostic.Emulator?.Split(' ')[0] ?? "";
            return diagnostic.Kind switch
            {
                EmulatorFailure.OlderProcessor => (Loc.F("This processor is too old for {0}", emulator), Loc.F("Code {0}: an instruction this processor does not have.", diagnostic.CodeText ?? "")),
                EmulatorFailure.MissingRuntime => (Loc.T("A system library is missing"), Loc.T("Install the Microsoft Visual C++ Redistributable (x64) on this PC.")),
                _ => (Loc.T("No known cause"), Loc.T("This code does not tell a known cause: the log is the best clue.")),
            };
        }

        private static string? Processor()
        {
            if (OperatingSystem.IsWindows())
                return Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString") as string;
            if (PlatformInfo.IsMac)
                return Output("sysctl", "-n", "machdep.cpu.brand_string").Trim();
            return File.Exists("/proc/cpuinfo") ? HardwareText.ProcessorOfCpuInfo(File.ReadAllText("/proc/cpuinfo")) : null;
        }

        private static string? Graphics()
        {
            IEnumerable<string> cards = OperatingSystem.IsWindows() ? WindowsGraphics()
                : PlatformInfo.IsMac ? HardwareText.GraphicsOfSystemProfiler(Output("system_profiler", "SPDisplaysDataType"))
                : LinuxGraphics();
            return string.Join(" · ", cards.Distinct());
        }

        /// <summary>The display adapters' class of devices, a key 0000, 0001... per adapter.</summary>
        [SupportedOSPlatform("windows")]
        private static IEnumerable<string> WindowsGraphics()
        {
            using RegistryKey? adapters = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (string name in adapters?.GetSubKeyNames().Where(n => n.All(char.IsDigit)) ?? Array.Empty<string>())
            {
                using RegistryKey? adapter = adapters!.OpenSubKey(name);
                if (adapter?.GetValue("DriverDesc") is string description)
                    yield return description;
            }
        }

        private static IEnumerable<string> LinuxGraphics()
        {
            if (PlatformInfo.FindInPath("lspci") is string lspci && HardwareText.GraphicsOfLspci(Output(lspci)).ToList() is { Count: > 0 } cards)
                return cards;
            return !Directory.Exists("/sys/class/drm") ? Array.Empty<string>() : Directory.EnumerateDirectories("/sys/class/drm", "card?")
                .Select(card => Path.Combine(card, "device", "vendor")).Where(File.Exists)
                .Select(vendor => HardwareText.VendorOfPciId(File.ReadAllText(vendor))).OfType<string>().ToList();
        }

        /// <summary>What a tool of the system prints, or nothing after a few seconds.</summary>
        private static string Output(string tool, params string[] arguments)
        {
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            if (process.WaitForExit(5000))
                return output.Result;
            process.Kill();
            return "";
        }
    }
}
