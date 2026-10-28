using System;
using gamevault.Localization;
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
        private static TopLevel? Owner => App.Instance.MainWindow is { IsVisible: true } main ? main : App.Instance.ActiveWindow ?? App.Instance.Root;

        public static async Task<string?> PickFolderAsync(string title, string? startDirectory = null)
        {
            var provider = Owner?.StorageProvider;
            if (provider == null)
                return null;
            var options = new FolderPickerOpenOptions { Title = Loc.T(title), AllowMultiple = false };
            if (!string.IsNullOrEmpty(startDirectory))
                options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(startDirectory);
            var result = await provider.OpenFolderPickerAsync(options);
            var folder = result.FirstOrDefault();
            return folder == null ? null : folder.TryGetLocalPath() ?? AndroidFolderPath(folder.Path);
        }

        /// <summary>
        /// Android's picker gives a folder of the phone's storage as a document tree
        /// (content://com.android.externalstorage.documents/tree/primary%3AGames): its path, which the app writes with its
        /// access to all the files ("primary" is the phone's storage, another name a memory card).
        /// </summary>
        internal static string? AndroidFolderPath(Uri uri)
        {
            if (uri.Scheme != "content" || uri.Host != "com.android.externalstorage.documents")
                return null;
            string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int tree = Array.IndexOf(segments, "tree");
            if (tree < 0 || tree + 1 >= segments.Length)
                return null;
            string[] parts = Uri.UnescapeDataString(segments[tree + 1]).Split(':', 2);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[0].Contains('/') || parts[0].Contains(".."))
                return null;
            string root = parts[0] == "primary" ? "/storage/emulated/0" : "/storage/" + parts[0];
            string relative = parts[1].Trim('/');
            if (relative.Split('/').Any(p => p == ".."))
                return null;
            return relative.Length == 0 ? root : root + "/" + relative;
        }

        /// <param name="patterns">Glob patterns per filter name, e.g. { "Images", new[] { "*.png", "*.jpg" } }</param>
        public static async Task<string?> PickFileAsync(string title, IDictionary<string, string[]>? patterns = null, string? startDirectory = null)
        {
            var provider = Owner?.StorageProvider;
            if (provider == null)
                return null;
            var options = new FilePickerOpenOptions { Title = Loc.T(title), AllowMultiple = false };
            if (patterns != null)
                options.FileTypeFilter = patterns.Select(p => new FilePickerFileType(Loc.T(p.Key)) { Patterns = p.Value }).ToList();
            if (!string.IsNullOrEmpty(startDirectory))
                options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(startDirectory);
            var result = await provider.OpenFilePickerAsync(options);
            return result.FirstOrDefault()?.TryGetLocalPath();
        }
    }
}
