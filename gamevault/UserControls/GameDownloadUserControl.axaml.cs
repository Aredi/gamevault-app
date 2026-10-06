using gamevault.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Helper.Platform;
using GameVault.Core;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class GameDownloadUserControl : UserControl
    {
        private DownloadSpeedCalculator downloadSpeedCalc { get; set; }
        private GameDownloadViewModel ViewModel { get; set; }
        private bool IsDownloadActive = false;

        private string m_DownloadPath { get; set; }
        private bool extractionCancelled = false;
        private HttpClientDownloadWithProgress client { get; set; }
        private DateTime startTime;

        private SevenZipHelper sevenZipHelper { get; set; }

        private GameSizeConverter gameSizeConverter { get; set; }
        private InputTimer downloadRetryTimer { get; set; }
        private bool isGameTypeForced = false;
        private bool downloadedAgainAfterDamage;
        private double downloadRetryTimerTickValue = 10;
        private string mountedDrive = "";

        /// <param name="installPath">The existing installation an update goes to (its folder keeps its name if the title changed).</param>
        public GameDownloadUserControl(Game game, string rootDirectory, bool download, string? installPath = null)
        {
            InitializeComponent();
            ViewModel = new GameDownloadViewModel();
            this.DataContext = ViewModel;
            ViewModel.Game = game;
            ViewModel.DownloadUIVisibility = false;
            ViewModel.ExtractionUIVisibility = false;
            ViewModel.DownloadFailedVisibility = false;

            string folderName = GameFolderName(ViewModel.Game);
            m_DownloadPath = Path.Combine(rootDirectory, "GameVault", "Downloads", folderName);
            ViewModel.InstallPath = installPath ?? Path.Combine(rootDirectory, "GameVault", "Installations", folderName);
            sevenZipHelper = new SevenZipHelper();
            gameSizeConverter = new GameSizeConverter();
            InitRetryTimer();
            if (download)
            {
                DownloadQueue.Enqueue(this);
            }
            else
            {
                UpdateDataSizeUI();
                if (File.Exists(Path.Combine(m_DownloadPath, "Extract", "gamevault-metadata")) && Preferences.Get(AppConfigKey.ExtractionFinished, Path.Combine(m_DownloadPath, "Extract", "gamevault-metadata")) == "1")
                {
                    ViewModel.State = "Extracted";
                    uiBtnExtract.IsEnabled = true;
                    uiBtnInstall.IsEnabled = true;
                    uiBtnExtract.Text = "Re-Extract";
                    ViewModel.InstallationStepperProgress = 1;
                }
                else
                {
                    //Try resume paused UI
                    if (TryRecreatePausedUI())
                        return;
                    //If no valid pause data, its downloaded
                    ViewModel.State = "Downloaded";
                    uiBtnExtract.IsEnabled = true;
                    ViewModel.InstallationStepperProgress = 0;
                }
            }
        }
        /// <summary>
        /// "(42)Title" with the characters the file system refuses replaced: only "/" on Linux, also ":*?" etc. on
        /// Windows (server titles come from file names on a Linux server, which may contain them).
        /// </summary>
        internal static string GameFolderName(Game game)
        {
            var name = new System.Text.StringBuilder($"({game.ID}){game.Title}");
            foreach (char c in Path.GetInvalidFileNameChars().Append('/').Append('\\'))
                name.Replace(c, '_');
            // Windows drops trailing dots and spaces, the folder would not be found again
            return OperatingSystem.IsWindows() ? name.ToString().TrimEnd('.', ' ') : name.ToString();
        }
        public void Refresh(Game game)
        {
            ViewModel.Game = game;
        }
        private void UpdateDataSizeUI()
        {
            Task.Run(() =>
            {
                try
                {
                    double size = 0;
                    foreach (FileInfo file in new DirectoryInfo(m_DownloadPath).GetFiles("*", SearchOption.AllDirectories))
                    {
                        file.Refresh();
                        size += file.Length;
                    }
                    ViewModel.TotalDataSize = size;
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            });

        }
        private bool TryRecreatePausedUI()
        {
            try
            {
                string resumeData = Preferences.Get(AppConfigKey.DownloadProgress, Path.Combine(m_DownloadPath, "gamevault-metadata"));
                if (!string.IsNullOrEmpty(resumeData))
                {
                    string[] resumeDataToProcess = resumeData.Split(";");
                    var resumePos = long.Parse(resumeDataToProcess[0]);
                    var preResumeSize = long.Parse(resumeDataToProcess[1]);
                    double progressPercentage = Math.Round((double)resumePos / preResumeSize * 100, 0);
                    DownloadProgress(preResumeSize, 0, resumePos, progressPercentage, resumePos);
                    ViewModel.IsDownloadPaused = true;
                    ViewModel.DownloadUIVisibility = true;
                    ViewModel.State = "Download Paused";
                    return true;
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }

            return false;
        }
        public bool IsPaused() => ViewModel.IsDownloadPaused;
        /// <summary>Downloading, extracting or installing: its files must not be touched.</summary>
        public bool IsBusy() => IsDownloadActive || ViewModel.ExtractionUIVisibility || uiProgressRingInstall.IsActive;

        #region Install and play
        /// <summary>"Install &amp; Play": extraction, installation and the first start follow the download by themselves.</summary>
        public bool PlayWhenInstalled { get; set; }

        /// <summary>The archive is downloaded completely, or already extracted.</summary>
        public bool HasDownloadedFiles()
        {
            if (Directory.Exists(Path.Combine(m_DownloadPath, "Extract")))
                return true;
            string archive = Path.Combine(m_DownloadPath, Path.GetFileName(ViewModel.Game?.Path ?? ""));
            // An unfinished download still has its resume data
            return File.Exists(archive) && !File.Exists(Path.Combine(m_DownloadPath, "gamevault-metadata"));
        }

        /// <summary>Continues a finished download (or extraction) up to the start of the game.</summary>
        public async Task ContinueToPlay()
        {
            PlayWhenInstalled = true;
            bool extracted = File.Exists(Path.Combine(m_DownloadPath, "Extract", "gamevault-metadata"))
                && Preferences.Get(AppConfigKey.ExtractionFinished, Path.Combine(m_DownloadPath, "Extract", "gamevault-metadata")) == "1";
            if (!extracted)
            {
                await Extract();// continues with the installation when it is done
                return;
            }
            if (ViewModel.Game?.Type == GameType.WINDOWS_SETUP)
                await InstallSetupForPlay();
            else
                await Install();
        }

        /// <summary>
        /// Setups install silently when the game has installer parameters (Custom Metadata); otherwise the
        /// installation options open so the user picks the installer, and the game starts afterwards.
        /// </summary>
        private async Task InstallSetupForPlay()
        {
            LoadSetupExecutables();
            if (!string.IsNullOrWhiteSpace(ViewModel.Game?.Metadata?.InstallerParameters) && uiCbSetupExecutable.SelectedItem != null)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.F("Installing {0}...", ViewModel.Game?.Title);
                await Install();
                return;
            }
            uiInstallOptions.IsVisible = true;
            MainWindowViewModel.Instance.AppBarText = Loc.F("Choose the installer of {0}: the game starts after the installation.", ViewModel.Game?.Title);
        }

        private async Task StartGameIfRequested()
        {
            if (!PlayWhenInstalled || ViewModel.State != "Installed")
                return;
            PlayWhenInstalled = false;
            // The installation is registered by a file watcher
            for (int i = 0; i < 30 && !InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == ViewModel.Game.ID); i++)
                await Task.Delay(500);
            if (InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == ViewModel.Game.ID))
                await InstallUserControl.PlayGame(ViewModel.Game.ID);
        }
        #endregion
        public bool IsDownloading()
        {
            return IsDownloadActive;
        }
        public bool IsGameIdDownloading(int id)
        {
            if (IsDownloadActive == true && ViewModel.Game.ID == id)
            {
                return true;
            }
            return false;
        }
        public int GetGameId()
        {
            return ViewModel.Game.ID;
        }
        public int GetCoverID()
        {
            return ViewModel.Game.Metadata.Cover.ID;
        }
        public int GetDownloadProgress()
        {
            return ViewModel.GameDownloadProgress;
        }
        #region Download queue
        /// <summary>Started with "Start now" or resumed by the user: not limited by the queue and the schedule.</summary>
        public bool BypassesQueue { get; private set; }
        private bool pausedBySchedule;

        public void SetQueuedState(string state)
        {
            ViewModel.IsQueued = true;
            ViewModel.State = state;
        }

        public void StartFromQueue()
        {
            ViewModel.IsQueued = false;
            if (ViewModel.IsDownloadPaused || pausedBySchedule)
            {
                pausedBySchedule = false;
                ViewModel.IsDownloadResumed = false;
                ViewModel.IsDownloadPaused = false;
                _ = DownloadGame(true);
            }
            else
            {
                // A partial download (e.g. after a failed attempt) is continued
                _ = DownloadGame(File.Exists(Path.Combine(m_DownloadPath, "gamevault-metadata")));
            }
        }

        public void PauseForSchedule()
        {
            if (!IsDownloadActive)
                return;
            PauseDownload();
            pausedBySchedule = true;
        }

        private void StartQueuedNow_Click(object sender, RoutedEventArgs e)
        {
            DownloadQueue.Remove(this);
            BypassesQueue = true;
            StartFromQueue();
        }

        private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
        {
            CancelDownload();
        }
        #endregion

        public void CancelDownload()
        {
            if (DownloadQueue.IsWaiting(this))
            {
                DownloadQueue.Remove(this);
                ViewModel.IsQueued = false;
                pausedBySchedule = false;
            }
            if (client == null)
            {
                try
                {
                    File.Delete(Path.Combine(m_DownloadPath, "gamevault-metadata"));
                    File.Delete(Path.Combine(m_DownloadPath, Path.GetFileName(ViewModel.Game.Path)));
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
            else
            {
                client.Cancel();
            }
            //client.Dispose();
            IsDownloadActive = false;
            ViewModel.IsDownloadPaused = false;
            ViewModel.State = "Download Cancelled";
            ViewModel.DownloadUIVisibility = false;
            ViewModel.DownloadFailedVisibility = true;
            DownloadQueue.Advance();
        }
        private async Task DownloadGame(bool tryResume = false)
        {
            IsDownloadActive = true;
            // The completion is detected when the progress reaches 100
            if (!tryResume)
                ViewModel.GameDownloadProgress = 0;
            ViewModel.State = "Downloading...";
            ViewModel.DownloadUIVisibility = true;
            ViewModel.DownloadFailedVisibility = false;

            if (!Directory.Exists(m_DownloadPath)) { Directory.CreateDirectory(m_DownloadPath); }
            Dictionary<string,string> additionalRequestHeaders = new Dictionary<string,string>();
            if (SettingsViewModel.Instance.DownloadLimit > 0)
            {
                additionalRequestHeaders.Add("X-Download-Speed-Limit", SettingsViewModel.Instance.DownloadLimit.ToString());
            }
            client = new HttpClientDownloadWithProgress($"{SettingsViewModel.Instance.ServerUrl}/api/games/{ViewModel.Game.ID}/download", m_DownloadPath, Path.GetFileName(ViewModel.Game.Path), additionalRequestHeaders);
            client.Connections = SettingsViewModel.Instance.DownloadConnections;
            client.ProgressChanged += DownloadProgress;
            startTime = DateTime.Now;
            downloadSpeedCalc = new DownloadSpeedCalculator();
            try
            {
                await client.StartDownload(tryResume);
                await CacheHelper.CreateOfflineCacheAsync(ViewModel.Game);
            }
            catch (Exception ex)
            {
                IsDownloadActive = false;
                ViewModel.State = $"Error: '{ex.Message}'";
                ViewModel.DownloadUIVisibility = false;
                ViewModel.DownloadFailedVisibility = true;

                if (downloadRetryTimer.Data != "error")
                {
                    if (!App.Instance.IsWindowActiveAndControlInFocus(MainControl.Downloads))
                        ToastMessageHelper.CreateToastMessage(Loc.T("Download Failed"), ViewModel.Game.Title, Path.Combine(LoginManager.Instance.GetUserProfile().ImageCacheDir, "gbox", $"{ViewModel.Game.ID}.{ViewModel.Game.Metadata.Cover?.ID}"));
                }
                StartRetryTimer();
                DownloadQueue.Advance();
            }
        }
        private void StartRetryTimer()
        {
            downloadRetryTimer.Interval = TimeSpan.FromSeconds(downloadRetryTimerTickValue);
            downloadRetryTimerTickValue *= 2;
            downloadRetryTimer.Start();
        }
        private void InitRetryTimer()
        {
            downloadRetryTimer = new InputTimer();
            downloadRetryTimer.Tick += AutoRetryDownload_Tick;
        }
        private void AutoRetryDownload_Tick(object? sender, EventArgs e)
        {
            downloadRetryTimer?.Stop();
            downloadRetryTimer.Data = "error";
            RetryDownload();
        }
        private void RetryDownload_Click(object sender, RoutedEventArgs e)
        {
            downloadRetryTimer?.Stop();
            downloadRetryTimer.Data = "";
            RetryDownload();
        }
        private void RetryDownload()
        {
            if (IsDownloading())
                return;

            ViewModel.DownloadInfo = string.Empty;
            ViewModel.GameDownloadProgress = 0;
            ViewModel.DownloadFailedVisibility = false;
            // Through the queue, so a retry does not exceed the limits
            DownloadQueue.Enqueue(this, atFront: true);
        }
        private void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsDownloadPaused)
            {
                // Resumed by the user: runs now, even outside of the schedule
                DownloadQueue.Remove(this);
                BypassesQueue = true;
                ViewModel.IsQueued = false;
                pausedBySchedule = false;
                ViewModel.IsDownloadResumed = false;
                ViewModel.IsDownloadPaused = false;
                _ = DownloadGame(true);
            }
            else
            {
                if (client == null)
                    return;

                ViewModel.IsDownloadPaused = true;
                client.Pause();
                IsDownloadActive = false;
                ViewModel.State = "Download Paused";
                DownloadQueue.Advance();
            }
        }
        public void PauseDownload()
        {
            if (client == null || ViewModel.IsDownloadPaused || !IsDownloadActive)
                return;

            ViewModel.IsDownloadPaused = true;
            client.Pause();
            IsDownloadActive = false;
            ViewModel.State = "Download Paused";
        }
        private void DownloadProgress(long totalFileSize, long currentBytesDownloaded, long totalBytesDownloaded, double? progressPercentage, long resumePosition)
        {
            Dispatcher.UIThread.Invoke(delegate
            {
                bool isResume = resumePosition != -1;
                var numerator = isResume ? currentBytesDownloaded : totalBytesDownloaded;
                var denumirator = isResume ? totalFileSize - resumePosition : totalFileSize;
                if (isResume)
                {
                    ViewModel.IsDownloadResumed = true;
                }
                if (currentBytesDownloaded == 0)
                {
                    ViewModel.DownloadInfo = $"{FormatBytesHumanReadable(totalBytesDownloaded)}" + $" of {FormatBytesHumanReadable((double)totalFileSize)}";
                }
                else
                {
                    downloadSpeedCalc.UpdateSpeed(currentBytesDownloaded);
                    ViewModel.DownloadInfo = Loc.F("{0} - {1} of {2} | Time left: {3}", $"{FormatBytesHumanReadable(downloadSpeedCalc.GetCurrentSpeed(), 1, 1000)}/s",
                        FormatBytesHumanReadable(totalBytesDownloaded), FormatBytesHumanReadable((double)totalFileSize),
                        CalculateTimeLeft(denumirator, numerator, (DateTime.Now - startTime).TotalMilliseconds));
                }

                if (ViewModel.GameDownloadProgress == (int)progressPercentage)
                {
                    return;
                }
                ViewModel.GameDownloadProgress = (int)progressPercentage;
                if (ViewModel.GameDownloadProgress == 100)
                {
                    DownloadCompleted();
                }
                });
        }

        private void DownloadCompleted()
        {
            if (client == null)
                return;

            UpdateDataSizeUI();
            ViewModel.DownloadUIVisibility = false;
            IsDownloadActive = false;
            BypassesQueue = false;
            ViewModel.State = "Downloaded";
            DownloadQueue.Advance();
            uiBtnExtract.IsEnabled = true;
            ViewModel.InstallationStepperProgress = 0;
            try
            {
                if (File.Exists(Path.Combine(m_DownloadPath, "gamevault-metadata")))
                    File.Delete(Path.Combine(m_DownloadPath, "gamevault-metadata"));
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            if (!Directory.Exists(ViewModel.InstallPath))
            {
                Directory.CreateDirectory(ViewModel.InstallPath);
            }
            MainWindowViewModel.Instance.Library.GetGameInstalls().AddSystemFileWatcher(ViewModel.InstallPath);

            if (!App.Instance.IsWindowActiveAndControlInFocus(MainControl.Downloads))
                ToastMessageHelper.CreateToastMessage(Loc.T("Download Complete"), ViewModel.Game.Title, Path.Combine(LoginManager.Instance.GetUserProfile().ImageCacheDir, "gbox", $"{ViewModel.Game.ID}.{ViewModel.Game.Metadata?.Cover?.ID}"));

            if (SettingsViewModel.Instance.AutoExtract || PlayWhenInstalled || IsUpdate)
            {
                Dispatcher.UIThread.Invoke((Action)async delegate
                {
                    uiBtnExtract.IsEnabled = false;
                    await Task.Delay(3000);
                    await Extract();
                    uiBtnExtract.IsEnabled = true;
                });
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            CancelDownload();
        }
        private void CancelExtraction_Click(object sender, RoutedEventArgs e)
        {
            extractionCancelled = true;
            sevenZipHelper.Cancel();
        }
        private string FormatBytesHumanReadable(double size, double tspan = 1, int baseVal = 1024)
        {
            try
            {
                double value = size / tspan;
                return (string)gameSizeConverter.Convert(value.ToString(), null, baseVal, null);
            }
            catch (Exception ex)
            {
                return "ERR";
            }
        }

        private string CalculateTimeLeft(long? totalFileSize, long totalBytesRead, double tspanMilliseconds)
        {
            double timeLeftSeconds = (double)((totalFileSize - totalBytesRead) / downloadSpeedCalc.GetCurrentSpeed());
            TimeSpan t = TimeSpan.FromSeconds(0);
            if (timeLeftSeconds > 0 && !double.IsInfinity(timeLeftSeconds) && !double.IsNaN(timeLeftSeconds))
            {
                t = TimeSpan.FromSeconds(timeLeftSeconds);
            }
            return string.Format("{0:00}:{1:00}:{2:00}", ((int)t.TotalHours), t.Minutes, t.Seconds);
        }

        private async void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            await DeleteFile(confirm: true);
        }

        public async Task DeleteFile(bool confirm = true)
        {
            if (IsDownloadActive)
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("Can not delete during the download, extraction, installing process");
                return;
            }

            bool doDelete = true;

            if (confirm)
            {
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("Are you sure you want to delete '{0}' ?", (ViewModel.Game == null ? Loc.T("this Game") : ViewModel.Game.Title)), "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Yes"), NegativeButtonText = Loc.T("No") });

                doDelete = result == MessageDialogResult.Affirmative;
            }

            if (doDelete)
            {
                try
                {
                    downloadRetryTimer?.Stop();

                    if (Directory.Exists(m_DownloadPath))
                        Directory.Delete(m_DownloadPath, true);

                    DownloadsViewModel.Instance.DownloadedGames.Remove(this);

                    //Delete Installation Directory if it is empty (Because GV should not create a filewatcher for it anymore)
                    if (Directory.Exists(ViewModel.InstallPath) && !Directory.EnumerateFileSystemEntries(ViewModel.InstallPath).Any())
                    {
                        Directory.Delete(ViewModel.InstallPath);
                    }
                }
                catch
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Can not delete during the download, extraction, installing process");
                }
            }
        }
        private void OpenDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(m_DownloadPath))
                PlatformInfo.OpenFolder(m_DownloadPath);
        }

        private void GoToGame_Click(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(ViewModel.Game, LoginManager.Instance.IsLoggedIn()));
        }

        private void ExtractionProgress(object sender, SevenZipProgressEventArgs e)
        {
            try
            {
                ViewModel.GameExtractionProgress = e.PercentageDone;
                long totalBytesDownloaded = (Convert.ToInt64(ViewModel.Game.Size) / 100) * e.PercentageDone;
                downloadSpeedCalc.UpdateSpeed(totalBytesDownloaded);
                ViewModel.ExtractionInfo = Loc.F("{0} - {1} of {2} | Time left: {3}", $"{FormatBytesHumanReadable(totalBytesDownloaded, (DateTime.Now - startTime).TotalSeconds, 1000)}/s", FormatBytesHumanReadable(totalBytesDownloaded), FormatBytesHumanReadable(Convert.ToInt64(ViewModel.Game.Size)), CalculateTimeLeft(Convert.ToInt64(ViewModel.Game.Size), totalBytesDownloaded, (DateTime.Now - startTime).TotalMilliseconds));
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void Extract_Click(object sender, RoutedEventArgs e)
        {
            await Extract();
        }
        private async Task<string> MountISO(string ISOPath)
        {
            if (OperatingSystem.IsLinux())
                return await MountISOLinux(ISOPath);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -NoProfile -Command \"$diskImage = Mount-DiskImage -ImagePath '{ISOPath}' -PassThru; ($diskImage | Get-Volume).DriveLetter\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = Process.Start(psi))
                {
                    await process.WaitForExitAsync();
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    return string.IsNullOrEmpty(output) ? string.Empty : output + @":\";
                }
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Loop-mounts an ISO with udisks (no root needed on desktop systems) and returns the mount point.
        /// </summary>
        private static async Task<string> MountISOLinux(string isoPath)
        {
            try
            {
                string? udisksctl = PlatformInfo.FindInPath("udisksctl");
                if (udisksctl == null)
                    return string.Empty;

                string setup = await RunAndRead(udisksctl, "loop-setup", "--no-user-interaction", "-r", "-f", isoPath);
                // "Mapped file /path/game.iso as /dev/loop12."
                var loop = System.Text.RegularExpressions.Regex.Match(setup, @"/dev/loop\d+");
                if (!loop.Success)
                    return string.Empty;

                // udisks usually auto-mounts the loop device; mount it explicitly if it did not.
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    string? mountPoint = FindMountPoint(loop.Value);
                    if (mountPoint != null)
                        return mountPoint;
                    if (attempt == 2)
                    {
                        string mount = await RunAndRead(udisksctl, "mount", "--no-user-interaction", "-b", loop.Value);
                        var path = System.Text.RegularExpressions.Regex.Match(mount, @" at (.+?)\.?$", System.Text.RegularExpressions.RegexOptions.Multiline);
                        if (path.Success && Directory.Exists(path.Groups[1].Value.Trim()))
                            return path.Groups[1].Value.Trim();
                    }
                    await Task.Delay(300);
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            return string.Empty;
        }

        private static string? FindMountPoint(string device)
        {
            foreach (string line in File.ReadAllLines("/proc/mounts"))
            {
                string[] parts = line.Split(' ');
                if (parts.Length > 1 && parts[0] == device)
                    return parts[1].Replace("\\040", " ");
            }
            return null;
        }

        private static async Task<string> RunAndRead(string program, params string[] args)
        {
            var info = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args)
                info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            string output = await process.StandardOutput.ReadToEndAsync();
            output += await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }

        private static bool IsTarArchive(string fileName)
        {
            string name = fileName.ToLowerInvariant();
            return name.EndsWith(".tar") || name.EndsWith(".tar.gz") || name.EndsWith(".tgz") || name.EndsWith(".tar.xz") || name.EndsWith(".txz")
                || name.EndsWith(".tar.bz2") || name.EndsWith(".tbz2") || name.EndsWith(".tar.zst");
        }

        /// <summary>
        /// Linux games usually come as tarballs; tar keeps the executable bits that 7-Zip would drop.
        /// </summary>
        private static async Task<(int ExitCode, string Error)> ExtractTar(string archive, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var info = new ProcessStartInfo("tar") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string arg in new[] { "-xf", archive, "-C", outputDir })
                info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            ProcessShepherd.Instance.AddProcess(process);
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            ProcessShepherd.Instance.RemoveProcess(process);
            return (process.ExitCode, error);
        }

        private async Task Extract()
        {
            if (!Directory.Exists(m_DownloadPath))
            {
                ViewModel.State = "Download path not found";
                MainWindowViewModel.Instance.AppBarText = Loc.T("Please report this issue on our Discord server or create a GitHub issue.");
                return;
            }
            DirectoryInfo dirInf = new DirectoryInfo(m_DownloadPath);
            FileInfo[] files = dirInf.GetFiles().Where(f => ViewModel.SupportedArchives.Contains(f.Extension.ToLower())).ToArray();
            if (files.Length <= 0)
            {
                ViewModel.State = "No archive found";
                return;
            }
            uiBtnInstall.IsEnabled = false;

            //Mount ISO if possible
            if (SettingsViewModel.Instance.MountIso && Path.GetExtension(Path.Combine(m_DownloadPath, files[0].Name)).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                uiBtnExtract.IsEnabled = false;
                mountedDrive = await MountISO(Path.Combine(m_DownloadPath, files[0].Name));
                if (Directory.Exists(mountedDrive))
                {
                    ViewModel.State = Loc.F("ISO mounted at {0}", mountedDrive);
                    ViewModel.InstallationStepperProgress = 1;
                    uiBtnExtract.IsEnabled = true;
                    uiBtnInstall.IsEnabled = true;
                    return;
                }
                ViewModel.State = "Failed to Mount ISO";
                uiBtnExtract.IsEnabled = true;
                return;
            }
            //

            ViewModel.ExtractionUIVisibility = false;
            ViewModel.State = "Extracting...";
            ViewModel.ExtractionUIVisibility = true;
            downloadSpeedCalc = new DownloadSpeedCalculator();//Reuse download speed calculator as extraction speed calculator and set new instance to reset it
            sevenZipHelper.Process -= ExtractionProgress;// once, also after a re-extraction
            sevenZipHelper.Process += ExtractionProgress;
            startTime = DateTime.Now;
            int result;
            string extractionError = "";
            bool useTar = !OperatingSystem.IsWindows() && IsTarArchive(files[0].Name) && PlatformInfo.FindInPath("tar") != null;
            bool isEncrypted = !useTar && await sevenZipHelper.IsArchiveEncrypted(Path.Combine(m_DownloadPath, files[0].Name));
            if (useTar)
            {
                ViewModel.ExtractionInfo = Loc.T("Extracting archive...");
                (result, extractionError) = await ExtractTar(Path.Combine(m_DownloadPath, files[0].Name), Path.Combine(m_DownloadPath, "Extract"));
                ViewModel.GameExtractionProgress = 100;
            }
            else if (isEncrypted)
            {
                string extractionPassword = Preferences.Get(AppConfigKey.ExtractionPassword, LoginManager.Instance.GetUserProfile().UserConfigFile, true);
                if (string.IsNullOrEmpty(extractionPassword))
                {
                    extractionPassword = await App.Instance.MainWindow.ShowInputAsync("Extraction", Loc.T("This archive requires a password to extract"));
                    result = await sevenZipHelper.ExtractArchive(Path.Combine(m_DownloadPath, files[0].Name), Path.Combine(m_DownloadPath, "Extract"), extractionPassword);
                }
                else
                {
                    result = await sevenZipHelper.ExtractArchive(Path.Combine(m_DownloadPath, files[0].Name), Path.Combine(m_DownloadPath, "Extract"), extractionPassword);
                    if (result == 69)//Error code for wrong password
                    {
                        extractionPassword = await App.Instance.MainWindow.ShowInputAsync("Extraction", Loc.T("This archive requires a password to extract"));
                        result = await sevenZipHelper.ExtractArchive(Path.Combine(m_DownloadPath, files[0].Name), Path.Combine(m_DownloadPath, "Extract"), extractionPassword);
                    }
                }
            }
            else
            {
                result = await sevenZipHelper.ExtractArchive(Path.Combine(m_DownloadPath, files[0].Name), Path.Combine(m_DownloadPath, "Extract"));
            }
            if (result == 0)
            {
                if (!File.Exists(Path.Combine(m_DownloadPath, "Extract", "gamevault-metadata")))
                {
                    File.Create(Path.Combine(m_DownloadPath, "Extract", "gamevault-metadata")).Close();
                }
                Preferences.Set(AppConfigKey.ExtractionFinished, "1", Path.Combine(m_DownloadPath, "Extract", "gamevault-metadata"));
                ViewModel.State = "Extracted";
                uiBtnExtract.Text = "Re-Extract";

                ViewModel.InstallationStepperProgress = 1;
                ViewModel.ExtractionUIVisibility = false;

                if (!App.Instance.IsWindowActiveAndControlInFocus(MainControl.Downloads))
                    ToastMessageHelper.CreateToastMessage(Loc.T("Extraction Complete"), ViewModel.Game.Title, Path.Combine(LoginManager.Instance.GetUserProfile().ImageCacheDir, "gbox", $"{ViewModel.Game?.ID}.{ViewModel.Game?.Metadata?.Cover?.ID}"));

                bool portable = ViewModel.Game?.Type == GameType.WINDOWS_PORTABLE || ViewModel.Game?.Type == GameType.LINUX_PORTABLE;
                if (IsUpdate)
                {
                    await Task.Delay(1000);//the extraction stream is closed
                    await ApplyUpdate();
                }
                else if (portable && (SettingsViewModel.Instance.AutoInstallPortable || PlayWhenInstalled))
                {
                    await Task.Delay(1000);//Just to be sure the extraction stream is closed and the files are ready to copy
                    await Install();
                }
                else if (PlayWhenInstalled && ViewModel.Game?.Type == GameType.WINDOWS_SETUP)
                {
                    uiBtnInstall.IsEnabled = true;
                    await InstallSetupForPlay();
                }
                else
                {
                    uiBtnInstall.IsEnabled = true;
                }

                UpdateDataSizeUI();
            }
            else
            {
                if (Directory.Exists(Path.Combine(m_DownloadPath, "Extract")))
                {
                    try
                    {
                        Directory.Delete(Path.Combine(m_DownloadPath, "Extract"), true);
                    }
                    catch (Exception ignored) { Log.Ignored(ignored); }
                }
                if (extractionCancelled)
                {
                    extractionCancelled = false;
                    ViewModel.State = "Extraction cancelled";
                }
                else if (result == 69)
                {
                    ViewModel.State = "Error: Wrong password";
                }
                else if (!downloadedAgainAfterDamage && GameVault.Core.Downloads.ArchiveErrors.IsDamaged(useTar ? extractionError : sevenZipHelper.LastError))
                {
                    // Damaged on the way or on the disk: one new download, then extraction, installation... as before
                    downloadedAgainAfterDamage = true;
                    Log.Info($"The archive of {ViewModel.Game?.Title} is damaged, downloading it again: {(useTar ? extractionError : sevenZipHelper.LastError).Trim()}");
                    ViewModel.ExtractionUIVisibility = false;
                    MainWindowViewModel.Instance.AppBarText = Loc.F("The archive of '{0}' is damaged, it is downloaded again", ViewModel.Game?.Title);
                    try
                    {
                        foreach (FileInfo damaged in files)
                            damaged.Delete();
                        File.Delete(Path.Combine(m_DownloadPath, "gamevault-metadata"));
                    }
                    catch (Exception ex) { Log.Ignored(ex); }
                    DownloadQueue.Enqueue(this, atFront: true);
                    return;
                }
                else
                {
                    ViewModel.State = "Something went wrong during extraction";
                    if (!App.Instance.IsWindowActiveAndControlInFocus(MainControl.Downloads))
                        ToastMessageHelper.CreateToastMessage(Loc.T("Extraction Failed"), ViewModel.Game.Title, Path.Combine(LoginManager.Instance.GetUserProfile().ImageCacheDir, "gbox", $"{ViewModel.Game?.ID}.{ViewModel.Game?.Metadata?.Cover?.ID}"));
                }
                ViewModel.ExtractionUIVisibility = false;
            }
        }
        private void OpenInstallOptions_Click(object? sender, RoutedEventArgs e)
        {
            uiInstallOptions.IsVisible = true;
            LoadSetupExecutables();
        }
        private void InstallOptionCancel_Click(object sender, RoutedEventArgs e)
        {
            uiInstallOptions.IsVisible = false;
        }
        /// <summary>
        /// Removes the installation folder when the setup left nothing in it but GameVault's own settings file.
        /// </summary>
        private async Task<bool> RemoveEmptyInstallationAsync()
        {
            // Some installers hand over to a child process and exit at once
            await Task.Delay(2000);
            try
            {
                if (!Directory.Exists(ViewModel.InstallPath))
                    return false;
                bool empty = Directory.EnumerateFileSystemEntries(ViewModel.InstallPath)
                    .All(entry => Path.GetFileName(entry) == "gamevault-exec" && File.Exists(entry));
                if (empty)
                    Directory.Delete(ViewModel.InstallPath, true);
                return empty;
            }
            catch (Exception ex) { Log.Ignored(ex); return false; }
        }

        private void LoadSetupExecutables()
        {
            string targedDir = (SettingsViewModel.Instance.MountIso && Directory.Exists(mountedDrive)) ? mountedDrive : Path.Combine(m_DownloadPath, "Extract");
            if (Directory.Exists(targedDir))
            {
                Dictionary<string, string> allExecutables = new Dictionary<string, string>();
                foreach (string fileType in Globals.SupportedExecutables)
                {
                    foreach (string entry in Directory.GetFiles(targedDir, $"*.{fileType}", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive }))
                    {
                        string keyToAdd = Path.GetFileName(entry);
                        if (!allExecutables.ContainsKey(keyToAdd))
                        {
                            allExecutables.Add(keyToAdd, entry);
                        }
                        else
                        {
                            allExecutables.Add(entry.Replace(targedDir, ""), entry); ;
                        }
                    }
                }
                uiCbSetupExecutable.ItemsSource = allExecutables;
                if (!string.IsNullOrWhiteSpace(ViewModel.Game?.Metadata?.InstallerExecutable))
                {
                    string wanted = ViewModel.Game?.Metadata?.InstallerExecutable.Replace('\\', '/') ?? "";
                    var candidates = allExecutables.Select((kv, index) => new { kv.Key, kv.Value, Index = index }).ToList();
                    var entry = candidates.FirstOrDefault(kv => kv.Value.Replace('\\', '/').EndsWith("/" + wanted.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault(kv => kv.Value.Replace('\\', '/').Contains(wanted, StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                        uiCbSetupExecutable.SelectedIndex = entry.Index;
                }
                else if (allExecutables.Count == 1)
                {
                    uiCbSetupExecutable.SelectedIndex = 0;
                }
            }
            else
            {
                uiCbSetupExecutable.ItemsSource = null;
            }
        }
        private async void Install_Click(object s, RoutedEventArgs e)
        {
            await Install();
        }
        private async Task Install()
        {
            if (!IsUpdate && InstallViewModel.Instance.InstalledGames.Any(game => game.Key.ID == ViewModel.Game.ID))
            {
                MessageDialogResult result = await App.Instance.MainWindow.ShowMessageAsync(Loc.F("The Game {0} is already installed at \n'{1}'", ViewModel.Game.Title, InstallViewModel.Instance.InstalledGames.First(game => game.Key.ID == ViewModel.Game.ID).Value) +
                       Loc.T("\nWarning: Overwriting an existing installation with a new one may cause data corruption."), "",
                       MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = Loc.T("Continue"), NegativeButtonText = Loc.T("Cancel") });

                if (result == MessageDialogResult.Negative)
                    return;
            }
            string targedDir = (SettingsViewModel.Instance.MountIso && Directory.Exists(mountedDrive)) ? mountedDrive : Path.Combine(m_DownloadPath, "Extract");

            uiBtnInstallPortable.IsEnabled = false;
            uiBtnInstallSetup.IsEnabled = false;
            uiBtnExtract.IsEnabled = false;
            try
            {
                if (!Directory.Exists(ViewModel.InstallPath))//make sure install path exists with file watcher attached
                {
                    Directory.CreateDirectory(ViewModel.InstallPath);
                }
                MainWindowViewModel.Instance.Library.GetGameInstalls().AddSystemFileWatcher(ViewModel.InstallPath);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            if (ViewModel.Game.Type == GameType.WINDOWS_PORTABLE || ViewModel.Game.Type == GameType.LINUX_PORTABLE)
            {
                bool error = false;
                uiProgressRingInstall.IsActive = true;
                await Task.Run(() =>
                {
                    try
                    {
                        if (!Directory.Exists(ViewModel.InstallPath))
                        {
                            Directory.CreateDirectory(ViewModel.InstallPath);
                        }
                        else if (Directory.Exists(Path.Combine(ViewModel.InstallPath, "Files")))
                        {
                            Directory.Delete(Path.Combine(ViewModel.InstallPath, "Files"), true);
                        }
                        if (Path.GetPathRoot(targedDir) == targedDir)
                        {
                            MoveFromRootPath(targedDir, Path.Combine(ViewModel.InstallPath, "Files"));
                        }
                        else
                        {
                            Directory.Move(targedDir, Path.Combine(ViewModel.InstallPath, "Files"));
                        }
                    }
                    catch { error = true; }
                });
                uiBtnInstall.IsEnabled = false;
                uiProgressRingInstall.IsActive = false;

                uiBtnInstallPortable.IsEnabled = true;
                uiBtnInstallSetup.IsEnabled = true;

                ViewModel.State = "Downloaded";
                uiBtnExtract.Text = "Extract";
                if (error)
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Something wen't wrong during installation");
                }
                else
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Successfully installed '{0}'", ViewModel.Game.Title);
                    ViewModel.InstallationStepperProgress = 2;
                    ViewModel.State = "Installed";
                    // The files of this version: an update replaces them and keeps everything else (saves)
                    try { GameVault.Core.Library.InstallManifest.Write(ViewModel.InstallPath, GameVault.Core.Library.InstallManifest.List(Path.Combine(ViewModel.InstallPath, "Files"))); }
                    catch (Exception ex) { Log.Ignored(ex); }

                    //Auto delete files of portable games after successful installation
                    if (SettingsViewModel.Instance.AutoDeletePortableGameFiles)
                    {
                        await DeleteFile(false);
                    }
                }
            }
            else if (ViewModel.Game.Type == GameType.WINDOWS_SETUP)
            {
                string setupEexecutable = string.Empty;
                if (!Directory.Exists(targedDir))
                    return;
                uiProgressRingInstall.IsActive = true;
                setupEexecutable = ((KeyValuePair<string, string>)uiCbSetupExecutable.SelectedItem!).Value;
                if (File.Exists(setupEexecutable))
                {
                    if (OperatingSystem.IsLinux())
                    {
                        Directory.CreateDirectory(ViewModel.InstallPath);
                        try { await GameFixes.PrepareAsync(ViewModel.InstallPath, ViewModel.Game?.Metadata?.Title ?? ViewModel.Game?.Title, status => MainWindowViewModel.Instance.AppBarText = status); }
                        catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = ex.Message; }
                    }
                    Process setupProcess = null;
                    try
                    {
                        setupProcess = ProcessHelper.StartApp(setupEexecutable, ViewModel.Game?.Metadata?.InstallerParameters?.Replace("%INSTALLDIR%", ViewModel.InstallerInstallPath), installationDirectory: ViewModel.InstallPath);
                    }
                    catch (Exception ex) when (!OperatingSystem.IsWindows())
                    {
                        MainWindowViewModel.Instance.AppBarText = ex.Message;
                        uiProgressRingInstall.IsActive = false;
                        return;
                    }
                    catch
                    {
                        try
                        {
                            setupProcess = ProcessHelper.StartApp(setupEexecutable, ViewModel.Game?.Metadata?.InstallerParameters?.Replace("%INSTALLDIR%", ViewModel.InstallerInstallPath), true, ViewModel.InstallPath);
                        }
                        catch
                        {
                            MainWindowViewModel.Instance.AppBarText = Loc.F("Can not execute '{0}'", setupEexecutable);
                        }
                    }
                    if (setupProcess != null)
                    {
                        await setupProcess.WaitForExitAsync();
                        if (IsUpdate && setupProcess.ExitCode != 0)
                        {
                            // The previous version stays installed and recorded
                            MainWindowViewModel.Instance.AppBarText = Loc.F("The installer of '{0}' failed (exit code {1}), the game was not updated", ViewModel.Game?.Title, setupProcess.ExitCode);
                            uiBtnInstallPortable.IsEnabled = true;
                            uiBtnInstallSetup.IsEnabled = true;
                            uiProgressRingInstall.IsActive = false;
                            uiBtnExtract.IsEnabled = true;
                            return;
                        }
                        if (await RemoveEmptyInstallationAsync())
                        {
                            // Cancelled, failed, or installed somewhere else: an empty folder must not count as installed
                            PlayWhenInstalled = false;
                            MainWindowViewModel.Instance.AppBarText = Loc.F("The installer of '{0}' finished without installing anything into '{1}'", ViewModel.Game?.Title, ViewModel.InstallerInstallPath);
                            uiBtnInstallPortable.IsEnabled = true;
                            uiBtnInstallSetup.IsEnabled = true;
                            uiProgressRingInstall.IsActive = false;
                            uiBtnExtract.IsEnabled = true;
                            return;
                        }
                        ViewModel.InstallationStepperProgress = 2;
                        if (InstallViewModel.Instance.InstalledGames.Any(g => g.Key.ID == ViewModel.Game.ID))
                        {
                            ViewModel.InstallationStepperProgress = 2;
                            ViewModel.State = "Installed";
                        }
                    }
                }
                else
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Could not find executable '{0}'", setupEexecutable);
                }
                uiBtnInstallPortable.IsEnabled = true;
                uiBtnInstallSetup.IsEnabled = true;
            }
            uiInstallOptions.IsVisible = false;
            uiProgressRingInstall.IsActive = false;
            uiBtnExtract.IsEnabled = true;
            try
            {
                if (Directory.Exists(ViewModel.InstallPath))
                    InstalledGameState.Record(ViewModel.InstallPath, ViewModel.Game);
                if (IsUpdate && Directory.Exists(ViewModel.InstallPath))
                {
                    // A setup update installed over the existing installation
                    IsUpdate = false;
                    InstallViewModel.Instance.ReplaceInstalledGame(ViewModel.Game);
                }
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
            //Save forced install type for uninstallation
            if (isGameTypeForced && Directory.Exists(ViewModel.InstallPath) && ViewModel?.Game?.Type != null)
            {
                try
                {
                    Preferences.Set(AppConfigKey.ForcedInstallationType, ViewModel?.Game?.Type, Path.Combine(ViewModel.InstallPath, "gamevault-exec"));
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
            //Set default launch parameter if available
            if (!string.IsNullOrWhiteSpace(ViewModel.Game?.Metadata?.LaunchParameters) && Directory.Exists(ViewModel.InstallPath))
            {
                try
                {
                    Preferences.Set(AppConfigKey.LaunchParameter, ViewModel.Game?.Metadata?.LaunchParameters, Path.Combine(ViewModel.InstallPath, "gamevault-exec"));
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
            ApplyLaunchExecutableFromMetadata();

            await StartGameIfRequested();
            if (ViewModel.CreateShortcut == true)
            {
                await Task.Delay(1000);
                var game = InstallViewModel.Instance.InstalledGames.Where(g => g.Key.ID == ViewModel.Game.ID).FirstOrDefault();
                if (game.Key == null || !Directory.Exists(game.Value))
                    return;

                if (!File.Exists(Preferences.Get(AppConfigKey.Executable, Path.Combine(game.Value, "gamevault-exec"))))
                {
                    if (!GameSettingsUserControl.TryPrepareLaunchExecutable(game.Value))
                    {
                        MainWindowViewModel.Instance.AppBarText = Loc.T("Can not create shortcut. No valid Executable found");
                        return;
                    }
                }
                await DesktopHelper.CreateShortcut(game.Key, Preferences.Get(AppConfigKey.Executable, Path.Combine(game.Value, "gamevault-exec")), false);
            }
        }
        /// <summary>The executable named in the game's metadata becomes the one Play starts.</summary>
        private void ApplyLaunchExecutableFromMetadata()
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.Game?.Metadata?.LaunchExecutable) && Directory.Exists(ViewModel.InstallPath))
            {
                try
                {
                    string extension = Path.GetExtension(ViewModel.Game?.Metadata?.LaunchExecutable);
                    var files = Directory.GetFiles(ViewModel.InstallPath, $"*{extension}", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive });
                    string wantedExecutable = ViewModel.Game?.Metadata?.LaunchExecutable.Replace('\\', '/').TrimStart('/') ?? "";
                    // "Game.exe" must not pick "MyGame.exe": a whole path end wins over a partial match
                    var targetFile = files.FirstOrDefault(file => file.Replace('\\', '/').EndsWith("/" + wantedExecutable, StringComparison.OrdinalIgnoreCase))
                        ?? files.FirstOrDefault(file => file.Replace('\\', '/').Contains(wantedExecutable, StringComparison.OrdinalIgnoreCase));
                    if (targetFile != null)
                    {
                        Preferences.Set(AppConfigKey.Executable, targetFile, Path.Combine(ViewModel.InstallPath, "gamevault-exec"));
                    }
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
        }

        #region Update
        /// <summary>Downloads the newer build of an installed game and replaces the installed one with it.</summary>
        public bool IsUpdate { get; set; }

        private async Task ApplyUpdate()
        {
            bool portable = ViewModel.Game?.Type == GameType.WINDOWS_PORTABLE || ViewModel.Game?.Type == GameType.LINUX_PORTABLE;
            if (!portable)
            {
                // Setups install over the existing installation
                LoadSetupExecutables();
                if (!string.IsNullOrWhiteSpace(ViewModel.Game?.Metadata?.InstallerParameters) && uiCbSetupExecutable.SelectedItem != null)
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Updating {0}...", ViewModel.Game?.Title);
                    await Install();
                }
                else
                {
                    uiInstallOptions.IsVisible = true;
                    MainWindowViewModel.Instance.AppBarText = Loc.F("Choose the installer of {0} to update it. Install it into the same folder.", ViewModel.Game?.Title);
                }
                return;
            }
            string extracted = Path.Combine(m_DownloadPath, "Extract");
            string files = Path.Combine(ViewModel.InstallPath, "Files");
            uiProgressRingInstall.IsActive = true;
            uiBtnExtract.IsEnabled = false;
            try
            {
                var oldFiles = GameVault.Core.Library.InstallManifest.Read(ViewModel.InstallPath);
                var newFiles = await Task.Run(() => GameVault.Core.Library.InstallManifest.ApplyUpdate(extracted, files, oldFiles));
                GameVault.Core.Library.InstallManifest.Write(ViewModel.InstallPath, newFiles);
                try { Directory.Delete(extracted, true); }
                catch (Exception ex) { Log.Ignored(ex); }
                InstalledGameState.Record(ViewModel.InstallPath, ViewModel.Game);

                // The executable may have been renamed or moved by the new version
                string settings = Path.Combine(ViewModel.InstallPath, "gamevault-exec");
                if (!File.Exists(Preferences.Get(AppConfigKey.Executable, settings)))
                    Preferences.DeleteKey(AppConfigKey.Executable, settings);
                ApplyLaunchExecutableFromMetadata();

                IsUpdate = false;
                ViewModel.State = "Updated";
                ViewModel.InstallationStepperProgress = 2;
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is up to date{1}", ViewModel.Game?.Title, (string.IsNullOrEmpty(ViewModel.Game?.Version) ? "" : $" ({ViewModel.Game.Version})"));
                InstallViewModel.Instance.ReplaceInstalledGame(ViewModel.Game!);
                if (SettingsViewModel.Instance.AutoDeletePortableGameFiles)
                    await DeleteFile(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Updating {ViewModel.Game?.Title} failed");
                ViewModel.State = "Update failed";
                MainWindowViewModel.Instance.AppBarText = Loc.F("Updating '{0}' failed: {1}", ViewModel.Game?.Title, ex.Message);
            }
            finally
            {
                uiProgressRingInstall.IsActive = false;
                uiBtnExtract.IsEnabled = true;
            }
        }
        #endregion

        public void MoveFromRootPath(string sourceDir, string destinationDir)
        {
            // Create the destination directory if it doesn't exist.
            Directory.CreateDirectory(destinationDir);

            // Copy all files.
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destFilePath = Path.Combine(destinationDir, fileName);
                // You can use overwrite option if necessary
                File.Copy(filePath, destFilePath, overwrite: true);
            }

            // Recursively copy subdirectories.
            foreach (string dirPath in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dirPath);
                string destSubDir = Path.Combine(destinationDir, dirName);
                MoveFromRootPath(dirPath, destSubDir);
            }

            // Do not attempt to delete source since it is on a read-only ISO.
        }
        private void CopyInstallPathToClipboard_Click(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                bool setup = ViewModel.Game?.Type == GameType.WINDOWS_SETUP;
                ClipboardHelper.SetText(setup ? ViewModel.InstallerInstallPath : ViewModel.InstallPath);
                MainWindowViewModel.Instance.AppBarText = Loc.T("Copied Installation Directory to Clipboard");
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        private void ContinueOverwriteGameType_Click(object sender, RoutedEventArgs e)
        {
            if (uiCbOverwriteGameType.SelectedItem is KeyValuePair<GameType, string?> selected)
            {
                var temp = ViewModel.Game;
                temp.Type = selected.Key;
                ViewModel.Game = null;
                ViewModel.Game = temp;
                isGameTypeForced = true;
            }
            else
            {
                MainWindowViewModel.Instance.AppBarText = Loc.T("No gametype selected for overwriting");
            }
        }

        private void InitOverwriteGameType_Click(object sender, RoutedEventArgs e)
        {
            var temp = ViewModel.Game;
            temp.Type = GameType.UNDETECTABLE;
            ViewModel.Game = null;
            ViewModel.Game = temp;
        }
    }
}
