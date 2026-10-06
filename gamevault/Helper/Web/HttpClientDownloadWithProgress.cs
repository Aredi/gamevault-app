using GameVault.Core;
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

        public HttpClientDownloadWithProgress(string downloadUrl, string destinationFolderPath, string fallbackFileName, Dictionary<string, string>? additionalHeader = null)
        {
            DownloadUrl = downloadUrl;
            DestinationFolderPath = destinationFolderPath;
            FallbackFileName = fallbackFileName;
            AdditionalHeader = additionalHeader;
        }

        public async Task StartDownload(bool tryResume = false)
        {

            if (tryResume)
            {
                InitResume();
            }
            else
            {
                //Edge case where the Library download overrrides the current download. But if its was a paused download, we also have to reset the metadata
                if (File.Exists(Path.Combine(DestinationFolderPath, "gamevault-metadata")))
                    File.Delete(Path.Combine(DestinationFolderPath, "gamevault-metadata"));
            }

            using (HttpResponseMessage response = await WebHelper.GetAsync(DownloadUrl, AdditionalHeader, HttpCompletionOption.ResponseHeadersRead))
                await DownloadFileFromHttpResponseMessage(response);
        }
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

        private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
        {
            try
            {
                // A file name, never a path (the header comes from the server)
                FileName = Path.GetFileName(response.Content.Headers.ContentDisposition.FileName.Replace("\"", ""));
                if (string.IsNullOrEmpty(FileName))
                {
                    throw new Exception("Missing response header (Content-Disposition)");
                }
            }
            catch
            {
                FileName = FallbackFileName;
            }
            if (ResumePosition != -1 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
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
                    throw new Exception("Missing response header (Content-Length/X-Download-Size)");
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
                                throw new IOException($"The connection was closed before the download finished ({position} of {expected} bytes)");
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
