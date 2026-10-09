using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Downloads of the image cache: a few at a time, and one download per image however many controls ask for it
    /// (the banner, the rows and the grid often show the same cover at once).
    /// </summary>
    public class TaskQueue
    {
        #region Singleton
        private static TaskQueue? instance;
        private static readonly object padlock = new object();

        public static TaskQueue Instance
        {
            get
            {
                lock (padlock)
                {
                    return instance ??= new TaskQueue();
                }
            }
        }
        #endregion

        private const int MaxConcurrentDownloads = 4;
        private readonly SemaphoreSlim slots = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
        private readonly Dictionary<int, Task> running = new();
        private readonly object sync = new();

        /// <summary>Runs the task, or waits for the one already running for this id.</summary>
        public Task Enqueue(Func<Task> taskGenerator, int id)
        {
            lock (sync)
            {
                if (running.TryGetValue(id, out Task? existing))
                    return existing;
                Task task = Run(taskGenerator, id);
                running[id] = task;
                return task;
            }
        }

        private async Task Run(Func<Task> taskGenerator, int id)
        {
            // Asynchronous from here, so the task is registered before it can finish
            await Task.Yield();
            await slots.WaitAsync();
            try
            {
                await taskGenerator();
            }
            finally
            {
                slots.Release();
                lock (sync)
                    running.Remove(id);
            }
        }

        public bool IsAlreadyInProcess(int id)
        {
            lock (sync)
                return running.ContainsKey(id);
        }

        public async Task WaitForProcessToFinish(int id)
        {
            Task? task;
            lock (sync)
                running.TryGetValue(id, out task);
            if (task == null)
                return;
            try { await task; }
            catch (Exception ignored) { GameVault.Core.Log.Ignored(ignored); }
        }
    }
}
