using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace gamevault.Android.Retro
{
    /// <summary>
    /// The game full screen: its picture as large as the screen allows (in the console's proportions, sharp pixels),
    /// the touch pad over it while no controller is there, a menu (its button at the top, Back): Resume or Quit.
    /// </summary>
    internal sealed unsafe class RetroPlayerView : Panel
    {
        /// <summary>The game's picture, stretched to the control, sharp pixels.</summary>
        private sealed class Screen : Control
        {
            public Bitmap? Bitmap { get; set; }

            public override void Render(DrawingContext context)
            {
                if (Bitmap is not Bitmap bitmap)
                    return;
                using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                    context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(Bounds.Size));
            }
        }

        private readonly RetroPlayer player;
        private readonly string platform;
        private readonly Screen picture = new();
        private readonly TouchPad pad;
        private readonly Button menuButton;
        private readonly Border menu;
        private readonly StackPanel menuRows = new() { Spacing = 4 };
        private readonly Border menuPanel;
        private readonly TextBlock menuTitle, notice;
        private readonly List<(Button Row, Action Run)> rows = new();
        private long shown = -1;
        /// <summary>The screen the view is on, while it is.</summary>
        private TopLevel? screen;
        private int menuChoice;

        /// <summary>Quit chosen (menu, Select+Start).</summary>
        public event Action? Quit;

        public RetroPlayerView(RetroPlayer player, string platform, string title)
        {
            this.player = player;
            this.platform = platform;
            Background = Brushes.Black;
            pad = new TouchPad(platform);
            pad.ButtonsChanged += player.SetTouchButtons;
            pad.FreeFinger += Touch;

            menuButton = new Button
            {
                Content = "☰",
                FontSize = 20,
                Width = 56,
                Height = 40,
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            };
            menuButton.Click += (_, _) => OpenMenu();

            // The menu: a panel on the side of the paused game (in the middle, upright), its lines rebuilt each time it opens
            menuTitle = new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
            menuStatus = new TextBlock { FontSize = 13, Foreground = Brushes.White, Opacity = 0.65, Margin = new Thickness(0, 2, 0, 12) };
            menuPanel = new Border
            {
                Width = 340,
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x11, 0x10, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(20, 22),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                    Content = new StackPanel { Children = { menuTitle, menuStatus, menuRows } },
                },
            };
            menu = new Border
            {
                IsVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
                Child = menuPanel,
            };
            menu.PointerPressed += (_, e) =>
            {
                // A tap beside the panel: back to the game
                if (e.Source == menu)
                    CloseMenu();
            };
            // What a line of the menu did (a state saved, a screenshot taken), a moment at the top
            notice = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
            noticeBox = new Border
            {
                IsVisible = false, CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 8), Margin = new Thickness(0, 16, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x4F, 0x46, 0xAF)), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Child = notice,
            };
            player.Done += text => Dispatcher.UIThread.Post(() => Notice(text));

            Children.Add(picture);
            Children.Add(pad);
            Children.Add(menuButton);
            Children.Add(menu);
            Children.Add(noticeBox);

            AttachedToVisualTree += (_, _) =>
            {
                screen = TopLevel.GetTopLevel(this);
                if (screen == null)
                    return;
                screen.BackRequested += Back;
                screen.RequestAnimationFrame(NextFrame);
            };
            // The screen kept from before: once detached, the view has none any more, and its Back stayed (the next
            // game's Back went to this one's menu)
            DetachedFromVisualTree += (_, _) =>
            {
                if (screen != null)
                    screen.BackRequested -= Back;
                screen = null;
            };
        }

        private readonly Border noticeBox;
        private Avalonia.Threading.DispatcherTimer? noticeTimer;

        private void Notice(string text)
        {
            notice.Text = text;
            noticeBox.IsVisible = true;
            noticeTimer?.Stop();
            noticeTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            noticeTimer.Tick += (_, _) =>
            {
                noticeTimer!.Stop();
                noticeBox.IsVisible = false;
            };
            noticeTimer.Start();
        }

        /// <summary>A line of the menu: its icon, its text, a picture (a state's) under it.</summary>
        private void Row(string icon, string text, Action run, Bitmap? picture = null, string? hint = null)
        {
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = text, FontSize = 15.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White } } };
            if (hint != null)
                words.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = Brushes.White, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            line.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(icon), Fill = Brushes.White, Width = 20, Height = 20, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(words, 1);
            line.Children.Add(words);
            if (picture != null)
            {
                var image = new Border { Width = 72, Height = 48, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new Image { Source = picture, Stretch = Stretch.UniformToFill } };
                Grid.SetColumn(image, 2);
                line.Children.Add(image);
            }
            var button = new Button
            {
                Content = line, MinHeight = 48, Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => run();
            rows.Add((button, run));
            menuRows.Children.Add(button);
        }

        private const string IconPlay = "M8 5v14l11-7z", IconSave = "M5 3h11l3 3v15H5zm3 0v5h7V3zm0 11v7h8v-7z",
            IconLoad = "M12 4a8 8 0 1 1-7.4 5h2.2A6 6 0 1 0 12 6V9L7.5 5 12 1z", IconCamera = "M9 4h6l2 3h3v12H4V7h3zm3 5a4 4 0 1 0 0 8 4 4 0 0 0 0-8z",
            IconDisc = "M12 3a9 9 0 1 1 0 18 9 9 0 0 1 0-18zm0 6.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z", IconQuit = "M14 4h6v16h-6v-2h4V6h-4zM9 7l1.4 1.4L8.8 10H16v2H8.8l1.6 1.6L9 15l-4-4z";

        /// <summary>The menu's lines, as the game is now: its states with their pictures, its discs.</summary>
        private void BuildMenu()
        {
            rows.Clear();
            menuRows.Children.Clear();
            Row(IconPlay, Loc.T("Resume"), CloseMenu);
            Row(IconSave, Loc.T("Save the state"), () =>
            {
                player.SaveSlot();
                CloseMenu();
            });
            foreach (var (path, saved, auto) in player.States())
            {
                Bitmap? picture = null;
                try { picture = File.Exists(path + ".png") ? new Bitmap(path + ".png") : null; }
                catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                Row(IconLoad, Loc.T("Load a state"), () =>
                {
                    player.LoadState(path);
                    CloseMenu();
                }, picture, (auto ? Loc.T("When it was quit") : Loc.T("Saved in the menu")) + " · " + Converter.RelativeDate.Format(saved.ToUniversalTime()));
            }
            Row(IconCamera, Loc.T("Take a screenshot"), () =>
            {
                player.Capture();
                CloseMenu();
            });
            if (player.Discs > 1)
                Row(IconDisc, Loc.F("Next disc (now {0} of {1})", player.CurrentDisc + 1, player.Discs), NextDisc);
            Row(IconQuit, Loc.T("Quit the game"), () => Quit?.Invoke(), hint: Loc.T("Select + Start"));
        }

        /// <summary>The touch pad (and the menu button) shows while no controller is there.</summary>
        public void ShowTouchPad(bool show)
        {
            pad.ShowButtons = show;
            pad.InvalidateVisual();
            // With a controller: Back or Select+Start, nothing over the game
            menuButton.IsVisible = show;
            foreach (var (key, value) in TouchOptions(platform, show))
                player.SetOption(key, value);
            InvalidateArrange();
        }

        /// <summary>
        /// What the cores change with the touch pad: the Wiimote aims where a finger is on the picture (else with the
        /// right stick, the core's own settings); the DS's screens one above the other between the controls (else side
        /// by side, larger).
        /// </summary>
        private static (string Key, string Value)[] TouchOptions(string platform, bool touch) => platform switch
        {
            // The pointer's field and height measured on Wii Play: the Wiimote's cursor under the finger
            "wii" => new[] { ("dolphin_ir_mode", touch ? "2" : "1"), ("dolphin_ir_yaw", touch ? "14" : "25"), ("dolphin_ir_pitch", touch ? "16" : "25"), ("dolphin_ir_offset", touch ? "24" : "0") },
            "nds" => new[] { ("melonds_screen_layout1", touch ? "top-bottom" : "left-right") },
            _ => Array.Empty<(string, string)>(),
        };

        private readonly TextBlock menuStatus;

        public bool IsMenuOpen => menu.IsVisible;

        private void Back(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (menu.IsVisible) CloseMenu(); else OpenMenu();
        }

        public void OpenMenu()
        {
            string battery = Helper.Platform.MobilePlatform.BatteryText;
            menuStatus.Text = DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture) + (battery.Length > 0 ? "  ·  " + battery : "");
            BuildMenu();
            // Landscape: on the right of the game, on the whole height; upright: a card in the middle
            bool landscape = Bounds.Width > Bounds.Height;
            menuPanel.HorizontalAlignment = landscape ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            menuPanel.VerticalAlignment = landscape ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            menuPanel.CornerRadius = landscape ? new CornerRadius(0) : new CornerRadius(24);
            menuPanel.BorderThickness = landscape ? new Thickness(1, 0, 0, 0) : new Thickness(1);
            menuPanel.MaxHeight = landscape ? double.PositiveInfinity : Bounds.Height - 80;
            menu.IsVisible = true;
            menuChoice = 0;
            ShowChoice();
            player.Pause(true);
        }

        public void CloseMenu()
        {
            menu.IsVisible = false;
            player.Pause(false);
        }

        /// <summary>The menu with a controller: up and down choose, the bottom button (or Start) confirms.</summary>
        public void MenuButtonPressed(int button)
        {
            if (button is RetroPlayer.Up or RetroPlayer.Down)
            {
                menuChoice = Math.Clamp(menuChoice + (button == RetroPlayer.Up ? -1 : 1), 0, rows.Count - 1);
                ShowChoice();
            }
            else if (button is RetroPlayer.B or RetroPlayer.Start)
                rows[Math.Clamp(menuChoice, 0, rows.Count - 1)].Run();
            else if (button == RetroPlayer.A)
                CloseMenu();
        }

        /// <summary>The line a controller is on: the client's accent behind it.</summary>
        private void ShowChoice()
        {
            for (int i = 0; i < rows.Count; i++)
                rows[i].Row.Background = i == menuChoice ? new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xAF)) : Brushes.Transparent;
            if (menuChoice < rows.Count)
                rows[menuChoice].Row.BringIntoView();
        }

        /// <summary>The next disc goes in, and the game goes on.</summary>
        private void NextDisc()
        {
            player.NextDisc();
            CloseMenu();
        }

        /// <summary>A finger on the game's picture: the console's touch screen (the DS's bottom screen), in libretro's coordinates.</summary>
        private void Touch(Point? finger)
        {
            Rect area = picture.Bounds;
            if (finger is not Point p || area.Width <= 0 || !area.Contains(p))
            {
                player.PointerDown = 0;
                return;
            }
            player.PointerX = (int)Math.Clamp((p.X - area.X) / area.Width * 65534 - 32767, -32767, 32767);
            player.PointerY = (int)Math.Clamp((p.Y - area.Y) / area.Height * 65534 - 32767, -32767, 32767);
            player.PointerDown = 1;
        }

        /// <summary>
        /// Each refresh of the screen (its vsync): a timer of its own came at other moments, and a picture now and then
        /// stayed twice as long.
        /// </summary>
        private void NextFrame(TimeSpan time)
        {
            if (screen == null)
                return;
            Present();
            screen.RequestAnimationFrame(NextFrame);
        }

        /// <summary>The newest picture of the core, copied once to the screen.</summary>
        private void Present()
        {
            try { PresentCore(); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }
        private void PresentCore()
        {
            FrameBuffer frame = player.Frame;
            Bitmap next;
            lock (frame)
            {
                if (frame.Version == shown || frame.Width == 0)
                    return;
                shown = frame.Version;
                // A new picture each time: a WriteableBitmap changed in place is not shown again on Android
                fixed (int* pixels = frame.Pixels)
                    next = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Opaque, (IntPtr)pixels, new PixelSize(frame.Width, frame.Height), new Vector(96, 96), frame.Width * 4);
            }
            Bitmap? previous = picture.Bitmap;
            picture.Bitmap = next;
            previous?.Dispose();
            picture.InvalidateVisual();
            if (Math.Abs(lastRatio - player.AspectRatio) > 0.001)
                InvalidateArrange();
        }
        private float lastRatio;

        protected override Size ArrangeOverride(Size finalSize)
        {
            lastRatio = player.AspectRatio;
            bool portrait = finalSize.Height > finalSize.Width;
            // With the touch pad, the picture between its controls (smaller on a phone, nothing under the fingers);
            // upright, the picture at the top on the whole width, the controls under it
            double room = pad.ShowButtons && !portrait ? TouchPad.SideRoom(finalSize) : 0;
            double width = Math.Max(0, finalSize.Width - 2 * room), height = width / lastRatio;
            double maxHeight = pad.ShowButtons && portrait ? finalSize.Height * 0.55 : finalSize.Height;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * lastRatio;
            }
            double top = pad.ShowButtons && portrait ? 52 : (finalSize.Height - height) / 2;
            picture.Arrange(new Rect((finalSize.Width - width) / 2, top, width, height));
            pad.Arrange(new Rect(finalSize));
            // Beside the picture with the touch pad (on its inner edge, above the cross), else at the top
            menuButton.Arrange(room > 0 ? new Rect(room * 0.86 - 40, 0, 80, 60) : new Rect(finalSize));
            menu.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
