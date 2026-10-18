namespace GameVault.Core.Storage
{
    /// <summary>Moves an installed game ("…/GameVault/Installations/(42)Title") to the Installations folder of another root directory.</summary>
    public static class InstallationMover
    {
        /// <summary>Folders being copied or deleted start with this: they are never taken for an installed game.</summary>
        public const string WorkPrefix = ".moving-";

        /// <returns>The new installation directory.</returns>
        /// <exception cref="IOException">The game is already in the target folder, or a file is in use (nothing was changed).</exception>
        public static async Task<string> MoveAsync(string installation, string targetRoot, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
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
            await CopyThenSwapAsync(installation, installations, name, progress, cancellationToken);
            return target;
        }

        /// <summary>
        /// Another disk: copied under a work name, then the old folder is renamed away (fails at once if a file is in use,
        /// and the copy is removed), the copy gets its real name and the old folder is deleted.
        /// </summary>
        internal static async Task CopyThenSwapAsync(string installation, string installations, string name, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            string copy = Path.Combine(installations, WorkPrefix + name);
            string old = Path.Combine(Path.GetDirectoryName(installation)!, WorkPrefix + name);
            if (Directory.Exists(copy))
                Directory.Delete(copy, true);
            try
            {
                string[] files = Directory.GetFiles(installation, "*", SearchOption.AllDirectories);
                long total = Math.Max(1, files.Sum(f => new FileInfo(f).Length));
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
            catch { }// the game is moved; a leftover work folder is never shown as a game
        }
    }
}
