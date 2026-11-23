using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameVault.Core.Retro
{
    /// <summary>
    /// The updates of a game in a folder added to RomM after it was installed here (its "update" folder): the files
    /// RomM has that the game's folder on this PC does not.
    /// </summary>
    public static class RetroUpdates
    {
        /// <summary>A file of the game in RomM: its path in the game's folder ("update/X.nsp"), as RomM zips it.</summary>
        public sealed record RemoteFile(int Id, string Path, long Size, string? Category);

        /// <param name="installed">The paths of the files in the game's folder here, from it.</param>
        public static IReadOnlyList<RemoteFile> Missing(IEnumerable<RemoteFile> remote, IEnumerable<string> installed)
        {
            var here = new HashSet<string>(installed.Select(Normal), StringComparer.OrdinalIgnoreCase);
            return remote.Where(f => string.Equals(f.Category, "update", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(f.Path) && !IsUnsafe(f.Path) && !here.Contains(Normal(f.Path)) && !here.Contains(Normal(Unpacked(f.Path))))
                .ToList();
        }

        /// <summary>Where it goes here: an .nsz/.xcz is decompressed for Eden into an .nsp (as <see cref="SwitchCompressed.Decompress"/> names it), the others stay under their name.</summary>
        public static string Unpacked(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".nsz" or ".xcz"
            ? System.IO.Path.ChangeExtension(path, ".nsp") : path;

        /// <summary>A path that leaves the game's folder (or starts from a drive) is not written.</summary>
        public static bool IsUnsafe(string path) =>
            System.IO.Path.IsPathRooted(path) || path.Contains(':') || Normal(path).Split('/').Any(p => p == "..");

        private static string Normal(string path) => path.Replace('\\', '/').TrimStart('/');
    }
}
