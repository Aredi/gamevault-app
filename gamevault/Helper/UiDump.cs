#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace gamevault.Helper
{
    /// <summary>
    /// Debug builds only: with GAMEVAULT_UI_DUMP=&lt;file&gt;, the visible texts of all windows and their screen
    /// positions are written to that file every two seconds, for automated UI tests without screenshots.
    /// </summary>
    internal static class UiDump
    {
        private static readonly System.Collections.Generic.HashSet<Popup> openPopups = new();

        public static void StartIfRequested()
        {
            string? file = Environment.GetEnvironmentVariable("GAMEVAULT_UI_DUMP");
            if (string.IsNullOrEmpty(file))
                return;
            Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, _) =>
            {
                if (popup.IsOpen) openPopups.Add(popup); else openPopups.Remove(popup);
            });
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                try { File.WriteAllText(file, Dump()); }
                catch { }
            };
            timer.Start();
        }

        private static string Dump()
        {
            var sb = new StringBuilder();
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return "";
            foreach (Window window in desktop.Windows.Where(w => w.IsVisible))
            {
                sb.AppendLine($"# {window.Title} {window.Position} {window.Bounds.Size}");
                DumpTree(sb, window, window);
                // Flyouts and context menus live in their own popup roots
                foreach (Popup popup in openPopups.Where(p => p.IsOpen && p.Child != null).ToList())
                {
                    sb.AppendLine("# popup");
                    if (popup.Child!.GetVisualRoot() is Visual root)
                        DumpTree(sb, root, popup.Child);
                }
            }
            return sb.ToString();
        }

        private static void DumpTree(StringBuilder sb, Visual root, Visual start)
        {
            {
                Visual window = root;
                foreach (Visual visual in start.GetVisualDescendants())
                {
                    if (visual is not Control control || !control.IsEffectivelyVisible || control.Bounds.Width <= 0)
                        continue;
                    string? text = control switch
                    {
                        TextBlock t => t.Text,
                        TextBox t => $"[TextBox] {t.Text}|{t.Watermark}",
                        ComboBox c => $"[ComboBox] {c.SelectedItem}",
                        CheckBox c => $"[CheckBox {(c.IsChecked == true ? "x" : " ")}] {c.Content}",
                        ToggleSwitch t => $"[Toggle {(t.IsChecked == true ? "on" : "off")}] {t.Content}",
                        _ => null,
                    };
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    var topLeft = control.TranslatePoint(new Point(0, 0), window);
                    if (topLeft == null)
                        continue;
                    var scaled = control.TransformToVisual(window);
                    var rect = scaled.HasValue ? new Rect(control.Bounds.Size).TransformToAABB(scaled.Value) : new Rect(topLeft.Value, control.Bounds.Size);
                    var screen = window.PointToScreen(rect.Center);
                    if (rect.Bottom < 0 || rect.Top > window.Bounds.Height)
                        continue;
                    sb.AppendLine($"{screen.X},{screen.Y}\t{text.Replace('\n', ' ')}{(control.IsEnabled ? "" : " (disabled)")}");
                }
            }
        }
    }
}
#endif
