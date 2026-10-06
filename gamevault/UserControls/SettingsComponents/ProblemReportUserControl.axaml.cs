using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.IO;

namespace gamevault.UserControls.SettingsComponents
{
    /// <summary>Creates a problem report (zip without secrets) the user sends to the administrator.</summary>
    public partial class ProblemReportUserControl : UserControl
    {
        private string? report;

        public ProblemReportUserControl()
        {
            InitializeComponent();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    MainWindowViewModel.Instance.ClosePopup();
            };
        }

        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            uiBtnCreate.IsEnabled = false;
            uiStatus.Text = Loc.T("Creating the report...");
            try
            {
                report = await ProblemReport.CreateAsync();
                uiStatus.Text = Loc.F("The report is ready:\n{0}\n\nSend this file to the administrator of your GameVault server.", report);
                uiBtnOpenFolder.IsVisible = true;
                uiBtnCopyPath.IsVisible = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Creating the problem report failed");
                uiStatus.Text = Loc.F("The report could not be created: {0}", ex.Message);
            }
            finally
            {
                uiBtnCreate.IsEnabled = true;
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (report != null && Path.GetDirectoryName(report) is string folder)
                PlatformInfo.OpenFolder(folder);
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (report == null)
                return;
            ClipboardHelper.SetText(report);
            MainWindowViewModel.Instance.AppBarText = Loc.T("Path copied to the clipboard");
        }

        private void GitHub_Click(object sender, RoutedEventArgs e) =>
            PlatformInfo.OpenUrl($"https://github.com/{AppRepository.Owner}/{AppRepository.Name}/issues/new");

        private void Close_Click(object? sender, PointerReleasedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }
}
