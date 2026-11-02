using GameVault.Core.Retro;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameVault.Core.Diagnostics
{
    /// <summary>How a game failed to start: what its page tells the player, what its diagnostic keeps of it.</summary>
    /// <param name="ExitCode">The emulator's, or none when it could not even be started.</param>
    public sealed record GameFailure(string Message, int? ExitCode, int Seconds, DateTime StartedUtc);

    /// <summary>
    /// What a player joins to a problem report, for the administrators: the failure, this PC and the end of the log.
    /// Sent as it is shown to the player: <see cref="Cleaned"/> has already taken out what must not leave the computer.
    /// </summary>
    public sealed record GameDiagnostic
    {
        public const int MaxLogLines = 200, MaxLogBytes = 64 * 1024;

        [JsonPropertyName("exitCode")] public int? ExitCode { get; init; }
        [JsonPropertyName("seconds")] public int? Seconds { get; init; }
        [JsonPropertyName("failure")] public string? Failure { get; init; }
        [JsonPropertyName("emulator")] public string? Emulator { get; init; }
        [JsonPropertyName("client")] public string Client { get; init; } = "";
        [JsonPropertyName("system")] public string System { get; init; } = "";
        [JsonPropertyName("processor")] public string Processor { get; init; } = "";
        [JsonPropertyName("graphics")] public string Graphics { get; init; } = "";
        /// <summary>The file it was read from ("eden_log.txt"), none when there was no log.</summary>
        [JsonPropertyName("logName")] public string? LogName { get; init; }
        [JsonPropertyName("log")] public string? Log { get; init; }

        [JsonIgnore] public string? CodeText => ExitCode is int code ? EmulatorExit.CodeText(code) : null;

        /// <summary>The kind of failure its code tells, none when it does not tell one.</summary>
        [JsonIgnore] public EmulatorFailure Kind => ExitCode is int code ? EmulatorExit.Of(code, TimeSpan.FromSeconds(Seconds ?? 0)) : EmulatorFailure.None;

        /// <summary>The last lines of a log, at most <see cref="MaxLogBytes"/>.</summary>
        public static string LogEnd(string file)
        {
            string[] lines = Redactor.Tail(file, MaxLogBytes).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            return string.Join('\n', lines.TakeLast(MaxLogLines));
        }

        /// <summary>Without passwords, tokens and the session's name, whatever the text.</summary>
        public GameDiagnostic Cleaned(params string?[] sessionNames)
        {
            string? Clean(string? text) => text == null ? null : Redactor.HideSession(Redactor.Redact(text), sessionNames);
            return this with
            {
                Failure = Clean(Failure), Emulator = Clean(Emulator), Client = Clean(Client)!, System = Clean(System)!,
                Processor = Clean(Processor)!, Graphics = Clean(Graphics)!, LogName = Clean(LogName), Log = Clean(Log),
            };
        }
    }

    /// <summary>What the systems' own tools say of a PC's processor and graphics card.</summary>
    public static class HardwareText
    {
        /// <summary>The "model name" of /proc/cpuinfo.</summary>
        public static string? ProcessorOfCpuInfo(string text) =>
            Regex.Match(text, @"^model name\s*:\s*(.+?)\s*$", RegexOptions.Multiline) is { Success: true } line ? line.Groups[1].Value : null;

        /// <summary>The display controllers of lspci, one per line.</summary>
        public static IEnumerable<string> GraphicsOfLspci(string text) => Regex.Matches(text, @"^\S+\s+(?:VGA compatible controller|3D controller|Display controller):\s*(.+?)\s*$", RegexOptions.Multiline)
            .Select(line => Regex.Replace(line.Groups[1].Value, @"\s*\(rev [0-9a-f]+\)$", ""));

        /// <summary>The "Chipset Model" of system_profiler SPDisplaysDataType.</summary>
        public static IEnumerable<string> GraphicsOfSystemProfiler(string text) =>
            Regex.Matches(text, @"^\s*Chipset Model:\s*(.+?)\s*$", RegexOptions.Multiline).Select(line => line.Groups[1].Value);

        /// <summary>A vendor from /sys/class/drm/*/device/vendor, when lspci is not there.</summary>
        public static string? VendorOfPciId(string id) => id.Trim().ToLowerInvariant() switch
        {
            "0x10de" => "NVIDIA",
            "0x1002" => "AMD",
            "0x8086" => "Intel",
            _ => null,
        };
    }
}
