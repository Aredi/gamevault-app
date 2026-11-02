using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The problems the players report on a game (it does not start, wrong cover...), for the administrators' console.
    /// One JSON file for all, written whole; the resolved ones are forgotten after a while. A report's diagnostic (a log can
    /// not go in that file) is a file of its own, read by the administrators and deleted when the report is resolved.
    /// </summary>
    public sealed class ReportStore
    {
        public const int MaxOpenPerUser = 20;
        /// <summary>64 KB of log and the facts, all of its characters escaped at worst.</summary>
        public const int MaxDiagnosticLength = 512 * 1024;
        public static readonly string[] Categories = { "launch", "match", "file", "media", "other" };
        private static readonly TimeSpan KeepResolvedFor = TimeSpan.FromDays(30);

        private readonly string file, diagnostics;
        private readonly object writing = new();

        public ReportStore(UploaderOptions options, ILogger<ReportStore> logger)
        {
            file = Path.Combine(options.ReportsDirectory, "reports.json");
            diagnostics = Path.Combine(options.ReportsDirectory, "diagnostics");
            Enabled = ProfileStore.IsWritable(options.ReportsDirectory, out string error);
            if (!Enabled)
                logger.LogWarning("Problem reports are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", options.ReportsDirectory, error);
        }

        public bool Enabled { get; }

        /// <summary>Kind is "pc" (a GameVault game) or "retro" (a RomM game, of Platform).</summary>
        public sealed record Report(string Id, string Kind, int GameId, string? Platform, string Title, string Category, string? Details, string? Context,
            int UserId, string Username, DateTime At, DateTime? ResolvedAt, string? ResolvedBy, bool HasDiagnostic = false);

        private string DiagnosticFile(string id) => Path.Combine(diagnostics, id + ".json");

        private List<Report> Read()
        {
            if (!Enabled || !File.Exists(file))
                return new();
            List<Report> reports = JsonSerializer.Deserialize<List<Report>>(File.ReadAllText(file)) ?? new();
            reports.RemoveAll(r => DateTime.UtcNow - r.ResolvedAt > KeepResolvedFor);
            return reports;
        }

        private void Write(List<Report> reports)
        {
            // Written aside then moved: never half written
            string part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(reports));
            File.Move(part, file, overwrite: true);
        }

        /// <summary>Newest first; a player sees only theirs.</summary>
        public List<Report> List(int? userId)
        {
            lock (writing)
                return Read().Where(r => userId == null || r.UserId == userId).OrderByDescending(r => r.At).ToList();
        }

        /// <returns>The new report, or null when the player already has too many open ones.</returns>
        public Report? Add(Report report, string? diagnostic = null)
        {
            lock (writing)
            {
                List<Report> reports = Read();
                if (reports.Count(r => r.UserId == report.UserId && r.ResolvedAt == null) >= MaxOpenPerUser)
                    return null;
                report = report with { Id = Guid.NewGuid().ToString("N"), At = DateTime.UtcNow, ResolvedAt = null, ResolvedBy = null, HasDiagnostic = diagnostic != null };
                if (diagnostic != null)
                {
                    Directory.CreateDirectory(diagnostics);
                    File.WriteAllText(DiagnosticFile(report.Id), diagnostic);
                }
                reports.Add(report);
                Write(reports);
                return report;
            }
        }

        /// <summary>The diagnostic of a report still open, as the client sent it; the id is looked up, never used as a path.</summary>
        public string? Diagnostic(string id)
        {
            lock (writing)
                return Read().FirstOrDefault(r => r.Id == id && r.HasDiagnostic) is Report report ? File.ReadAllText(DiagnosticFile(report.Id)) : null;
        }

        /// <summary>An administrator says it is fixed, and its diagnostic is no longer needed; false when there is no such report.</summary>
        public bool Resolve(string id, string admin)
        {
            lock (writing)
            {
                List<Report> reports = Read();
                int index = reports.FindIndex(r => r.Id == id);
                if (index < 0)
                    return false;
                bool hadDiagnostic = reports[index].HasDiagnostic;
                reports[index] = reports[index] with { ResolvedAt = DateTime.UtcNow, ResolvedBy = admin, HasDiagnostic = false };
                Write(reports);
                if (hadDiagnostic)
                    File.Delete(DiagnosticFile(id));
                return true;
            }
        }
    }
}
