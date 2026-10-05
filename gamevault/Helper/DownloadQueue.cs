using Avalonia.Threading;
using GameVault.Core;
using gamevault.UserControls;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// Starts downloads in the order they were requested, respecting the maximum number of simultaneous
    /// downloads and the download schedule (Settings → Installation). Downloads still running when the
    /// schedule window closes are paused and continue when it opens again.
    /// </summary>
    internal static class DownloadQueue
    {
        private static readonly List<GameDownloadUserControl> waiting = new();
        private static DispatcherTimer? timer;

        public static bool IsWaiting(GameDownloadUserControl download) => waiting.Contains(download);

        public static void Enqueue(GameDownloadUserControl download, bool atFront = false)
        {
            if (!waiting.Contains(download))
            {
                if (atFront)
                    waiting.Insert(0, download);
                else
                    waiting.Add(download);
            }
            if (timer == null)
            {
                // Opens and closes the schedule window without user interaction
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                timer.Tick += (_, _) => Advance();
                timer.Start();
            }
            Advance();
        }

        public static void Remove(GameDownloadUserControl download)
        {
            if (waiting.Remove(download))
                RefreshStates();
        }

        /// <summary>Starts waiting downloads while there are free slots; call it when a download ends.</summary>
        public static void Advance()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(Advance);
                return;
            }
            try
            {
                var schedule = SettingsViewModel.Instance.DownloadSchedule;
                if (!schedule.IsAllowed(DateTime.Now))
                {
                    // Outside of the window: pause what the queue started, keep the order
                    foreach (var running in Running().Where(d => !d.BypassesQueue).Reverse().ToList())
                    {
                        running.PauseForSchedule();
                        if (!waiting.Contains(running))
                            waiting.Insert(0, running);
                    }
                    RefreshStates();
                    return;
                }
                int max = SettingsViewModel.Instance.MaxConcurrentDownloads;
                while (waiting.Count > 0 && (max == 0 || Running().Count(d => !d.BypassesQueue) < max))
                {
                    var next = waiting[0];
                    waiting.RemoveAt(0);
                    next.StartFromQueue();
                }
                RefreshStates();
            }
            catch (Exception ex) { Log.Error(ex, "Download queue"); }
        }

        private static IEnumerable<GameDownloadUserControl> Running() =>
            DownloadsViewModel.Instance.DownloadedGames.Where(d => d.IsDownloading());

        private static void RefreshStates()
        {
            var schedule = SettingsViewModel.Instance.DownloadSchedule;
            bool open = schedule.IsAllowed(DateTime.Now);
            for (int i = 0; i < waiting.Count; i++)
            {
                waiting[i].SetQueuedState(open
                    ? $"Queued ({i + 1})"
                    : $"Scheduled, starts at {schedule.NextStart(DateTime.Now):HH:mm}");
            }
        }
    }
}
