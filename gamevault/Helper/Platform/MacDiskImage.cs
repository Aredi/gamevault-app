using gamevault.Localization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper.Platform
{
    /// <summary>A Mac's disk image (.dmg): mounted with hdiutil, its app copied with ditto (which keeps its links and the signature of its frameworks).</summary>
    internal static class MacDiskImage
    {
        /// <summary>The app at the image's root, or in its one folder ("Installer/eden.app"), copied into <paramref name="folder"/>.</summary>
        public static async Task CopyAppAsync(string image, string app, string work, string folder)
        {
            string mount = Path.Combine(work, "image");
            Directory.CreateDirectory(mount);
            await RunAsync("hdiutil", "attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount, image);
            try
            {
                string source = Directory.EnumerateDirectories(mount).Prepend(mount).Select(d => Path.Combine(d, app)).FirstOrDefault(Directory.Exists)
                    ?? throw new IOException(Loc.F("{0} is not in {1}", app, Path.GetFileName(image)));
                Directory.CreateDirectory(folder);
                await RunAsync("ditto", source, Path.Combine(folder, app));
            }
            finally { await RunAsync("hdiutil", "detach", mount, "-force"); }
        }

        private static async Task RunAsync(string program, params string[] arguments)
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            using Process process = Process.Start(info) ?? throw new IOException(program + " could not be started");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await output;
            if (process.ExitCode != 0)
                throw new IOException($"{program}: {(await error).Trim()}");
        }
    }
}
