using GameVault.Core;
using System;

namespace gamevault.Helper
{
    internal static class ClipboardHelper
    {
        public static async void SetText(string? text)
        {
            try
            {
                var clipboard = (App.Instance.MainWindow ?? App.Instance.ActiveWindow)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(text ?? "");
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }
    }
}
