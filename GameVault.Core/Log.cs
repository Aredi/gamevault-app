using System.Runtime.CompilerServices;

namespace GameVault.Core
{
    /// <summary>
    /// Minimal file logger. Used mainly to record exceptions that the UI deliberately ignores,
    /// so that failures stop disappearing without a trace.
    /// </summary>
    public static class Log
    {
        private const long MaxFileSize = 2 * 1024 * 1024;
        private static readonly object fileLock = new object();
        private static string? logFile;

        public static string? LogFile => logFile;

        public static void Initialize(string directory)
        {
            Directory.CreateDirectory(directory);
            logFile = Path.Combine(directory, "gamevault.log");
        }

        public static void Info(string message) => Write("INFO", message);

        public static void Error(Exception ex, string? context = null)
        {
            Write("ERROR", context == null ? ex.ToString() : $"{context}: {ex}");
        }

        /// <summary>
        /// Records an exception that is intentionally not surfaced to the user.
        /// </summary>
        public static void Ignored(Exception ex, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            Write("DEBUG", $"{Path.GetFileName(file)}:{line} {member}: {ex.GetType().Name}: {ex.Message}");
        }

        private static void Write(string level, string message)
        {
            if (logFile == null)
                return;
            try
            {
                lock (fileLock)
                {
                    var info = new FileInfo(logFile);
                    if (info.Exists && info.Length > MaxFileSize)
                    {
                        File.Move(logFile, logFile + ".1", true);
                    }
                    File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never take the application down.
            }
        }
    }
}
