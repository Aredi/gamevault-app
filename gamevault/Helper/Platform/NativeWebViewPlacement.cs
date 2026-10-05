using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;

namespace gamevault.Helper.Platform
{
    /// <summary>
    /// On Linux the GTK web view behind <see cref="NativeWebView"/> is attached asynchronously. When that happens
    /// after the last layout pass, the native window is never positioned and stays 1x1 (invisible).
    /// A tiny size change after each navigation forces a new placement.
    /// </summary>
    internal static class NativeWebViewPlacement
    {
        public static void Nudge(NativeWebView? webView)
        {
            if (webView == null || !OperatingSystem.IsLinux())
                return;
            Thickness margin = webView.Margin;
            webView.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom + 1);
            Dispatcher.UIThread.Post(() => webView.Margin = margin, DispatcherPriority.Background);
        }

        public static void KeepPlaced(NativeWebView webView)
        {
            webView.NavigationCompleted += (_, _) => Nudge(webView);
        }
    }
}
