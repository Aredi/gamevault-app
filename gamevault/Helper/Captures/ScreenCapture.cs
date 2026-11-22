using Avalonia.Controls;
using GameVault.Core;
using gamevault.Localization;
using gamevault.ViewModels;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Ctrl+PrtScn on Windows: the screen the game is on, saved as a PNG in Pictures\SanctuaryVault\&lt;game&gt; and
    /// kept with the game's captures. A game in exclusive full screen can come out black: the Game Bar
    /// (Win+Alt+PrtScn) takes those, and its captures are the game's too.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class ScreenCapture
    {
        private const int HotkeyId = 0x5356;
        private const uint ModControl = 0x0002, ModNoRepeat = 0x4000, VkSnapshot = 0x2C, WmHotkey = 0x0312;

        private readonly Window window;
        private readonly Win32Properties.CustomWndProcHookCallback hook;
        private IntPtr handle;

        private ScreenCapture(Window window)
        {
            this.window = window;
            hook = WndProc;
        }

        /// <summary>The shortcut while the window lives (a sign-in makes a new one).</summary>
        public static void Attach(Window window)
        {
            var capture = new ScreenCapture(window);
            Win32Properties.AddWndProcHookCallback(window, capture.hook);
            // Its handle is there from its making (the client can start in the tray); again once shown, to be sure
            capture.Apply();
            window.Opened += (_, _) => { if (capture.handle == IntPtr.Zero) capture.Apply(); };
            SettingsViewModel.Instance.PropertyChanged += Changed;
            GameCaptures.InGameChanged += GameChanged;
            window.Closed += (_, _) =>
            {
                SettingsViewModel.Instance.PropertyChanged -= Changed;
                GameCaptures.InGameChanged -= GameChanged;
                capture.Unregister();
                Win32Properties.RemoveWndProcHookCallback(window, capture.hook);
            };

            void GameChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(capture.Apply);

            void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(SettingsViewModel.CaptureShortcut))
                    capture.Apply();
            }
        }

        private void Apply()
        {
            Unregister();
            // Only while a game runs: out of a game, Ctrl+PrtScn stays another program's (ShareX's region capture)
            if (!SettingsViewModel.Instance.CaptureShortcut || !GameCaptures.InGame || window.TryGetPlatformHandle()?.Handle is not IntPtr hwnd)
                return;
            // Taken by another program: the Game Bar still takes captures, the shortcut is only not there
            if (RegisterHotKey(hwnd, HotkeyId, ModControl | ModNoRepeat, VkSnapshot))
                handle = hwnd;
            else
                Log.Info("Ctrl+PrtScn is taken by another program: no capture shortcut");
        }

        private void Unregister()
        {
            if (handle == IntPtr.Zero)
                return;
            UnregisterHotKey(handle, HotkeyId);
            handle = IntPtr.Zero;
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam == HotkeyId)
            {
                handled = true;
                Task.Run(Take);
            }
            return IntPtr.Zero;
        }

        private static void Take()
        {
            try
            {
                // The screen first, before the game moves on
                using SKBitmap shot = Grab();
                var game = GameCaptures.Playing();
                string title = game?.Title ?? "SanctuaryVault";
                string safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
                if (safe.Length == 0)
                    safe = "SanctuaryVault";
                string folder = game != null ? Path.Combine(GameCaptures.OwnFolder, safe) : GameCaptures.OwnFolder;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"{safe} {DateTime.Now:yyyy-MM-dd HH-mm-ss-fff}.png");
                using (SKData png = shot.Encode(SKEncodedImageFormat.Png, 100))
                using (FileStream file = File.Create(path))
                    png.SaveTo(file);
                if (game != null)
                    GameCaptures.Add(game.Value.Key, path);
                ToastMessageHelper.ShowAsIs(Loc.T("Screenshot saved"), game != null ? title : path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Screenshot");
                ToastMessageHelper.ShowAsIs(Loc.T("Screenshot failed"), ex.Message);
            }
        }

        /// <summary>The whole screen the window in front is on, in its pixels.</summary>
        private static SKBitmap Grab()
        {
            IntPtr monitor = MonitorFromWindow(GetForegroundWindow(), 2 /* the nearest */);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
                throw new InvalidOperationException("No screen");
            int x = info.Monitor.Left, y = info.Monitor.Top, width = info.Monitor.Right - x, height = info.Monitor.Bottom - y;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = CreateCompatibleBitmap(screen, width, height);
            IntPtr old = SelectObject(memory, bitmap);
            try
            {
                bool read = BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */);
                // Out of the memory screen before it is read (and before it is freed, even on a failure)
                SelectObject(memory, old);
                if (!read)
                    throw new InvalidOperationException("The screen could not be read");
                var shot = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
                if (GetDIBits(memory, bitmap, 0, (uint)height, shot.GetPixels(), ref header, 0) != height)
                {
                    shot.Dispose();
                    throw new InvalidOperationException("The screen could not be read");
                }
                return shot;
            }
            finally
            {
                DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, IntPtr bits, ref BitmapInfoHeader info, uint usage);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
