using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Captures;
using gamevault.Helper.Integrations;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// The screenshots and videos taken while a game runs, by whatever took them: the Game Bar (Win+Alt+PrtScn,
    /// Win+Alt+R), OBS, NVIDIA's, the system's tool, an emulator's key, or the client's own shortcut. A file that appears
    /// in one of their folders while a game runs is that game's; the files stay where they are.
    /// </summary>
    internal static class GameCaptures
    {
        /// <summary>A game got a capture ("pc:793"): on the UI thread.</summary>
        public static event Action<string>? Added;

        /// <summary>A capture that comes as the game closes (a video written at its end) is still that game's.</summary>
        private static readonly TimeSpan AfterTheGame = TimeSpan.FromMinutes(2);

        private static readonly object gate = new();
        private static readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
        private static (string Key, string Title)? retroPlaying;
        private static (string Key, string Title, DateTime Until)? lastPlayed;
        private static CaptureIndex? index;
        private static string? indexFile;

        public static string PcKey(int gameId) => $"pc:{gameId}";
        public static string RetroKey(RetroGame game) => "retro:" + RetroLibrary.Key(game);

        /// <summary>Where the client's own shortcut saves them.</summary>
        public static string OwnFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) is { Length: > 0 } pictures
            ? pictures : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SanctuaryVault");

        private static CaptureIndex Index
        {
            get
            {
                // One per profile: a sign-in with another account has its own
                string file = Path.Combine(Path.GetDirectoryName(LoginManager.Instance.GetUserProfile().CollectionsFile)!, "captures.json");
                lock (gate)
                {
                    if (index == null || indexFile != file)
                    {
                        index = new CaptureIndex(file);
                        indexFile = file;
                    }
                    return index;
                }
            }
        }

        public static IReadOnlyList<Capture> Of(string game)
        {
            try { return Index.Of(game); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return Array.Empty<Capture>();
            }
        }

        /// <summary>The retro game played is known from the first use, the folders are watched from <see cref="Start"/>.</summary>
        static GameCaptures()
        {
            RetroPlay.Started += game =>
            {
                retroPlaying = (RetroKey(game), game.Name);
                // An emulator installed since the start: its folder is watched from its first game (not in the tests, which never start)
                if (started)
                    Watch();
                InGameChanged?.Invoke();
            };
            RetroPlay.Closed += game =>
            {
                if (retroPlaying?.Key == RetroKey(game))
                    retroPlaying = null;
                lastPlayed = (RetroKey(game), game.Name, DateTime.UtcNow + AfterTheGame);
                InGameChanged?.Invoke();
            };
        }

        private static bool started;
        public static void Start()
        {
            if (started)
                return;
            started = true;
            Watch();
        }

        /// <summary>The tracker saw this PC game running (each minute).</summary>
        public static void PcSeen(int gameId) => lastPlayed = (PcKey(gameId), TitleOf(gameId), DateTime.UtcNow + AfterTheGame);

        private static bool pcRunning;
        /// <summary>Whether a game runs (a retro one, or a PC one the tracker saw at its last minute): Ctrl+PrtScn is taken then only.</summary>
        public static bool InGame => retroPlaying != null || pcRunning;
        /// <summary>A game started or ended (not on the UI thread for the PC games).</summary>
        public static event Action? InGameChanged;

        /// <summary>The tracker's minute: whether a PC game runs.</summary>
        public static void PcRunning(bool running)
        {
            if (running == pcRunning)
                return;
            pcRunning = running;
            InGameChanged?.Invoke();
        }

        private static IEnumerable<(string Folder, bool Deep)> Folders()
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (OperatingSystem.IsWindows())
            {
                // The Game Bar's Videos\Captures, NVIDIA's Videos\<game>, OBS's Videos; Win+PrtScn's Pictures\Screenshots
                yield return (videos, true);
                yield return (Path.Combine(pictures, "Screenshots"), false);
            }
            else
            {
                // GNOME's and KDE's screenshots and screen recordings, OBS's Videos
                yield return (pictures, false);
                yield return (Path.Combine(pictures, "Screenshots"), false);
                yield return (videos, false);
                yield return (Path.Combine(videos, "Screencasts"), false);
            }
            yield return (RetroArchManager.Layout.ScreenshotsDirectory, false);
            foreach (StandaloneEmulator emulator in StandaloneEmulator.All)
                if (emulator.Layout.ScreenshotsDirectory is string folder)
                    yield return (folder, true);
        }

        private static void Watch()
        {
            string? retroArch = RetroArchManager.Layout.ScreenshotsDirectory;
            foreach (var (folder, deep) in Folders())
            {
                if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
                    continue;
                lock (gate)
                {
                    if (watchers.ContainsKey(folder))
                        continue;
                    try
                    {
                        // An emulator's folder is made when it is there: it saves into it from its first screenshot
                        if (!Directory.Exists(folder) && Directory.Exists(Path.GetDirectoryName(folder)) && (folder == retroArch || StandaloneEmulator.All.Any(e => e.Layout.ScreenshotsDirectory == folder)))
                            Directory.CreateDirectory(folder);
                        if (!Directory.Exists(folder))
                            continue;
                        var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = deep, NotifyFilter = NotifyFilters.FileName };
                        watcher.Created += (_, e) => Seen(e.FullPath);
                        watcher.Renamed += (_, e) => Seen(e.FullPath);
                        watcher.EnableRaisingEvents = true;
                        watchers[folder] = watcher;
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
            }
        }

        /// <summary>The game played now, or the one that closed a moment ago; null when none.</summary>
        public static (string Key, string Title)? Playing()
        {
            if (retroPlaying is { } retro)
                return retro;
            try
            {
                if (GameTimeTracker.RunningGames().FirstOrDefault() is int id && id != 0)
                    return (PcKey(id), TitleOf(id));
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return lastPlayed is { } last && DateTime.UtcNow < last.Until ? (last.Key, last.Title) : null;
        }

        /// <summary>A file appeared in a watched folder.</summary>
        internal static void Seen(string path)
        {
            if (!CaptureFiles.IsCapture(path))
                return;
            try
            {
                if (Playing() is { } game)
                    Add(game.Key, path);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        public static void Add(string game, string path)
        {
            if (!Index.Add(new Capture(game, path, DateTime.UtcNow)))
                return;
            Log.Info($"Capture of {game}: {path}");
            Dispatcher.UIThread.Post(() => Added?.Invoke(game));
        }

        private static string TitleOf(int gameId)
        {
            try
            {
                Game? game = InstallViewModel.Instance.InstalledGames.ToList().FirstOrDefault(g => g.Key?.ID == gameId).Key;
                return game?.Metadata?.Title is { Length: > 0 } title ? title : game?.Title ?? gameId.ToString();
            }
            catch (InvalidOperationException) { return gameId.ToString(); }// the list changed on the UI thread meanwhile
        }
    }
}
