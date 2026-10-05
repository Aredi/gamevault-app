using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Folder / file pickers (replacement for WinForms FolderBrowserDialog and Win32 OpenFileDialog).
    /// </summary>
    internal static class StorageHelper
    {
        private static TopLevel? Owner => App.Instance.MainWindow is { IsVisible: true } main ? main : App.Instance.ActiveWindow;

        public static async Task<string?> PickFolderAsync(string title, string? startDirectory = null)
        {
            var provider = Owner?.StorageProvider;
            if (provider == null)
                return null;
            var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            if (!string.IsNullOrEmpty(startDirectory))
                options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(startDirectory);
            var result = await provider.OpenFolderPickerAsync(options);
            return result.FirstOrDefault()?.TryGetLocalPath();
        }

        /// <param name="patterns">Glob patterns per filter name, e.g. { "Images", new[] { "*.png", "*.jpg" } }</param>
        public static async Task<string?> PickFileAsync(string title, IDictionary<string, string[]>? patterns = null, string? startDirectory = null)
        {
            var provider = Owner?.StorageProvider;
            if (provider == null)
                return null;
            var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
            if (patterns != null)
                options.FileTypeFilter = patterns.Select(p => new FilePickerFileType(p.Key) { Patterns = p.Value }).ToList();
            if (!string.IsNullOrEmpty(startDirectory))
                options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(startDirectory);
            var result = await provider.OpenFilePickerAsync(options);
            return result.FirstOrDefault()?.TryGetLocalPath();
        }
    }
}
