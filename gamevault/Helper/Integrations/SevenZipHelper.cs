using GameVault.Core;
using gamevault.Helper.Integrations;
using gamevault.Helper.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace gamevault.Helper
{
    public class SevenZipProgressEventArgs : EventArgs
    {
        public int PercentageDone { get; set; }
        public SevenZipProgressEventArgs(int percentageDone)
        {
            PercentageDone = percentageDone;
        }
    }
    internal class ProcessShepherd
    {
        #region Singleton
        private static ProcessShepherd instance = null;
        private static readonly object padlock = new object();

        public static ProcessShepherd Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new ProcessShepherd();
                    }
                    return instance;
                }
            }
        }
        #endregion
        // Used from the UI thread and from background tasks
        private readonly List<Process> childProcesses = new List<Process>();
        internal void AddProcess(Process process)
        {
            lock (childProcesses)
                childProcesses.Add(process);
        }
        internal void RemoveProcess(Process process)
        {
            lock (childProcesses)
                childProcesses.Remove(process);
        }
        internal void KillAllChildProcesses()
        {
            Process[] processes;
            lock (childProcesses)
                processes = childProcesses.ToArray();
            foreach (Process process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
        }

    }
    internal class SevenZipHelper
    {
        private Process process { get; set; }
        /// <summary>Error output of the last extraction (tells a damaged archive from other failures).</summary>
        public string LastError { get; private set; } = "";
        /// <summary>Bytes the archive takes once extracted, read by <see cref="IsArchiveEncrypted"/>: 0 when it hides its list of files.</summary>
        public long ExtractedSize { get; private set; }
        public delegate void ProcessHandler(object sender, SevenZipProgressEventArgs e);
        public event ProcessHandler Process;
        private ProcessStartInfo CreateProcessHeader()
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            info.FileName = ToolLocator.SevenZip() ?? throw new FileNotFoundException(ToolLocator.MissingToolMessage("7z"));
            return info;
        }
        internal async Task<bool> IsArchiveEncrypted(string archivePath)
        {
            bool result = false;
            Process process = new Process();
            ProcessShepherd.Instance.AddProcess(process);
            process.StartInfo = CreateProcessHeader();
            // A wrong password makes 7-Zip report encrypted headers instead of asking for one
            foreach (string arg in new[] { "l", "-slt", "-pskibidibopmmdadap", archivePath })
                process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            // Both streams at once: a full stderr pipe would block 7-Zip while stdout is read
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            string output = await outputTask;
            string error = await errorTask;
            await process.WaitForExitAsync();
            ExtractedSize = SumSizes(output);
            if (error.Contains("encrypted", StringComparison.OrdinalIgnoreCase) || output.Contains("Encrypted = +"))
            {
                result = true;
            }
            ProcessShepherd.Instance.RemoveProcess(process);
            return result;
        }
        internal static long SumSizes(string listing) =>
            System.Text.RegularExpressions.Regex.Matches(listing, @"^Size = (\d+)", System.Text.RegularExpressions.RegexOptions.Multiline).Sum(m => long.Parse(m.Groups[1].Value));
        /// <param name="excludedNames">Files or folders of these names are left in the archive (wherever they are).</param>
        internal async Task<int> ExtractArchive(string archivePath, string outputDir, string password = "", IEnumerable<string>? excludedNames = null)
        {
            int exitCode = -1;
            await Task.Run(() =>
             {
                 process = new Process();
                 ProcessShepherd.Instance.AddProcess(process);
                 process.StartInfo = CreateProcessHeader();
                 foreach (string arg in new[] { "x", "-y", "-bsp1", $"-o{outputDir}", archivePath })
                     process.StartInfo.ArgumentList.Add(arg);
                 if (!string.IsNullOrEmpty(password))
                 {
                     // One argument: spaces or quotes in the password stay part of it
                     process.StartInfo.ArgumentList.Add($"-p{password}");
                 }
                 foreach (string name in excludedNames ?? Array.Empty<string>())
                     process.StartInfo.ArgumentList.Add($"-xr!{name}");
                 process.EnableRaisingEvents = true;
                 var errors = new System.Text.StringBuilder();
                 process.ErrorDataReceived += (sender, e) =>
                 {
                     if (e.Data == null)
                         return;
                     lock (errors)
                         errors.AppendLine(e.Data);
                     if (e.Data.Contains("Wrong password"))
                     {
                         exitCode = 69;
                     }
                 };

                 if (!Directory.Exists(outputDir))
                 {
                     Directory.CreateDirectory(outputDir);
                 }

                 process.Start();
                 process.BeginErrorReadLine();
                 Task progress = ReadProgressAsync(process.StandardOutput, percentage => Process?.Invoke(this, new SevenZipProgressEventArgs(percentage)));
                 process.WaitForExit();
                 progress.Wait();
                 ProcessShepherd.Instance.RemoveProcess(process);
                 lock (errors)
                     LastError = errors.ToString();
             });
            try
            {
                if (exitCode == -1)
                {
                    return process.ExitCode;
                }
                return exitCode;
            }
            catch
            {
                return exitCode;
            }
        }
        /// <summary>
        /// 7-Zip redraws its percentage with backspaces, never ending the line: read line by line, the progress only
        /// came at the end. The output is cut on backspaces too.
        /// </summary>
        internal static async Task ReadProgressAsync(TextReader output, Action<int> report)
        {
            char[] buffer = new char[4096];
            var segment = new System.Text.StringBuilder();
            int last = -1;
            void Flush()
            {
                string text = segment.ToString();
                segment.Clear();
                int index = text.IndexOf('%');
                if (index > 0 && int.TryParse(text[..index].Trim(), out int percentage) && percentage != last)
                    report(last = percentage);
            }
            int read;
            while ((read = await output.ReadAsync(buffer, 0, buffer.Length)) > 0)
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] is '\b' or '\r' or '\n')
                        Flush();
                    else
                        segment.Append(buffer[i]);
                }
            Flush();
        }

        internal async Task PackArchive(string directoryToPack, string archiveName)
        {
            await Task.Run(() =>
            {
                process = new Process();
                ProcessShepherd.Instance.AddProcess(process);
                process.StartInfo = CreateProcessHeader();
                process.StartInfo.ArgumentList.Add("a");
                process.StartInfo.ArgumentList.Add(archiveName);
                process.StartInfo.ArgumentList.Add(directoryToPack);
                process.ErrorDataReceived += (sender, e) =>
                {
                    Debug.WriteLine("ERROR" + e.Data);
                };
                process.OutputDataReceived += (sender, e) =>
                {
                    Debug.WriteLine(e.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                ProcessShepherd.Instance.RemoveProcess(process);
            });
        }
        internal void Cancel()
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) { Log.Ignored(ex); }// not started yet, or already finished
        }
    }
}
