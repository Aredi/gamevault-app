using gamevault.Localization;
using GameVault.Core;
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
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;
using gamevault.Windows;
using System.Globalization;
using GameVault.Core.CloudSaves;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using gamevault.Helper.Platform;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace gamevault.Helper.Integrations
{
    internal class SaveGameHelper
    {
        #region Singleton
        private static SaveGameHelper instance = null;
        private static readonly object padlock = new object();

        public static SaveGameHelper Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new SaveGameHelper();
                    }
                    return instance;
                }
            }
        }
        #endregion

        private class SaveGameEntry
        {
            [JsonPropertyName("score")]
            public double Score { get; set; }
        }
        private List<int> runningGameIds = new List<int>();
        private SevenZipHelper zipHelper;
        internal SaveGameHelper()
        {
            zipHelper = new SevenZipHelper();
        }
        /// <param name="force">The user asked for the cloud save: no conflict check (a local copy is still kept).</param>
        internal async Task<string> RestoreBackup(int gameId, string installationDir, bool force = false)
        {
            if (!LoginManager.Instance.IsLoggedIn())
                return CloudSaveStatus.RestoreFailed;

            if (!SettingsViewModel.Instance.CloudSaves)
                return CloudSaveStatus.SettingDisabled;

            try
            {

                string installationId = GetGameInstallationId(installationDir);
                // Decided before the save is downloaded: the question may stay open for a while
                ServerSave? serverSave = await GetServerSave(gameId);
                string ludusaviTitle = "";
                DateTime? localChange = null;
                if (serverSave != null && !serverSave.InstallationId.Equals(installationId, StringComparison.OrdinalIgnoreCase))
                {
                    ludusaviTitle = await LudusaviTitleOf(gameId);
                    localChange = ludusaviTitle == "" ? null : await GetLocalSaveLastChange(ludusaviTitle);
                    if (!force && SaveSync.BeforePlaying(serverSave, installationId, ReadLastSync(installationDir), localChange) == SaveSyncAction.Conflict)
                    {
                        string choice = await AskConflict(gameId, serverSave, localChange!.Value);
                        if (choice == CloudSaveStatus.Cancelled)
                            return CloudSaveStatus.Cancelled;
                        if (choice == CloudSaveStatus.KeptLocal)
                        {
                            string uploaded = await BackupSaveGame(gameId, force: true);
                            return uploaded == CloudSaveStatus.BackupSuccess ? CloudSaveStatus.KeptLocal : uploaded;
                        }
                    }
                }

                using (HttpResponseMessage response = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/savefiles/user/{LoginManager.Instance.GetCurrentUser()!.ID}/game/{gameId}", null, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    string serverFileName = response.Content.Headers.ContentDisposition?.FileName ?? "";
                    string fileName = ServerSave.FromFileName(serverFileName)?.InstallationId ?? serverFileName.Split('_')[1].Split('.')[0];
                    if (fileName != installationId)
                    {
                        if (ludusaviTitle == "")
                        {
                            ludusaviTitle = await LudusaviTitleOf(gameId);
                            localChange = ludusaviTitle == "" ? null : await GetLocalSaveLastChange(ludusaviTitle);
                        }
                        // Whatever happens, the save that is replaced can be taken back
                        if (ludusaviTitle != "" && localChange != null)
                            await KeepLocalCopy(gameId, ludusaviTitle);
                        string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                        Directory.CreateDirectory(tempFolder);
                        try
                        {
                            string archive = Path.Combine(tempFolder, "backup.zip");
                            using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                await contentStream.CopyToAsync(fileStream);
                            }

                            await zipHelper.ExtractArchive(archive, tempFolder);
                            var mappingFile = Directory.GetFiles(tempFolder, "mapping.yaml", SearchOption.AllDirectories);
                            string extractFolder = "";
                            if (mappingFile.Length < 1)
                                throw new Exception(Loc.T("no savegame extracted"));

                            extractFolder = Path.GetDirectoryName(Path.GetDirectoryName(mappingFile[0]));
                            PrepareConfigFile(installationDir, Path.Combine(LoginManager.Instance.GetUserProfile().CloudSaveConfigDir, "config.yaml"));
                            Process process = new Process();
                            ProcessShepherd.Instance.AddProcess(process);
                            process.StartInfo = CreateProcessHeader();
                            process.StartInfo.ArgumentList.Add("--config");
                            process.StartInfo.ArgumentList.Add(LoginManager.Instance.GetUserProfile().CloudSaveConfigDir);
                            process.StartInfo.ArgumentList.Add("restore");
                            process.StartInfo.ArgumentList.Add("--force");
                            process.StartInfo.ArgumentList.Add("--path");
                            process.StartInfo.ArgumentList.Add(extractFolder);
                            process.Start();
                            // Awaited: the restore runs while the UI waits to start the game
                            await process.WaitForExitAsync();
                            ProcessShepherd.Instance.RemoveProcess(process);
                            WriteLastSync(installationDir);
                            return CloudSaveStatus.RestoreSuccess;
                        }
                        finally
                        {
                            try { Directory.Delete(tempFolder, true); }
                            catch (Exception ex) { Log.Ignored(ex); }
                        }
                    }
                    else
                    {
                        return CloudSaveStatus.UpToDate;
                    }
                }
            }
            catch (Exception ex)
            {
                string statusCode = WebExceptionHelper.GetServerStatusCode(ex);
                if (statusCode == "405")
                {
                    MainWindowViewModel.Instance.AppBarText = CloudSaveStatus.ServerSettingDisabled;
                }
                else if (statusCode != "404")
                {
                    MainWindowViewModel.Instance.AppBarText = CloudSaveStatus.RestoreFailed;
                }
            }

            return CloudSaveStatus.RestoreFailed;
        }
        #region Conflicts
        private static string SettingsFile(string installationDir) => Path.Combine(installationDir, "gamevault-exec");

        private static DateTime? ReadLastSync(string installationDir) =>
            DateTime.TryParse(Preferences.Get(AppConfigKey.LastCloudSync, SettingsFile(installationDir)), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time) ? time.ToUniversalTime() : null;

        private static void WriteLastSync(string installationDir)
        {
            try { Preferences.Set(AppConfigKey.LastCloudSync, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), SettingsFile(installationDir)); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        private async Task<string> LudusaviTitleOf(int gameId)
        {
            Game? game = InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key?.ID == gameId).Key;
            string title = game?.Metadata?.Title ?? "";
            if (title == "")
                title = game?.Title ?? "";
            if (title == "")
                return "";
            try { return await SearchForLudusaviGameTitle(title); }
            catch (Exception ex) { Log.Ignored(ex); return ""; }
        }

        /// <summary>When the save files of the game on this computer last changed (Ludusavi knows where they are).</summary>
        private async Task<DateTime?> GetLocalSaveLastChange(string ludusaviTitle)
        {
            try
            {
                var info = CreateProcessHeader(true);
                foreach (string arg in new[] { "--config", LoginManager.Instance.GetUserProfile().CloudSaveConfigDir, "backup", "--preview", "--api", ludusaviTitle })
                    info.ArgumentList.Add(arg);
                using Process process = Process.Start(info)!;
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                await errors;
                return SaveSync.LastChange(SaveSync.SaveFilesFromLudusaviPreview(await output));
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>The newest save on the server, without downloading it.</summary>
        private static async Task<ServerSave?> GetServerSave(int gameId)
        {
            try
            {
                using HttpResponseMessage response = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/savefiles/user/{LoginManager.Instance.GetCurrentUser()!.ID}/game/{gameId}", null, HttpCompletionOption.ResponseHeadersRead);
                return ServerSave.FromFileName(response.Content.Headers.ContentDisposition?.FileName);
            }
            catch (Exception ex)
            {
                // 404: no save on the server yet
                Log.Ignored(ex);
                return null;
            }
        }

        private static async Task<MessageDialogResult> Ask(string message, string title, MessageDialogStyle style, MetroDialogSettings settings)
        {
            var window = App.Instance.MainWindow;
            if (window == null)
                return MessageDialogResult.Affirmative;
            return await Dispatcher.UIThread.InvokeAsync(() => window.ShowMessageAsync(title, message, style, settings));
        }

        private static string GameTitle(int gameId) => InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key?.ID == gameId).Key?.Title ?? "this game";

        /// <summary>Both this computer and another one changed the save: the user decides which one wins.</summary>
        private static async Task<string> AskConflict(int gameId, ServerSave server, DateTime localChange)
        {
            MessageDialogResult result = await Ask(
                Loc.F("The saves of '{0}' differ.\n\nCloud save (from another computer): {1:g}\nSave on this computer: {2:g}\n\nThe save that is replaced is kept in the save history.", GameTitle(gameId), server.UploadedAt.ToLocalTime(), localChange.ToLocalTime()),
                Loc.T("Cloud save conflict"), MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary,
                new MetroDialogSettings { AffirmativeButtonText = Loc.T("Use the cloud save"), FirstAuxiliaryButtonText = Loc.T("Keep this computer's save"), NegativeButtonText = Loc.T("Don't start") });
            return result switch
            {
                MessageDialogResult.Affirmative => CloudSaveStatus.RestoreSuccess,
                MessageDialogResult.FirstAuxiliary => CloudSaveStatus.KeptLocal,
                _ => CloudSaveStatus.Cancelled,
            };
        }

        /// <summary>Another computer uploaded while this one played: asked before its save is replaced.</summary>
        private async Task<bool> MayReplaceServerSave(int gameId, string installationDir)
        {
            ServerSave? server = await GetServerSave(gameId);
            if (!SaveSync.ServerChangedMeanwhile(server, GetGameInstallationId(installationDir), ReadLastSync(installationDir)))
                return true;
            MessageDialogResult result = await Ask(
                Loc.F("Another computer uploaded a save of '{0}' on {1:g}, after this computer last synchronized.\n\nReplace it with the save of this computer?", GameTitle(gameId), server!.UploadedAt.ToLocalTime()),
                Loc.T("Cloud save conflict"), MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = Loc.T("Upload this computer's save"), NegativeButtonText = Loc.T("Keep the cloud save") });
            return result == MessageDialogResult.Affirmative;
        }

        /// <summary>A copy of the local save before it is replaced (Save history, the last 5 per game).</summary>
        private async Task KeepLocalCopy(int gameId, string ludusaviTitle)
        {
            try
            {
                string history = Path.Combine(LoginManager.Instance.GetUserProfile().RootDir, "SaveHistory", gameId.ToString());
                string target = Path.Combine(history, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(target);
                await CreateBackup(ludusaviTitle, target);
                foreach (string old in Directory.GetDirectories(history).OrderByDescending(d => d, StringComparer.Ordinal).Skip(5))
                    Directory.Delete(old, true);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }
        #endregion
        private string GetGameInstallationId(string installationDir)
        {
            string metadataFile = Path.Combine(installationDir, "gamevault-exec");
            string installationId = Preferences.Get(AppConfigKey.InstallationId, metadataFile);
            if (string.IsNullOrWhiteSpace(installationId))
            {
                installationId = Guid.NewGuid().ToString();
                Preferences.Set(AppConfigKey.InstallationId, installationId, metadataFile);
            }
            return installationId;
        }
        internal async Task BackupSaveGamesFromIds(List<int> gameIds)
        {
            var removedIds = runningGameIds.Except(gameIds).ToList();

            foreach (var removedId in removedIds)
            {
                if (!SettingsViewModel.Instance.CloudSaves)
                {
                    break;
                }
                if (!LoginManager.Instance.IsLoggedIn())
                {
                    // Uploaded when the server is back (UploadPendingSaveGamesAsync)
                    AddPendingBackup(removedId);
                    MainWindowViewModel.Instance.AppBarText = Loc.F("{0} The savegame is uploaded when you are back online.", CloudSaveStatus.Offline);
                    continue;
                }
                try
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Uploading Savegame to the Server...");
                    string status = await BackupSaveGame(removedId);
                    MainWindowViewModel.Instance.AppBarText = status;
                }
                catch (Exception ex)
                {
                    GameVault.Core.Log.Ignored(ex);
                    MainWindowViewModel.Instance.AppBarText = CloudSaveStatus.BackupFailed;
                }
            }

            // Find IDs that are new and add them to the list
            var newIds = gameIds.Except(runningGameIds).ToList();
            runningGameIds.AddRange(newIds);

            // Remove IDs that are no longer in the new list
            runningGameIds = runningGameIds.Intersect(gameIds).ToList();
        }
        #region Offline backups
        private static string PendingBackupsFile => Path.Combine(LoginManager.Instance.GetUserProfile().CacheDir, "pendingsaves");

        private static void AddPendingBackup(int gameId)
        {
            try { Preferences.Set(gameId.ToString(), DateTime.Now.ToString("o"), PendingBackupsFile); }
            catch (Exception ex) { Log.Ignored(ex); }
        }

        /// <summary>Backs up and uploads the saves of games that were closed while offline.</summary>
        internal async Task UploadPendingSaveGamesAsync()
        {
            if (!SettingsViewModel.Instance.CloudSaves || !LoginManager.Instance.IsLoggedIn() || !File.Exists(PendingBackupsFile))
                return;
            foreach (string line in File.ReadAllLines(PendingBackupsFile))
            {
                string key = line.Split('=')[0];
                if (!int.TryParse(key, out int gameId))
                    continue;
                if (runningGameIds.Contains(gameId))
                    continue;// still running: backed up when it closes
                try
                {
                    MainWindowViewModel.Instance.AppBarText = Loc.T("Uploading savegames made offline...");
                    string status = await BackupSaveGame(gameId);
                    Log.Info($"Offline savegame of game {gameId}: {status}");
                    MainWindowViewModel.Instance.AppBarText = status;
                    if (status != CloudSaveStatus.BackupUploadFailed)
                        Preferences.DeleteKey(key, PendingBackupsFile);
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }
        #endregion
        /// <param name="force">The user asked for the upload: replaces the server's save without asking.</param>
        internal async Task<string> BackupSaveGame(int gameId, bool force = false)
        {
            if (!SettingsViewModel.Instance.CloudSaves)
                return CloudSaveStatus.SettingDisabled;

            var installedGame = InstallViewModel.Instance?.InstalledGames?.FirstOrDefault(g => g.Key?.ID == gameId);
            string gameMetadataTitle = installedGame?.Key?.Metadata?.Title ?? "";
            if (gameMetadataTitle == "")
            {
                gameMetadataTitle = installedGame?.Key?.Title ?? "";
            }
            string installationDir = installedGame?.Value ?? "";
            if (gameMetadataTitle != "" && installationDir != "")
            {
                PrepareConfigFile(installedGame?.Value!, Path.Combine(LoginManager.Instance.GetUserProfile().CloudSaveConfigDir, "config.yaml"));
                string title = await SearchForLudusaviGameTitle(gameMetadataTitle);
                if (string.IsNullOrEmpty(title))
                    return CloudSaveStatus.BackupFailed;

                string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempFolder);
                try
                {
                    await CreateBackup(title, tempFolder);
                    string archive = Path.Combine(tempFolder, "backup.zip");
                    if (Directory.GetFiles(tempFolder, "mapping.yaml", SearchOption.AllDirectories).Length == 0)
                        return CloudSaveStatus.BackupCreationFailed;
                    await zipHelper.PackArchive(tempFolder, archive);

                    if (!force && !await MayReplaceServerSave(gameId, installationDir))
                        return CloudSaveStatus.KeptCloud;
                    bool success = await UploadSavegame(archive, gameId, installationDir);
                    if (success)
                        WriteLastSync(installationDir);
                    return success ? CloudSaveStatus.BackupSuccess : CloudSaveStatus.BackupUploadFailed;
                }
                finally
                {
                    try { Directory.Delete(tempFolder, true); }
                    catch (Exception ex) { Log.Ignored(ex); }
                }
            }
            return CloudSaveStatus.BackupFailed;
        }
        public void PrepareConfigFile(string installationPath, string yamlPath)
        {
            // Backups store the user folder as G:\gamevault\currentuser so they can be restored on any machine.
            // Windows games on Linux keep their saves in the Wine prefix, so that is the "user folder" there.
            // Each game may have a prefix of its own; Proton prefixes use the "steamuser" account.
            var compatibility = GameCompatibility.ForInstallation(installationPath);
            string userFolder = PlatformInfo.IsWindows
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : compatibility.PrefixUserFolder;

            // Base configuration with redirects (always included)
            var redirects = new List<Dictionary<string, object>>
    {
        new Dictionary<string, object>
        {
            { "kind", "bidirectional" },
            { "source", userFolder },
            { "target", "G:\\gamevault\\currentuser" }
        },
        new Dictionary<string, object>
        {
            { "kind", "bidirectional" },
            { "source", installationPath },
            { "target", "G:\\gamevault\\installation" }
        }
    };



            var roots = new List<Dictionary<string, object>>();
            foreach (DirectoryEntry rootPath in SettingsViewModel.Instance.RootDirectories)
            {
                roots.Add(new Dictionary<string, object>
        {
            { "store", "other" },
            { "path", Path.Combine(rootPath.Uri,"GameVault","Installations") }
        });
            }

            if (!PlatformInfo.IsWindows && Directory.Exists(compatibility.PrefixPath))
            {
                roots.Add(new Dictionary<string, object>
        {
            { "store", "otherWine" },
            { "path", compatibility.PrefixPath }
        });
            }

            // Start with base configuration (redirects and roots always included)
            var yamlData = new Dictionary<string, object>
    {
        { "redirects", redirects },
        { "roots", roots }
    };

            // Add manifest section if custom manifests exist (optional)
            var customLudusaviManifests = SettingsViewModel.Instance.CustomCloudSaveManifests.Where(m => !string.IsNullOrWhiteSpace(m.Uri));

            if (customLudusaviManifests.Any())
            {
                var manifest = new Dictionary<string, object>
        {
            { "enable", SettingsViewModel.Instance.UsePrimaryCloudSaveManifest },
            { "secondary", new List<Dictionary<string, object>>() }
        };

                foreach (DirectoryEntry entry in customLudusaviManifests)
                {
                    ((List<Dictionary<string, object>>)manifest["secondary"]).Add(new Dictionary<string, object>
            {
                { Uri.IsWellFormedUriString(entry.Uri, UriKind.Absolute) ? "url" : "path", entry.Uri },
                { "enable", true }
            });
                }

                yamlData.Add("manifest", manifest);
            }

            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            string result = serializer.Serialize(yamlData);
            File.WriteAllText(yamlPath, result);
        }

        internal async Task<string> SearchForLudusaviGameTitle(string title)
        {
            return await Task.Run<string>(() =>
            {
                Process process = new Process();
                ProcessShepherd.Instance.AddProcess(process);
                process.StartInfo = CreateProcessHeader(true);
                //process.StartInfo.Arguments = $"find \"{title}\" --fuzzy --api";//--normalized
                process.StartInfo.ArgumentList.Add("--config");
                process.StartInfo.ArgumentList.Add(LoginManager.Instance.GetUserProfile().CloudSaveConfigDir);
                process.StartInfo.ArgumentList.Add("find");
                process.StartInfo.ArgumentList.Add(title);
                process.StartInfo.ArgumentList.Add("--fuzzy");
                process.StartInfo.ArgumentList.Add("--api");
                process.EnableRaisingEvents = true;

                List<string> output = new List<string>();

                process.ErrorDataReceived += (sender, e) =>
                {
                    // Debug.WriteLine("ERROR:" + e.Data);
                };
                process.OutputDataReceived += (sender, e) =>
                {
                    output.Add(e.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                ProcessShepherd.Instance.RemoveProcess(process);
                string jsonString = string.Join("", output).Trim();
                var entries = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, SaveGameEntry>>>(jsonString);
                if (entries?.Count > 0 && entries.Values.Count > 0 && entries.Values.First().Values.Count > 0 && entries.Values.First().Values.First().Score > 0.9d)//Make sure Score is set and over 0.9
                {
                    string lunusaviTitle = entries.Values.First().Keys.First();
                    return lunusaviTitle;

                }
                return "";
            });
        }
        private async Task CreateBackup(string lunusaviTitle, string tempFolder)
        {
            await Task.Run(() =>
           {
               Process process = new Process();
               ProcessShepherd.Instance.AddProcess(process);
               process.StartInfo = CreateProcessHeader();
               //process.StartInfo.Arguments = $"--config {LoginManager.Instance.GetUserProfile().CloudSaveConfigDir} backup --force --format \"zip\" --path \"{tempFolder}\" \"{lunusaviTitle}\"";
               process.StartInfo.ArgumentList.Add("--config");
               process.StartInfo.ArgumentList.Add(LoginManager.Instance.GetUserProfile().CloudSaveConfigDir);
               process.StartInfo.ArgumentList.Add("backup");
               process.StartInfo.ArgumentList.Add("--force");
               process.StartInfo.ArgumentList.Add("--format");
               process.StartInfo.ArgumentList.Add("zip");
               process.StartInfo.ArgumentList.Add("--path");
               process.StartInfo.ArgumentList.Add(tempFolder);
               process.StartInfo.ArgumentList.Add(lunusaviTitle);

               process.Start();
               process.WaitForExit();
               ProcessShepherd.Instance.RemoveProcess(process);

           });
        }
        private async Task<bool> UploadSavegame(string saveFilePath, int gameId, string installationDir)
        {
            try
            {
                string installationId = GetGameInstallationId(installationDir);
                using (MemoryStream memoryStream = await FileToMemoryStreamAsync(saveFilePath))
                {
                    await WebHelper.UploadFileAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/savefiles/user/{LoginManager.Instance.GetCurrentUser()!.ID}/game/{gameId}", memoryStream, "x.zip", new List<RequestHeader> { new RequestHeader() { Name = "X-Installation-Id", Value = installationId } });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Uploading the savegame of game {gameId} failed");
                return false;
            }
            return true;
        }
        private async Task<MemoryStream> FileToMemoryStreamAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException(Loc.T("File not found"), filePath);

            MemoryStream memoryStream = new MemoryStream();
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                await fileStream.CopyToAsync(memoryStream);
            }
            memoryStream.Position = 0; // Reset position to beginning
            return memoryStream;
        }
        private ProcessStartInfo CreateProcessHeader(bool redirectConsole = false)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = redirectConsole;
            info.RedirectStandardError = redirectConsole;
            info.UseShellExecute = false;
            var ludusavi = ToolLocator.Ludusavi() ?? throw new FileNotFoundException(ToolLocator.MissingToolMessage("ludusavi"));
            info.FileName = ludusavi.FileName;
            foreach (string arg in ludusavi.PrefixArguments)
                info.ArgumentList.Add(arg);
            return info;
        }
    }
    public struct CloudSaveStatus
    {
        public static string BackupSuccess = "Successfully synchronized the cloud saves";
        public static string BackupFailed = "Something went wrong during the Backup";
        public static string BackupCreationFailed = "Failed to create a copy of your Savegame";
        public static string BackupUploadFailed = "Failed to upload your Savegame to the Server";

        public static string RestoreSuccess = "Successfully synchronized the cloud save";
        public static string RestoreFailed = "Failed to restore the Savegame";
        public static string UpToDate = "Your Savegame is up to date";

        public static string SettingDisabled = "Activate Cloud Saves under Settings -> Integrations -> Cloud Saves";
        public static string ServerSettingDisabled = "Cloud Saves are not enabled on this Server";
        public static string Offline = "Can not synchronize the cloud saves, because you are offline";
        public static string Cancelled = "The game was not started";
        public static string KeptLocal = "This computer's save was kept and uploaded";
        public static string KeptCloud = "The cloud save was kept, this computer's save was not uploaded";
    }
    public class DirectoryEntry
    {
        public string Uri { get; set; }
    }
}
