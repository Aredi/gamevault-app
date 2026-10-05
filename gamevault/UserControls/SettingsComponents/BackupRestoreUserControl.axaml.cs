using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;

namespace gamevault.UserControls.SettingsComponents
{
    /// <summary>
    /// Interaction logic for BackupRestoreUserControl.xaml
    /// </summary>
    public partial class BackupRestoreUserControl : UserControl
    {
        public BackupRestoreUserControl()
        {
            InitializeComponent();

        }
        private void BackupRestorePopup_Close(object? sender, PointerReleasedEventArgs e)
        {
            MainWindowViewModel.Instance.ClosePopup();
        }

        private async void ChooseBackupDirectory_Click(object sender, RoutedEventArgs e)
        {
            string? selectedPath = await StorageHelper.PickFolderAsync("Select the backup directory");
            if (!string.IsNullOrEmpty(selectedPath) && Directory.Exists(selectedPath))
            {
                uiBackupDirectory.Text = selectedPath;
                Backup_PasswordChanged(null, null);
            }
        }
        private void Backup_PasswordChanged(object? sender, TextChangedEventArgs? e)
        {
            if (!string.IsNullOrEmpty(uiBackupDatabasePassword.Text) && !string.IsNullOrEmpty(uiBackupDirectory.Text))
            {
                uiBtnStartBackup.IsEnabled = true;
            }
            else
            {
                uiBtnStartBackup.IsEnabled = false;
            }
        }

        private async void StartBackup_Click(object sender, RoutedEventArgs e)
        {
            uiBtnStartBackup.IsEnabled = false;
            this.IsEnabled = false;
            try
            {
                Dictionary<string,string>? additionalRequestHeaders = new Dictionary<string, string>
                {
                    { "X-Database-Password", uiBackupDatabasePassword.Text }
                };
                HttpClientDownloadWithProgress httpClientDownloadWithProgress = new HttpClientDownloadWithProgress(@$"{SettingsViewModel.Instance.ServerUrl}/api/admin/database/backup",
                    uiBackupDirectory.Text, $"DB_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db", additionalRequestHeaders);

                await httpClientDownloadWithProgress.StartDownload();
                MainWindowViewModel.Instance.AppBarText = "Successfully performed database backup.";
            }
            catch (HttpRequestException httpEx)
            {
                if (httpEx.StatusCode == HttpStatusCode.Unauthorized)
                {
                    MainWindowViewModel.Instance.AppBarText = "Unauthorized. Either the database password is wrong or you are not logged in.";
                }
                else
                {
                    MainWindowViewModel.Instance.AppBarText = $"Http Error: {httpEx.Message}";
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = $"Error: {ex.Message}";
            }
            uiBtnStartBackup.IsEnabled = true;
            this.IsEnabled = true;
        }
        //### RESTORE ###
        private async void ChooseRestoreFile_Click(object sender, RoutedEventArgs e)
        {
            string? file = await StorageHelper.PickFileAsync("Select the database file", new Dictionary<string, string[]> { { "Database File", new[] { "*.db" } } });
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
            {
                uiRestoreFile.Tag = file;
                uiRestoreFile.Text = Path.GetFileName(file);
                Restore_PasswordChanged(null, null);
            }
        }

        private void Restore_PasswordChanged(object? sender, TextChangedEventArgs? e)
        {
            if (!string.IsNullOrEmpty(uiRestoreDatabasePassword.Text) && !string.IsNullOrEmpty(uiRestoreFile.Text))
            {
                uiBtnStartRestore.IsEnabled = true;
            }
            else
            {
                uiBtnStartRestore.IsEnabled = false;
            }
        }

        private async void StartRestore_Click(object sender, RoutedEventArgs e)
        {
            uiBtnStartRestore.IsEnabled = false;
            this.IsEnabled = false;
            try
            {
                await WebHelper.UploadFileAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/admin/database/restore", File.OpenRead(uiRestoreFile.Tag.ToString()), uiRestoreFile.Text, new List<RequestHeader> { new RequestHeader() { Name = "X-Database-Password", Value = uiRestoreDatabasePassword.Text } });
                MainWindowViewModel.Instance.AppBarText = "Successfully uploaded database file";
            }
            catch (HttpRequestException httpEx)
            {
                if (httpEx.StatusCode == HttpStatusCode.Unauthorized)
                {
                    MainWindowViewModel.Instance.AppBarText = "Unauthorized. Either the database password is wrong or you are not logged in.";
                }
                else
                {
                    MainWindowViewModel.Instance.AppBarText = $"Http Error: {httpEx.Message}";
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.Instance.AppBarText = $"Error: {ex.Message}";
            }
            uiBtnStartRestore.IsEnabled = true;
            this.IsEnabled = true;
        }
    }
}
