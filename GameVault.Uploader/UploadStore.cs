using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace GameVault.Uploader
{
    /// <summary>
    /// Writes an upload as "&lt;name&gt;.partial" in the games folder, chunk after chunk, and renames it when it is
    /// complete: the GameVault server never indexes a half written archive, and an interrupted upload continues
    /// at the size of the partial file.
    /// </summary>
    public sealed class UploadStore(UploaderOptions options)
    {
        public const long MaxChunkSize = 64L * 1024 * 1024;
        private const string PartialSuffix = ".partial";

        private static readonly string[] Extensions =
        {
            ".zip", ".7z", ".rar", ".iso", ".tar", ".gz", ".tgz", ".xz", ".txz", ".bz2", ".tbz2", ".zst",
        };
        private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A plain archive file name: no folder, no hidden file, a known archive extension.</summary>
        public static bool IsValidName(string? name, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(name) || name.Length > 250)
                error = "The file name is empty or too long.";
            else if (name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\') || name.Contains(".."))
                error = "The file name must not contain a folder.";
            else if (name.StartsWith('.') || name.EndsWith(PartialSuffix, StringComparison.OrdinalIgnoreCase))
                error = "This file name is not allowed.";
            else if (Regex.IsMatch(name, @"[\x00-\x1f<>:""|?*%]"))
                error = "The file name contains characters that are not allowed.";
            else if (!Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                error = "Only game archives can be uploaded (.zip, .7z, .rar, .iso, .tar.gz, ...).";
            return error.Length == 0;
        }

        private string FinalPath(string name) => Path.Combine(options.FilesDirectory, name);
        private string PartialPath(string name) => FinalPath(name) + PartialSuffix;

        public sealed record State(long Received, bool Exists, long FreeSpace);

        public State GetState(string name)
        {
            var partial = new FileInfo(PartialPath(name));
            return new State(partial.Exists ? partial.Length : 0, File.Exists(FinalPath(name)), FreeSpace());
        }

        public long FreeSpace()
        {
            try { return new DriveInfo(Path.GetFullPath(options.FilesDirectory)).AvailableFreeSpace; }
            catch { return -1; }
        }

        public enum AppendResult { Appended, WrongOffset, TooBig, NoSpace }

        /// <summary>Appends <paramref name="body"/> at <paramref name="offset"/>, which must be the size received so far.</summary>
        public async Task<(AppendResult Result, long Received)> AppendAsync(string name, long offset, long? total, Stream body, CancellationToken cancellationToken)
        {
            SemaphoreSlim gate = locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                string partial = PartialPath(name);
                long received = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (offset != received)
                    return (AppendResult.WrongOffset, received);
                if (total is long size && FreeSpace() is long free and >= 0 && size - received > free)
                    return (AppendResult.NoSpace, received);

                await using var file = new FileStream(partial, FileMode.Append, FileAccess.Write, FileShare.None, 1 << 16, true);
                byte[] buffer = new byte[1 << 16];
                long written = 0;
                int read;
                try
                {
                    while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        if (written + read > MaxChunkSize)
                            return (AppendResult.TooBig, received + written);
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        written += read;
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or BadHttpRequestException)
                {
                    // The connection broke during the chunk: what arrived is kept, the client continues at the received size
                    await file.FlushAsync(CancellationToken.None);
                    return (AppendResult.WrongOffset, received + written);
                }
                return (AppendResult.Appended, received + written);
            }
            finally
            {
                gate.Release();
            }
        }

        public enum CompleteResult { Completed, WrongSize, Exists, Missing }

        /// <summary>Renames the partial file once it has the announced size: the server indexes it from then on.</summary>
        public CompleteResult Complete(string name, long size, bool overwrite)
        {
            string partial = PartialPath(name);
            if (!File.Exists(partial))
                return CompleteResult.Missing;
            if (new FileInfo(partial).Length != size)
                return CompleteResult.WrongSize;
            if (File.Exists(FinalPath(name)) && !overwrite)
                return CompleteResult.Exists;
            File.Move(partial, FinalPath(name), overwrite);
            return CompleteResult.Completed;
        }

        public void Cancel(string name)
        {
            if (File.Exists(PartialPath(name)))
                File.Delete(PartialPath(name));
        }
    }
}
