using Avalonia;
using Avalonia.Threading;
using System;

namespace gamevault.Helper
{
    internal static class ScreenHelper
    {
        private static Size? cached;

        /// <summary>
        /// Size of the primary screen in device independent pixels (like WPF's SystemParameters.FullPrimaryScreen*).
        /// </summary>
        public static Size PrimaryScreenSize
        {
            get
            {
                if (cached != null)
                    return cached.Value;
                Size size = new Size(1920, 1080);
                try
                {
                    Size Read()
                    {
                        var screens = App.Instance.ActiveWindow?.Screens;
                        var primary = screens?.Primary;
                        if (primary == null)
                            return size;
                        return new Size(primary.WorkingArea.Width / primary.Scaling, primary.WorkingArea.Height / primary.Scaling);
                    }
                    size = Dispatcher.UIThread.CheckAccess() ? Read() : Dispatcher.UIThread.Invoke(Read);
                    cached = size;
                }
                catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                return size;
            }
        }
    }
}
