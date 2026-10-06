using gamevault.Localization;
using GameVault.Core;
using GameVault.Core.Downloads;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    public class HttpClientDownloadWithProgress
    {
        private readonly string DownloadUrl;
        private readonly string DestinationFolderPath;
        private string FileName;
        private string FallbackFileName;
        private Dictionary<string, string>? AdditionalHeader;
        private bool Cancelled = false;
        private bool Paused = false;
        private long ResumePosition = -1;
        private long PreResumeSize = -1;
        private DateTime LastTime;
        /// <summary>Content-Length is exact; X-Download-Size (streamed folders) may only be an estimate.</summary>
        private bool ExactSize;


        public delegate void ProgressChangedHandler(long totalFileSize, long currentBytesDownloaded, long totalBytesDownloaded, double? progressPercentage, long resumePosition);

        public event ProgressChangedHandler ProgressChanged;

        /// <summary>Connections for one download: big archives are fetched in that many ranges at once.</summary>
        public int Connections { get; set; } = 1;
        private string MetadataFile => Path.Combine(DestinationFolderPath, "gamevault-metadata");

        public HttpClientDownloadWithProgress(string downloadUrl, string destinationFolderPath, string fallbackFileName, Dictionary<string, string>? additionalHeader = null)
        {
            DownloadUrl = downloadUrl;
            DestinationFolderPath = destinationFolderPath;
            FallbackFileName = fallbackFileName;
            AdditionalHeader = additionalHeader;
        }

        public async Task StartDownload(bool tryResume = false)
        {
            List<DownloadPart>? savedParts = null;
            long savedSize = 0;
            if (tryResume)
            {
                savedParts = DownloadParts.Parse(Preferences.Get(AppConfigKey.DownloadParts, MetadataFile), out savedSize);
                if (savedParts == null)
                    InitResume();
            }
            else
            {
                //Edge case where the Library download overrrides the current download. But if its was a paused download, we also have to reset the metadata
                if (File.Exists(MetadataFile))
                    File.Delete(MetadataFile);
            }

            if (savedParts != null)
            {
                await ResumeParallel(savedParts, savedSize);
                return;
            }
            bool tryParallel = !tryResume || ResumePosition == -1;
            var headers = new Dictionary<string, string>(AdditionalHeader ?? new Dictionary<string, string>());
            // An open range reveals whether the server can send parts of the file
            if (tryParallel && Connections > 1 && !headers.ContainsKey("Range"))
                headers["Range"] = "bytes=0-";
            HttpResponseMessage response = await WebHelper.GetAsync(DownloadUrl, headers, HttpCompletionOption.ResponseHeadersRead);
            try
            {
                // Standard servers answer 206 with the size in Content-Range; GameVault answers 200 and announces ranges
                long? total = response.StatusCode == System.Net.HttpStatusCode.PartialContent
                    ? (response.Content.Headers.ContentRange?.From == 0 ? response.Content.Headers.ContentRange?.Length : null)
                    : (response.Headers.AcceptRanges.Contains("bytes") ? response.Content.Headers.ContentLength : null);
                if (tryParallel && Connections > 1 && total is > 0)
                {
                    var parts = DownloadParts.Plan(total.Value, Connections);
                    if (parts.Count > 1)
                    {
                        ReadFileName(response);
                        await DownloadParallel(parts, total.Value, response);
                        return;
                    }
                }
                await DownloadFileFromHttpResponseMessage(response);
            }
            finally
            {
                response.Dispose();
            }
        }

        #region Parallel download
        private long sessionBytes;

        private async Task ResumeParallel(List<DownloadPart> parts, long totalSize)
        {
            FileName = Preferences.Get(AppConfigKey.DownloadFileName, MetadataFile);
            if (string.IsNullOrEmpty(FileName) || !File.Exists(Path.Combine(DestinationFolderPath, FileName)))
            {
                // Nothing to continue from
                File.Delete(MetadataFile);
                await StartDownload(false);
                return;
            }
            try
            {
                await DownloadParallel(parts, totalSize, null);
            }
            catch (RangeNotSupportedException)
            {
                Log.Info($"Resume of {FileName} not supported by the server, downloading again");
                File.Delete(MetadataFile);
                Connections = 1;
                await StartDownload(false);
            }
        }

        /// <summary>
        /// Fetches every unfinished range with its own connection into the same file. The progress of each range
        /// is saved every two seconds, so a pause, a crash or a lost connection only repeats the last seconds.
        /// </summary>
        private async Task DownloadParallel(List<DownloadPart> parts, long totalSize, HttpResponseMessage? firstResponse)
        {
            string file = Path.Combine(DestinationFolderPath, FileName);
            using (var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                if (stream.Length != totalSize)
                    stream.SetLength(totalSize);
            }
            Preferences.Set(AppConfigKey.DownloadFileName, FileName, MetadataFile);
            PreResumeSize = totalSize;
            ExactSize = true;
            long before = DownloadParts.Downloaded(parts);
            ResumePosition = before > 0 ? before : -1;
            sessionBytes = 0;
            LastTime = DateTime.Now;

            using var stop = new CancellationTokenSource();
            var running = parts.Where(p => !p.IsComplete)
                .Select((part, index) => DownloadPart(part, file, index == 0 && part.Start == 0 ? firstResponse : null, parts.Count, stop.Token))
                .ToList();
            Task all = Task.WhenAll(running);
            while (!all.IsCompleted)
            {
                await Task.WhenAny(all, Task.Delay(500));
                if (Cancelled)
                    stop.Cancel();
                if ((DateTime.Now - LastTime).TotalMilliseconds > 2000)
                {
                    SaveCheckpoint(parts, totalSize);
                    TriggerProgressChanged(totalSize, sessionBytes, DownloadParts.Downloaded(parts));
                    LastTime = DateTime.Now;
                }
            }
            try
            {
                await all;
            }
            catch (Exception) when (Cancelled)
            {
                // Stopped by pause or cancel below
            }
            catch (Exception)
            {
                // One range failed: stop the others, keep what arrived for the retry
                stop.Cancel();
                try { await Task.WhenAll(running); } catch { }
                SaveCheckpoint(parts, totalSize);
                throw (running.Select(t => t.Exception?.InnerException).FirstOrDefault(e => e != null && e is not OperationCanceledException)
                    ?? new IOException(Loc.T("The download was interrupted")));
            }

            if (Cancelled)
            {
                if (Paused)
                {
                    SaveCheckpoint(parts, totalSize);
                    TriggerProgressChanged(totalSize, 0, DownloadParts.Downloaded(parts));
                    return;
                }
                try
                {
                    await Task.Delay(1000);
                    File.Delete(MetadataFile);
                    File.Delete(file);
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                return;
            }
            if (parts.Any(p => !p.IsComplete))
            {
                SaveCheckpoint(parts, totalSize);
                throw new IOException(Loc.T("The connection was closed before the download finished"));
            }
            Preferences.DeleteKey(AppConfigKey.DownloadParts, MetadataFile);
            TriggerProgressChanged(totalSize, sessionBytes, totalSize, completed: true);
        }

        private async Task DownloadPart(DownloadPart part, string file, HttpResponseMessage? response, int connections, CancellationToken stop)
        {
            if (response == null)
            {
                var headers = new Dictionary<string, string>(AdditionalHeader ?? new Dictionary<string, string>());
                headers["Range"] = part.RangeHeader;
                // The speed limit is shared by the connections of the download
                if (headers.TryGetValue("X-Download-Speed-Limit", out string? limit) && long.TryParse(limit, out long total) && connections > 1)
                    headers["X-Download-Speed-Limit"] = Math.Max(1, total / connections).ToString();
                response = await WebHelper.GetAsync(DownloadUrl, headers, HttpCompletionOption.ResponseHeadersRead);
                if (!HonorsRange(response, part.Position, part.End - part.Position + 1))
                {
                    response.Dispose();
                    throw new RangeNotSupportedException();
                }
            }
            using (response)
            using (Stream content = await response.Content.ReadAsStreamAsync(stop))
            // Unbuffered: what the checkpoint counts is already written
            using (var output = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1, true))
            {
                output.Position = part.Position;
                byte[] buffer = new byte[64 * 1024];
                while (!part.IsComplete)
                {
                    if (Cancelled)
                        return;
                    int wanted = (int)Math.Min(buffer.Length, part.End - part.Position + 1);
                    int read = await content.ReadAsync(buffer.AsMemory(0, wanted), stop);
                    if (read == 0)
                        throw new IOException(Loc.F("The connection was closed before the range {0}-{1} finished", part.Start, part.End));
                    await output.WriteAsync(buffer.AsMemory(0, read), stop);
                    part.Position += read;
                    Interlocked.Add(ref sessionBytes, read);
                }
            }
        }

        private void SaveCheckpoint(List<DownloadPart> parts, long totalSize)
        {
            try
            {
                Preferences.Set(AppConfigKey.DownloadParts, DownloadParts.Serialize(totalSize, parts), MetadataFile);
                // Read by the download list to show the paused progress
                Preferences.Set(AppConfigKey.DownloadProgress, $"{DownloadParts.Downloaded(parts)};{totalSize}", MetadataFile);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>
        /// The response holds the requested part of the file: 206 from standard servers, 200 with the part's size
        /// from GameVault (it sends the range with X-Download-Size and no Content-Range). A server that ignores
        /// Range sends the whole file, which has another size.
        /// </summary>
        internal static bool HonorsRange(HttpResponseMessage response, long from, long length)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                return response.Content.Headers.ContentRange?.From is null || response.Content.Headers.ContentRange.From == from;
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return false;
            long? size = response.Content.Headers.ContentLength;
            if (size is null or 0 && response.Headers.TryGetValues("X-Download-Size", out var values) && long.TryParse(values.FirstOrDefault(), out long announced))
                size = announced;
            return size == length;
        }

        private sealed class RangeNotSupportedException : Exception
        {
            public RangeNotSupportedException() : base("The server does not send parts of the file") { }
        }
        #endregion
        private void InitResume()
        {
            string resumeData = Preferences.Get(AppConfigKey.DownloadProgress, Path.Combine(DestinationFolderPath, "gamevault-metadata"));
            if (!string.IsNullOrEmpty(resumeData))
            {
                try
                {
                    string[] resumeDataToProcess = resumeData.Split(";");
                    ResumePosition = long.Parse(resumeDataToProcess[0]);
                    PreResumeSize = long.Parse(resumeDataToProcess[1]);
                    if (AdditionalHeader == null)
                    {
                        AdditionalHeader = new Dictionary<string, string>();                    
                    }
                    AdditionalHeader?.Add("Range", $"bytes={ResumePosition}-");                  
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            }
        }

        private void ReadFileName(HttpResponseMessage response)
        {
            try
            {
                // A file name, never a path (the header comes from the server)
                FileName = Path.GetFileName(response.Content.Headers.ContentDisposition.FileName.Replace("\"", ""));
                if (string.IsNullOrEmpty(FileName))
                {
                    throw new Exception(Loc.T("Missing response header (Content-Disposition)"));
                }
            }
            catch
            {
                FileName = FallbackFileName;
            }
        }

        private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
        {
            ReadFileName(response);
            if (ResumePosition != -1 && !HonorsRange(response, ResumePosition, PreResumeSize - ResumePosition))
            {
                // The server ignored the Range header and sends the whole file: start again instead of appending it
                Log.Info($"Resume of {FileName} not supported by the server, downloading again");
                ResumePosition = -1;
                PreResumeSize = -1;
            }
            var responseContentLength = response.Content.Headers.ContentLength;
            ExactSize = responseContentLength is > 0;
            if (responseContentLength == null || responseContentLength == 0)
            {
                if (response.Headers.TryGetValues("X-Download-Size", out var headerValues) && long.TryParse(headerValues.First(), out long length))
                {
                    responseContentLength = length;
                }
                else
                {
                    throw new Exception(Loc.T("Missing response header (Content-Length/X-Download-Size)"));
                }
            }

            using (var contentStream = await response.Content.ReadAsStreamAsync())
                await ProcessContentStream(responseContentLength.Value, contentStream);
        }

        private async Task ProcessContentStream(long currentDownloadSize, Stream contentStream)
        {
            long currentBytesRead = 0;
            byte[] buffer = new byte[8192];
            bool isMoreToRead = true;
            LastTime = DateTime.Now;
            string fullFilePath = Path.Combine(DestinationFolderPath, FileName);
            using (var fileStream = new FileStream(fullFilePath, ResumePosition == -1 ? FileMode.Create : FileMode.Open, FileAccess.Write, FileShare.None, 8192, true))
            {
                try
                {
                    if (ResumePosition != -1)
                    {
                        fileStream.Position = ResumePosition;
                    }
                    do
                    {
                        if (Cancelled)
                        {
                            if (Paused)
                            {
                                Preferences.Set(AppConfigKey.DownloadProgress, $"{fileStream.Position};{(PreResumeSize == -1 ? currentDownloadSize : PreResumeSize)}", Path.Combine(DestinationFolderPath, "gamevault-metadata"));
                                TriggerProgressChanged(currentDownloadSize, 0, fileStream.Position);
                                fileStream.Close();
                                return;
                            }
                            fileStream.Close();
                            try
                            {
                                await Task.Delay(1000);
                                File.Delete(Path.Combine(DestinationFolderPath, "gamevault-metadata"));
                                File.Delete(fullFilePath);
                            }
                            catch (Exception ignored) { Log.Ignored(ignored); }
                            return;
                        }

                        var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                        {
                            isMoreToRead = false;
                            long position = fileStream.Position;
                            long expected = PreResumeSize == -1 ? currentDownloadSize : PreResumeSize;
                            if (ExactSize && position < expected)
                            {
                                // The connection ended early: keep the checkpoint so the retry resumes here
                                throw new IOException(Loc.F("The connection was closed before the download finished ({0} of {1} bytes)", position, expected));
                            }
                            // Closed before the completion is reported, so the extraction finds the file ready
                            fileStream.Close();
                            TriggerProgressChanged(currentDownloadSize, currentBytesRead, position, completed: true);
                            continue;
                        }

                        await fileStream.WriteAsync(buffer, 0, bytesRead);

                        currentBytesRead += bytesRead;
                        if ((DateTime.Now - LastTime).TotalMilliseconds > 2000)
                        {
                            //Save checkpoints all two seconds in case the app is closed by the user, or hardly crashed
                            Preferences.Set(AppConfigKey.DownloadProgress, $"{fileStream.Position};{(PreResumeSize == -1 ? currentDownloadSize : PreResumeSize)}", Path.Combine(DestinationFolderPath, "gamevault-metadata"));
                            TriggerProgressChanged(currentDownloadSize, currentBytesRead, fileStream.Position);
                            LastTime = DateTime.Now;
                        }
                    }
                    while (isMoreToRead);
                }
                catch (Exception ex)//On exception try to save the download progress and forward the exception
                {
                    GameVault.Core.Log.Ignored(ex);
                    if (currentBytesRead > 0 && fileStream.CanSeek)
                    {
                        fileStream.Flush();
                        Preferences.Set(AppConfigKey.DownloadProgress, $"{fileStream.Position};{(PreResumeSize == -1 ? currentDownloadSize : PreResumeSize)}", Path.Combine(DestinationFolderPath, "gamevault-metadata"));
                    }
                    throw;
                }
            }
        }

        private void TriggerProgressChanged(long totalDownloadSize, long currentBytesRead, long totalBytesRead, bool completed = false)
        {
            if (ProgressChanged == null)
                return;

            totalDownloadSize = PreResumeSize == -1 ? totalDownloadSize : PreResumeSize;
            // Exactly 100 marks the completion: an estimated size must neither stop it at 99 nor push it over 100
            double progressPercentage = completed ? 100 : Math.Min(99.9, (double)totalBytesRead / totalDownloadSize * 100);
            ProgressChanged(totalDownloadSize, currentBytesRead, totalBytesRead, progressPercentage, ResumePosition);
        }
        public void Cancel()
        {
            if (Paused)
            {
                try
                {
                    File.Delete(Path.Combine(DestinationFolderPath, "gamevault-metadata"));
                    File.Delete(Path.Combine(DestinationFolderPath, FileName));
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                return;
            }
            Cancelled = true;
        }
        public void Pause()
        {
            Paused = true;
            Cancelled = true;
        }
    }
}
