namespace GameVault.Core.Storage
{
    /// <summary>Moves an installed game ("…/GameVault/Installations/(42)Title") to the Installations folder of another root directory.</summary>
    public static class InstallationMover
    {
        /// <summary>Folders being copied or deleted start with this: they are never taken for an installed game.</summary>
        public const string WorkPrefix = ".moving-";

        /// <returns>The new installation directory.</returns>
        /// <exception cref="IOException">The game is already in the target folder, the other disk is too small, or a file is in use (nothing was changed).</exception>
        public static async Task<string> MoveAsync(string installation, string targetRoot, IProgress<double>? progress = null, CancellationToken cancellationToken = default, Func<string, long?>? freeSpace = null)
        {
            installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
            string name = Path.GetFileName(installation);
            string installations = Path.Combine(Path.GetFullPath(targetRoot), "GameVault", "Installations");
            string target = Path.Combine(installations, name);
            if (string.Equals(target, installation, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("The game is already in this folder");
            if (Directory.Exists(target))
            {
                if (Directory.EnumerateFileSystemEntries(target).Any())
                    throw new IOException($"'{target}' already exists");
                Directory.Delete(target);
            }
            Directory.CreateDirectory(installations);
            try
            {
                // Same disk: renamed at once
                Directory.Move(installation, target);
                progress?.Report(1);
                return target;
            }
            catch (IOException) when (Directory.Exists(installation) && !Directory.Exists(target)) { }
            await CopyThenSwapAsync(installation, installations, name, progress, cancellationToken, freeSpace);
            return target;
        }

        /// <summary>
        /// Another disk: copied under a work name, then the old folder is renamed away (fails at once if a file is in use,
        /// and the copy is removed), the copy gets its real name and the old folder is deleted.
        /// </summary>
        internal static async Task CopyThenSwapAsync(string installation, string installations, string name, IProgress<double>? progress, CancellationToken cancellationToken, Func<string, long?>? freeSpace = null)
        {
            string copy = Path.Combine(installations, WorkPrefix + name);
            string old = Path.Combine(Path.GetDirectoryName(installation)!, WorkPrefix + name);
            if (Directory.Exists(copy))
                Directory.Delete(copy, true);
            string[] files = Directory.GetFiles(installation, "*", SearchOption.AllDirectories);
            long size = files.Sum(f => new FileInfo(f).Length);
            // Checked before copying anything: a disk filled up halfway would only waste time
            long? free = (freeSpace ?? FreeSpace)(installations);
            if (free < size)
                throw new NotEnoughSpaceException(size, free.Value);
            try
            {
                long total = Math.Max(1, size);
                long done = 0;
                foreach (string dir in Directory.GetDirectories(installation, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(installation, dir)));
                Directory.CreateDirectory(copy);
                var buffer = new byte[1 << 20];
                foreach (string file in files)
                {
                    string destination = Path.Combine(copy, Path.GetRelativePath(installation, file));
                    await using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan | FileOptions.Asynchronous))
                    await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous))
                    {
                        int read;
                        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            done += read;
                            progress?.Report((double)done / total);
                        }
                    }
                    File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(destination, File.GetUnixFileMode(file));
                }
                Directory.Move(installation, old);
            }
            catch
            {
                try { Directory.Delete(copy, true); } catch { }
                throw;
            }
            Directory.Move(copy, Path.Combine(installations, name));
            progress?.Report(1);
            try { Directory.Delete(old, true); }
            catch { }// the game is moved; the folder left over is removed by RemoveLeftovers
        }

        private static long? FreeSpace(string directory)
        {
            try { return new DriveInfo(directory).AvailableFreeSpace; }
            catch { return null; }// unknown: the copy itself fails if the disk is full, and is removed
        }

        /// <summary>
        /// Deletes the work folders of moves that could not finish their cleanup (a file was in use, the app was closed).
        /// Not while a game is being moved: its copy is one of them.
        /// </summary>
        public static void RemoveLeftovers(IEnumerable<string> rootDirectories)
        {
            foreach (string root in rootDirectories)
            {
                string installations = Path.Combine(root, "GameVault", "Installations");
                try
                {
                    if (!Directory.Exists(installations))
                        continue;
                    foreach (string dir in Directory.GetDirectories(installations, WorkPrefix + "*"))
                    {
                        try { Directory.Delete(dir, true); }
                        catch { }// still in use: next time
                    }
                }
                catch { }// disk unplugged
            }
        }
    }

    public class NotEnoughSpaceException : IOException
    {
        public NotEnoughSpaceException(long needed, long free) : base($"{needed} bytes are needed, {free} are free")
        {
            Needed = needed;
            Free = free;
        }

        public long Needed { get; }
        public long Free { get; }
    }
}
