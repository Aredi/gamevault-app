using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// A retro game coming down, shown in the Downloads page beside the library's games and started by the same queue
    /// (simultaneous downloads, schedule). It lives here, not in the Retro page: changing page or platform keeps it.
    /// Once the file is complete it is made playable: a zip of several files unpacked, a CIA's main content taken,
    /// then RetroArch, the core and the BIOS fetched.
    /// </summary>
    internal sealed class RetroDownload : ViewModelBase, IQueuedDownload
    {
        /// <summary>Beside the game's file: what the game is, so the download is found again after a restart.</summary>
        public const string InfoFile = "retro-game.json";

        public RetroGame Game { get; }
        public string Folder { get; }
        /// <summary>"Install &amp; Play": started once ready.</summary>
        public bool PlayWhenDone { get; set; }

        private HttpClientDownloadWithProgress? client;
        private DownloadSpeedCalculator speed = new();
        private DateTime startTime;
        private bool paused, cancelled;
        private readonly List<RetroGame> watchers = new();

        public RetroDownload(RetroGame game, string folder)
        {
            Game = game;
            Folder = folder;
            watchers.Add(game);
        }

        private int progress;
        public int Progress { get => progress; private set { progress = value; OnPropertyChanged(); } }

        private string info = "";
        /// <summary>Speed, size and time left, as for the library's downloads.</summary>
        public string Info { get => info; private set { info = value; OnPropertyChanged(); } }

        private string state = "";
        public string State { get => state; private set { state = value; OnPropertyChanged(); Notify(); } }

        private bool isActive;
        /// <summary>The file is coming down.</summary>
        public bool IsActive { get => isActive; private set { isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanPause)); OnPropertyChanged(nameof(IsRunning)); } }

        private bool isPreparing;
        /// <summary>Downloaded, being made playable (unpacking, RetroArch...).</summary>
        public bool IsPreparing { get => isPreparing; private set { isPreparing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRunning)); } }

        private bool isPaused;
        public bool IsPaused { get => isPaused; private set { isPaused = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanPause)); } }

        private bool isQueued;
        public bool IsQueued { get => isQueued; private set { isQueued = value; OnPropertyChanged(); } }

        private bool isFailed;
        public bool IsFailed { get => isFailed; private set { isFailed = value; OnPropertyChanged(); } }

        private bool isDone;
        public bool IsDone { get => isDone; private set { isDone = value; OnPropertyChanged(); } }

        public bool CanPause => IsActive || IsPaused;
        /// <summary>Downloading or preparing: the Downloads page counts it as in progress.</summary>
        public bool IsRunning => IsActive || IsPreparing;

        /// <summary>The cards of the Retro page that show this game (the page makes new ones when it reloads).</summary>
        public void Watch(RetroGame game)
        {
            if (!watchers.Contains(game))
                watchers.Add(game);
            Notify();
        }

        /// <summary>Set when the game is there but its platform has no BIOS yet: the card keeps saying so.</summary>
        private string? notice;

        private void Notify()
        {
            bool busy = !IsDone && !IsFailed && !IsPaused;
            string shown = IsDone ? notice ?? "" : IsActive ? Loc.F("Downloading... {0:0}%", Progress) : State;
            foreach (RetroGame game in watchers)
            {
                game.Busy = busy;
                game.Status = shown;
                game.Refresh();
            }
        }

        /// <summary>A finished one leaves the list; the game stays installed.</summary>
        public void Dismiss() => DownloadsViewModel.Instance.RetroDownloads.Remove(this);

        #region Queue
        public bool BypassesQueue { get; private set; }
        public bool IsDownloading() => IsActive;

        public void SetQueuedState(string text)
        {
            IsQueued = true;
            State = text;
        }

        public void StartFromQueue()
        {
            IsQueued = false;
            // A download paused by the schedule goes on from where it was
            _ = RunAsync();
        }

        public void PauseForSchedule()
        {
            // Without advancing the queue: it is the one pausing (as the library's downloads)
            if (IsActive)
                Pause(advance: false);
        }
        #endregion

        public void Pause(bool advance = true)
        {
            if (client == null || !IsActive)
                return;
            paused = true;
            client.Pause();
            IsActive = false;
            IsPaused = true;
            State = Loc.T("Download Paused");
            if (advance)
                DownloadQueue.Advance();
        }

        /// <summary>Resumed by the player: runs now, even outside of the schedule.</summary>
        public void Resume()
        {
            DownloadQueue.Remove(this);
            IsQueued = false;
            BypassesQueue = true;
            _ = RunAsync();
        }

        public void StartNow()
        {
            DownloadQueue.Remove(this);
            BypassesQueue = true;
            StartFromQueue();
        }

        /// <summary>Found again at start, waiting for the player to resume it.</summary>
        public RetroDownload Paused()
        {
            IsPaused = true;
            State = Loc.T("Download Paused");
            return this;
        }

        public void Retry()
        {
            IsFailed = false;
            DownloadQueue.Enqueue(this, atFront: true);
        }

        /// <summary>Stops it and deletes what came down; the game is no longer in the list.</summary>
        public void Cancel()
        {
            DownloadQueue.Remove(this);
            cancelled = true;
            client?.Cancel();
            IsActive = false;
            DownloadQueue.Advance();
            DownloadsViewModel.Instance.RetroDownloads.Remove(this);
            _ = Task.Run(async () =>
            {
                // The download loop lets the file go first
                await Task.Delay(1500);
                try { Directory.Delete(Folder, true); }
                catch (Exception ex) { Log.Ignored(ex); }
            });
            State = "";
            IsPaused = false;
            Game.Status = "";
            foreach (RetroGame game in watchers)
            {
                game.Busy = false;
                game.Status = "";
                game.Refresh();
            }
        }

        private string Metadata => Path.Combine(Folder, RetroService.DownloadMetadata);

        private async Task RunAsync()
        {
            if (IsActive || IsDone)
                return;
            paused = cancelled = false;
            IsPaused = false;
            IsFailed = false;
            IsActive = true;
            State = Loc.T("Downloading...");
            try
            {
                Directory.CreateDirectory(Folder);
                // The download limit of the settings, applied by the SanctuaryVault service as the GameVault server does
                var headers = SettingsViewModel.Instance.DownloadLimit > 0
                    ? new Dictionary<string, string> { ["X-Download-Speed-Limit"] = SettingsViewModel.Instance.DownloadLimit.ToString() }
                    : null;
                client = new HttpClientDownloadWithProgress(RetroService.ContentUrl(Game), Folder, Game.FileName, headers)
                {
                    Connections = SettingsViewModel.Instance.DownloadConnections,
                };
                client.ProgressChanged += Client_ProgressChanged;
                speed = new DownloadSpeedCalculator();
                startTime = DateTime.Now;
                // A download stopped half way goes on from where it was
                await client.StartDownload(tryResume: File.Exists(Metadata));
                if (paused || cancelled)
                    return;
                IsActive = false;
                BypassesQueue = false;
                DownloadQueue.Advance();
                File.Delete(Metadata);
                IsPreparing = true;
                await PrepareAsync();
                IsPreparing = false;
                IsDone = true;
                State = Loc.T("Ready to play");
                Toast(Loc.T("Download Complete"));
                if (PlayWhenDone)
                    await RetroPlay.PlayAsync(Game);
            }
            catch (RetroArchManager.MissingBiosException ex)
            {
                // The game is there: the BIOS comes when an administrator adds it, and Play fetches it
                IsPreparing = false;
                IsDone = true;
                notice = ex.Message;
                State = ex.Message;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                IsActive = IsPreparing = false;
                if (paused || cancelled)
                    return;
                IsFailed = true;
                State = Loc.F("Failed: {0}", WebExceptionHelper.TryGetServerMessage(ex));
                Toast(Loc.T("Download Failed"));
                DownloadQueue.Advance();
            }
        }

        private void Client_ProgressChanged(long totalFileSize, long currentBytesDownloaded, long totalBytesDownloaded, double? progressPercentage, long resumePosition)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsActive)
                    return;
                if (currentBytesDownloaded > 0)
                    speed.UpdateSpeed(currentBytesDownloaded);
                double perSecond = speed.GetCurrentSpeed();
                string left = perSecond > 0 ? TimeSpan.FromSeconds(Math.Max(0, (totalFileSize - totalBytesDownloaded) / perSecond)).ToString(@"hh\:mm\:ss") : "--:--:--";
                Info = currentBytesDownloaded > 0
                    ? Loc.F("{0} - {1} of {2} | Time left: {3}", $"{Size(perSecond, 1000)}/s", Size(totalBytesDownloaded), Size(totalFileSize), left)
                    : $"{Size(totalBytesDownloaded)} / {Size(totalFileSize)}";
                Progress = (int)(progressPercentage ?? 0);
                Notify();
            });
        }

        /// <summary>A whole number of bytes: the converter reads its value back with the current culture ("8388608.5" is no number in French).</summary>
        internal static string Size(double bytes, int unit = 1024) =>
            new Converter.GameSizeConverter().Convert((long)Math.Round(bytes), typeof(string), unit, System.Globalization.CultureInfo.CurrentCulture) as string ?? "";

        /// <summary>The downloaded file made playable, then what the platform needs.</summary>
        private async Task PrepareAsync()
        {
            string downloaded = Directory.EnumerateFiles(Folder).Where(f => Path.GetFileName(f) != InfoFile).OrderByDescending(File.GetLastWriteTimeUtc).First();
            if (Game.Multi && string.Equals(Path.GetExtension(downloaded), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                // A game in several files (a CD and its tracks): RomM sends them zipped
                var sevenZip = new SevenZipHelper();
                sevenZip.Process += (_, e) => Dispatcher.UIThread.Post(() => State = Loc.F("Unpacking... {0}%", e.PercentageDone));
                State = Loc.F("Unpacking... {0}%", 0);
                if (await sevenZip.ExtractArchive(downloaded, Folder) != 0)
                    throw new IOException(sevenZip.LastError);
                File.Delete(downloaded);
            }
            else if (string.Equals(Path.GetExtension(downloaded), ".cia", StringComparison.OrdinalIgnoreCase))
            {
                State = Loc.T("Preparing the 3DS game...");
                byte[] start = new byte[CiaReader.HeaderBytes];
                int read;
                await using (FileStream file = File.OpenRead(downloaded))
                    read = await file.ReadAsync(start);
                await CiaReader.ExtractAsync(downloaded, CiaReader.Inspect(start.AsSpan(0, read), new FileInfo(downloaded).Length));
            }
            await RetroArchManager.EnsureReadyAsync(Game.Platform, text => Dispatcher.UIThread.Post(() => State = text));
            if (Game.RomFile == null)
                throw new IOException(Loc.T("None of this game's files can be started by the emulator."));
        }

        private void Toast(string title)
        {
            if (!App.Instance.IsWindowActiveAndControlInFocus(MainControl.Downloads))
                ToastMessageHelper.CreateToastMessage(title, Game.Name);
        }

        /// <summary>What the folder holds, written when the download starts.</summary>
        public void WriteInfo()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, InfoFile), JsonSerializer.Serialize(new SavedGame(Game, Game.Platform)));
        }

        internal sealed record SavedGame(RetroGame Game, RetroPlatform Platform);
    }

    /// <summary>The retro downloads of this PC: started from the Retro page, listed in the Downloads page.</summary>
    internal static class RetroDownloads
    {
        private static IEnumerable<RetroDownload> All => DownloadsViewModel.Instance.RetroDownloads;

        public static RetroDownload? Find(RetroGame game) => All.FirstOrDefault(d => d.Game.Id == game.Id && d.Game.Platform.Slug == game.Platform.Slug);

        /// <summary>The cards of a freshly loaded list show the downloads already going on.</summary>
        public static void Attach(IEnumerable<RetroGame> games)
        {
            foreach (RetroGame game in games)
                Find(game)?.Watch(game);
        }

        /// <returns>Null when it is in the Downloads page (added, or already there), else why not.</returns>
        public static async Task<string?> StartAsync(RetroGame game, bool playWhenDone = false)
        {
            if (Find(game) is RetroDownload existing)
            {
                existing.PlayWhenDone |= playWhenDone;
                if (existing.IsFailed)
                    existing.Retry();
                else if (existing.IsPaused)
                    existing.Resume();
                existing.Watch(game);
                return null;
            }
            if ((SettingsViewModel.Instance.RootDirectories?.Count ?? 0) == 0)
                return Loc.T("No Root Directory configured! Go to ⚙️Settings->Data");
            // Most 3DS packages are encrypted, which Azahar can not play: told before gigabytes come down
            if (string.Equals(Path.GetExtension(game.FileName), ".cia", StringComparison.OrdinalIgnoreCase)
                && CiaReader.Inspect(await RetroService.ReadStartAsync(game, CiaReader.HeaderBytes), game.Size).Verdict != CiaReader.Verdict.Playable)
                return Loc.T("This 3DS game is encrypted: Azahar can not play it. A decrypted copy is needed in RomM.");
            // As the library's games: asked where when the setting says so (or no folder is set yet)
            string root;
            if (SettingsViewModel.Instance.AskInstallLocation)
            {
                var picker = new UserControls.InstallLocationUserControl(game.Size);
                MainWindowViewModel.Instance.OpenPopup(picker);
                root = await picker.SelectInstallLocation();
                if (string.IsNullOrEmpty(root))
                    return Loc.T("No install location was chosen");
                if (!Directory.Exists(root))
                    return Loc.T("Selected directory does not exist");
            }
            else
                root = UserControls.InstallLocationUserControl.DefaultLocation();
            var download = new RetroDownload(game, RetroService.Folder(game, root)) { PlayWhenDone = playWhenDone };
            download.WriteInfo();
            DownloadsViewModel.Instance.RetroDownloads.Add(download);
            DownloadQueue.Enqueue(download);
            return null;
        }

        /// <summary>At start: the downloads stopped half way (the app closed), shown paused as the library's.</summary>
        public static void Restore()
        {
            foreach (var root in SettingsViewModel.Instance.RootDirectories ?? new())
            {
                string retro = Path.Combine(root.Uri, "GameVault", "Retro");
                if (!Directory.Exists(retro))
                    continue;
                foreach (string folder in Directory.EnumerateDirectories(retro).SelectMany(Directory.EnumerateDirectories))
                {
                    try
                    {
                        string info = Path.Combine(folder, RetroDownload.InfoFile);
                        if (!File.Exists(info) || !File.Exists(Path.Combine(folder, RetroService.DownloadMetadata)))
                            continue;
                        var saved = JsonSerializer.Deserialize<RetroDownload.SavedGame>(File.ReadAllText(info));
                        if (saved == null || !RetroSystems.All.ContainsKey(saved.Platform.Slug))
                            continue;
                        saved.Game.Platform = saved.Platform;
                        if (Find(saved.Game) == null)
                            DownloadsViewModel.Instance.RetroDownloads.Add(new RetroDownload(saved.Game, folder).Paused());
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
            }
        }
    }
}
