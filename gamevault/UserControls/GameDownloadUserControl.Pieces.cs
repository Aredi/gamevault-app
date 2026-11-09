using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Downloads;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// An update by pieces: the zip of the new version stays on the server, only its files that differ from the
    /// installed ones are read out of it. Anything that prevents it (another archive type, a password, a server
    /// that does not send parts) falls back to the download of the whole archive.
    /// </summary>
    public partial class GameDownloadUserControl
    {
        /// <summary>Above this share of the zip to fetch, the whole download (several connections) is as fast.</summary>
        private const double PiecesWorthIt = 0.7;
        private const int PiecesAttempts = 3;

        private CancellationTokenSource? piecesCancel;
        private Task? piecesRunning;
        private HttpResponseMessage? piecesResponse;
        private bool piecesRefused;
        private int piecesFailures;

        private string PiecesStaging => Path.Combine(m_DownloadPath, "Pieces");

        private bool CanUpdateByPieces => IsUpdate && !piecesRefused
            && ViewModel.Game?.Type is GameType.WINDOWS_PORTABLE or GameType.LINUX_PORTABLE
            && string.Equals(Path.GetExtension(ViewModel.Game.Path), ".zip", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(ViewModel.Game.Size, out long size) && size > 0
            && Directory.Exists(Path.Combine(ViewModel.InstallPath, "Files"));

        private async Task UpdateByPieces()
        {
            IsDownloadActive = true;
            ViewModel.GameDownloadProgress = 0;
            ViewModel.DownloadInfo = string.Empty;
            ViewModel.State = "Comparing with the installed files...";
            ViewModel.DownloadUIVisibility = true;
            ViewModel.DownloadFailedVisibility = false;
            var cancel = piecesCancel = new CancellationTokenSource();
            // A paused attempt may still be closing its files
            if (piecesRunning != null)
            {
                try { await piecesRunning; }
                catch (Exception ex) { Log.Ignored(ex); }
            }
            Game game = ViewModel.Game;
            long size = long.Parse(game.Size);
            Task<DeltaPlan?> run = Task.Run(() => FetchPieces(game, size, cancel.Token));
            piecesRunning = run;
            DeltaPlan? plan = null;
            Exception? failure = null;
            try { plan = await run; }
            catch (Exception ex) { failure = ex; }
            if (cancel.IsCancellationRequested)
                return;// paused or cancelled: whoever asked has set the state
            piecesCancel = null;
            IsDownloadActive = false;

            if (failure is NotEnoughSpaceException)
            {
                ViewModel.State = failure.Message;
                ViewModel.DownloadUIVisibility = false;
                ViewModel.DownloadFailedVisibility = true;
                DownloadQueue.Advance();
                return;
            }
            // The zip cannot be read by pieces, or the network failed too often: the whole archive, as before
            bool unreadable = failure is NotSupportedException || (failure is InvalidDataException && failure.InnerException is not (IOException or HttpRequestException));
            if (unreadable || (failure == null && plan == null) || (failure != null && ++piecesFailures >= PiecesAttempts))
            {
                if (failure != null)
                    Log.Info($"{game.Title} is updated from the whole archive: {failure.Message}");
                piecesRefused = true;
                DeletePieces();
                await DownloadGame(false);
                return;
            }
            if (failure != null)
            {
                Log.Info($"The update of {game.Title} stopped, it goes on where it was: {failure.Message}");
                ViewModel.State = $"Error: '{failure.Message}'";
                ViewModel.DownloadUIVisibility = false;
                ViewModel.DownloadFailedVisibility = true;
                StartRetryTimer();
                DownloadQueue.Advance();
                return;
            }

            ViewModel.GameDownloadProgress = 100;
            ViewModel.DownloadUIVisibility = false;
            BypassesQueue = false;
            DownloadQueue.Advance();
            await ApplyUpdate(PiecesStaging, plan);
            if (ViewModel.State == "Updated")
            {
                Log.Info($"{game.Title} updated by pieces: {plan!.Changed.Count} of {plan.Files.Count} files, {plan.DownloadSize} of {size} bytes");
                MainWindowViewModel.Instance.AppBarText = Loc.F("'{0}' is up to date: {1} downloaded instead of {2}", game.Title, GameSizeConverter.Text(plan.DownloadSize), GameSizeConverter.Text(size));
                DeletePieces();
                try { await CacheHelper.CreateOfflineCacheAsync(game); }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }

        /// <summary>Compares, then fetches the changed files into the staging folder. Null: not worth it or not possible by pieces.</summary>
        private DeltaPlan? FetchPieces(Game game, long size, CancellationToken cancel)
        {
            string files = Path.Combine(ViewModel.InstallPath, "Files");
            string url = $"{SettingsViewModel.Instance.ServerUrl}/api/games/{game.ID}/download";
            var shown = Stopwatch.StartNew();
            using var served = new RangeStream(size, from => OpenFrom(url, from, size));
            // A read waiting for the network does not see the cancellation: its connection is closed
            using var stop = cancel.Register(() => piecesResponse?.Dispose());
            try
            {
                using var zip = new ZipArchive(served, ZipArchiveMode.Read, leaveOpen: true);
                DeltaPlan? plan = ZipDelta.Plan(zip, files, FileSums.Read(ViewModel.InstallPath), (done, total) =>
                {
                    if (shown.ElapsedMilliseconds < 200)
                        return;
                    shown.Restart();
                    int percent = total == 0 ? 0 : (int)(done * 100 / total);
                    Dispatcher.UIThread.Post(() => { if (!cancel.IsCancellationRequested) ViewModel.GameDownloadProgress = Math.Min(99, percent); });
                }, cancel);
                if (plan == null || plan.DownloadSize > size * PiecesWorthIt)
                    return null;
                if (FreeSpace(m_DownloadPath) is long free && free < plan.ExtractedSize)
                    throw new NotEnoughSpaceException(Loc.F("Not enough space to update: {0} needed, {1} free.", GameSizeConverter.Text(plan.ExtractedSize), GameSizeConverter.Text(free)));

                long toFetch = Math.Max(1, plan.DownloadSize);
                long before = served.Fetched;
                long last = before;
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested)
                        return;
                    ViewModel.State = "Downloading the changed files...";
                    ViewModel.GameDownloadProgress = 0;
                    startTime = DateTime.Now;
                    downloadSpeedCalc = new DownloadSpeedCalculator();
                });
                ZipDelta.Fetch(plan, PiecesStaging, files, () =>
                {
                    if (shown.ElapsedMilliseconds < 200)
                        return;
                    shown.Restart();
                    long now = served.Fetched;
                    long step = now - last;
                    last = now;
                    long fetched = Math.Min(toFetch, now - before);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (cancel.IsCancellationRequested)
                            return;
                        downloadSpeedCalc.UpdateSpeed(step);
                        ViewModel.DownloadInfo = Loc.F("{0} - {1} of {2} | Time left: {3}", $"{FormatBytesHumanReadable(downloadSpeedCalc.GetCurrentSpeed(), 1, 1000)}/s",
                            FormatBytesHumanReadable(fetched), FormatBytesHumanReadable(toFetch), CalculateTimeLeft(toFetch, fetched, (DateTime.Now - startTime).TotalMilliseconds));
                        ViewModel.GameDownloadProgress = (int)Math.Min(99, fetched * 100 / toFetch);
                    });
                }, cancel);
                return plan;
            }
            finally
            {
                piecesResponse?.Dispose();
                piecesResponse = null;
            }
        }

        private Stream OpenFrom(string url, long from, long size)
        {
            var headers = new Dictionary<string, string> { ["Range"] = $"bytes={from}-" };
            if (SettingsViewModel.Instance.DownloadLimit > 0)
                headers["X-Download-Speed-Limit"] = SettingsViewModel.Instance.DownloadLimit.ToString();
            piecesResponse?.Dispose();
            HttpResponseMessage response = WebHelper.GetAsync(url, headers, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            if (!HttpClientDownloadWithProgress.HonorsRange(response, from, size - from))
            {
                response.Dispose();
                throw new NotSupportedException("The server does not send parts of the file");
            }
            piecesResponse = response;
            return response.Content.ReadAsStream();
        }

        /// <summary>Stops the update by pieces; the files fetched so far stay for the next attempt.</summary>
        private bool StopPieces()
        {
            if (piecesCancel == null)
                return false;
            piecesCancel.Cancel();
            piecesCancel = null;
            return true;
        }

        private void DeletePieces()
        {
            try
            {
                if (Directory.Exists(PiecesStaging))
                    Directory.Delete(PiecesStaging, true);
                // Nothing else was downloaded for this update
                if (Directory.Exists(m_DownloadPath) && Directory.GetFileSystemEntries(m_DownloadPath).Length == 0)
                    Directory.Delete(m_DownloadPath);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private sealed class NotEnoughSpaceException : IOException
        {
            public NotEnoughSpaceException(string message) : base(message) { }
        }
    }
}
