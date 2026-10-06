using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace gamevault.Helper.Platform
{
    internal static class PlatformInfo
    {
        public static bool IsWindows => OperatingSystem.IsWindows();
        public static bool IsLinux => OperatingSystem.IsLinux();

        public static string AppDirectory => AppContext.BaseDirectory;

        /// <summary>
        /// The path other programs should start to reach this app. Inside an AppImage this is the
        /// AppImage file itself, not the binary in the temporary mount.
        /// </summary>
        public static string ExecutablePath
        {
            get
            {
                string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
                if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
                    return appImage;
                string appHost = Path.Combine(AppDirectory, IsWindows ? "gamevault.exe" : "gamevault");
                string? process = Environment.ProcessPath;
                // Started as "dotnet gamevault.dll": other programs must start the app host, not bare dotnet
                if (process == null || Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    return appHost;
                return process;
            }
        }

        /// <summary>
        /// ~/.local/share/GameVault on Linux, %LOCALAPPDATA%\GameVault on Windows.
        /// </summary>
        public static string LocalDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameVault");

        /// <summary>
        /// Finds an executable in PATH (and a few user directories that are often missing from PATH).
        /// </summary>
        public static string? FindInPath(params string[] names)
        {
            var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            directories.Add(Path.Combine(home, ".local", "bin"));
            directories.Add(Path.Combine(home, "bin"));

            foreach (string name in names)
            {
                foreach (string dir in directories)
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                    if (IsWindows && File.Exists(candidate + ".exe"))
                        return candidate + ".exe";
                }
            }
            return null;
        }

        /// <summary>
        /// The drive / mount point that contains <paramref name="path"/> (the most specific mount on Linux,
        /// where every path starts with "/").
        /// </summary>
        public static DriveInfo? GetDriveForPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string fullPath = Path.GetFullPath(path);
            var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return DriveInfo.GetDrives()
                .Where(d =>
                {
                    try
                    {
                        // "/mnt/pool" must not match "/mnt/pool2/games" ("/" and "C:\" keep their separator)
                        string root = d.RootDirectory.FullName;
                        if (!root.EndsWith(Path.DirectorySeparatorChar))
                            root += Path.DirectorySeparatorChar;
                        return d.IsReady && (fullPath + Path.DirectorySeparatorChar).StartsWith(root, comparison);
                    }
                    catch { return false; }
                })
                .OrderByDescending(d => d.RootDirectory.FullName.Length)
                .FirstOrDefault();
        }

        public static void OpenUrl(string url)
        {
            try
            {
                if (IsLinux)
                {
                    Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { GameVault.Core.Log.Error(ex, $"Could not open {url}"); }
        }

        /// <summary>
        /// Opens a folder in the file manager.
        /// </summary>
        public static void OpenFolder(string path) => OpenUrl(path);

        public static void MakeExecutable(string file)
        {
            if (IsWindows)
                return;
            try
            {
                var mode = File.GetUnixFileMode(file);
                File.SetUnixFileMode(file, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }
    }

    internal static class ToolLocator
    {
        /// <summary>
        /// 7-Zip: bundled 7z.exe on Windows, the system 7-Zip (7zz from the "7zip" package or 7z from p7zip) on Linux.
        /// </summary>
        public static string? SevenZip()
        {
            string bundled = Path.Combine(PlatformInfo.AppDirectory, "Lib", "7z", PlatformInfo.IsWindows ? "7z.exe" : "7zz");
            if (File.Exists(bundled))
            {
                PlatformInfo.MakeExecutable(bundled);
                return bundled;
            }
            return PlatformInfo.IsWindows ? null : PlatformInfo.FindInPath("7zz", "7z", "7za");
        }

        /// <summary>
        /// Ludusavi (cloud saves): bundled binary, then PATH, then the Flatpak.
        /// Returns the program and the arguments that must precede Ludusavi's own arguments.
        /// </summary>
        public static (string FileName, string[] PrefixArguments)? Ludusavi()
        {
            string bundled = Path.Combine(PlatformInfo.AppDirectory, "Lib", "savegame", PlatformInfo.IsWindows ? "ludusavi.exe" : "ludusavi");
            if (File.Exists(bundled))
            {
                PlatformInfo.MakeExecutable(bundled);
                return (bundled, Array.Empty<string>());
            }
            if (PlatformInfo.IsWindows)
                return null;

            string? inPath = PlatformInfo.FindInPath("ludusavi");
            if (inPath != null)
                return (inPath, Array.Empty<string>());

            string? flatpak = PlatformInfo.FindInPath("flatpak");
            if (flatpak != null && FlatpakInstalled(flatpak, "com.github.mtkennerly.ludusavi"))
                return (flatpak, new[] { "run", "com.github.mtkennerly.ludusavi" });
            return null;
        }

        private static bool FlatpakInstalled(string flatpak, string appId)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo(flatpak, $"info {appId}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                process!.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static string MissingToolMessage(string tool) => PlatformInfo.IsLinux
            ? tool switch
            {
                "7z" => "7-Zip was not found. Install it with your package manager (e.g. 'sudo apt install 7zip').",
                "ludusavi" => "Ludusavi was not found. Install it (e.g. 'flatpak install flathub com.github.mtkennerly.ludusavi') to use cloud saves.",
                _ => $"{tool} was not found.",
            }
            : $"{tool} is missing from the GameVault installation directory.";
    }
}
