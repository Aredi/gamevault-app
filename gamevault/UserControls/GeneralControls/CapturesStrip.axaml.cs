using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Captures;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    internal sealed class CaptureItem : ViewModelBase
    {
        public CaptureItem(Capture capture) => Capture = capture;
        public Capture Capture { get; }
        public string Name => Path.GetFileName(Capture.Path);
        public bool IsVideo => !CaptureFiles.IsShownImage(Capture.Path);
        public string When => RelativeDate.FormatMoment(Capture.Taken);
        private Bitmap? thumb;
        public Bitmap? Thumb { get => thumb; set { thumb = value; OnPropertyChanged(); } }
    }

    /// <summary>A game's screenshots and videos (see <see cref="GameCaptures"/>); not shown while it has none.</summary>
    public partial class CapturesStrip : UserControl
    {
        /// <summary>The newest: an old game's hundreds of screenshots are in their folder.</summary>
        private const int Shown = 24;
        private string? game;
        private int load;

        public CapturesStrip()
        {
            InitializeComponent();
            uiScroll.AddHandler(PointerWheelChangedEvent, (s, e) => LibraryUserControl.RowWheel(uiScroll, e), RoutingStrategies.Tunnel);
            AttachedToVisualTree += (_, _) => GameCaptures.Added += CaptureAdded;
            DetachedFromVisualTree += (_, _) => GameCaptures.Added -= CaptureAdded;
        }

        /// <summary>"pc:793", "retro:psx:2161".</summary>
        public string? Game
        {
            get => game;
            set
            {
                game = value;
                _ = LoadAsync();
            }
        }

        private void CaptureAdded(string added)
        {
            if (added == game)
                _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            int mine = ++load;
            if (game is not string key)
            {
                IsVisible = false;
                return;
            }
            var captures = await Task.Run(() => GameCaptures.Of(key));
            if (mine != load)
                return;
            var known = (uiItems.ItemsSource as IEnumerable<CaptureItem>)?.ToDictionary(i => i.Capture.Path) ?? new();
            var items = captures.Take(Shown).Select(c => known.TryGetValue(c.Path, out CaptureItem? same) ? same : new CaptureItem(c)).ToList();
            uiItems.ItemsSource = items;
            uiCount.Text = Loc.F(captures.Count == 1 ? "{0} capture" : "{0} captures", captures.Count);
            IsVisible = items.Count > 0;
            foreach (CaptureItem item in items.Where(i => i.Thumb == null && !i.IsVideo))
            {
                // A capture still being written (a 4K one from the Game Bar) is read again a moment later
                for (int attempt = 1; attempt <= 3 && item.Thumb == null; attempt++)
                {
                    try
                    {
                        string path = item.Capture.Path;
                        item.Thumb = await Task.Run(() =>
                        {
                            using FileStream file = File.OpenRead(path);
                            return Bitmap.DecodeToWidth(file, 416);
                        });
                    }
                    // Not an image after all, or still not written
                    catch (Exception ex) when (attempt < 3) { Log.Ignored(ex); await Task.Delay(1000); }
                    catch (Exception ex) { Log.Ignored(ex); }
                    if (mine != load)
                        return;
                }
            }
        }

        private static CaptureItem? ItemOf(object? sender) => (sender as Control)?.DataContext as CaptureItem;

        private void Capture_Click(object? sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is CaptureItem item)
                PlatformInfo.OpenFile(item.Capture.Path);
        }

        private void ShowFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is CaptureItem item && Path.GetDirectoryName(item.Capture.Path) is string folder)
                PlatformInfo.OpenFolder(folder);
        }
    }
}
