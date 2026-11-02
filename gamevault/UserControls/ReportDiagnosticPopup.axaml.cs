using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameVault.Core.Diagnostics;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>The diagnostic a player joined to a report: a lead from its code, the PC, the log.</summary>
    public partial class ReportDiagnosticPopup : UserControl
    {
        private readonly string text = "", title = "";

        public ReportDiagnosticPopup() => InitializeComponent();

        internal ReportDiagnosticPopup(ProblemReportItem report, GameDiagnostic diagnostic) : this()
        {
            (text, title) = (GameDiagnostics.Text(diagnostic), report.Title);
            uiTitle.Text = report.Header;
            uiByline.Text = report.HasDetails ? $"{report.Username} · {report.CategoryText} · « {report.Details} »" : $"{report.Username} · {report.CategoryText}";
            uiFacts.ItemsSource = GameDiagnostics.Facts(diagnostic);
            if (GameDiagnostics.Hint(diagnostic) is var (hint, detail))
            {
                uiHint.IsVisible = true;
                uiHintTitle.Text = hint;
                uiHintDetail.Text = detail;
            }
            uiLogName.Text = diagnostic.LogName ?? Loc.T("Log");
            uiLog.Text = diagnostic.Log ?? Loc.T("No log available");
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();

        private async void Copy_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text);
            MainWindowViewModel.Instance.AppBarText = Loc.T("Diagnostic copied");
        }

        private async void Download_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not { } top)
                    return;
                IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = $"{Sanitized(title)}-diagnostic.txt", DefaultExtension = "txt" });
                if (file == null)
                    return;
                await using Stream stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(text);
            }
            catch (Exception ex) { MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex); }
        }

        private static string Sanitized(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    }
}
