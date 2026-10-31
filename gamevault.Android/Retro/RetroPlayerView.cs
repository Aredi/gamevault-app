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
        private readonly Button resumeButton, quitButton;
        private long shown = -1;
        private readonly DispatcherTimer refresh;
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

            resumeButton = MenuButton(Loc.T("Resume"));
            resumeButton.Click += (_, _) => CloseMenu();
            quitButton = MenuButton(Loc.T("Quit the game"));
            quitButton.Click += (_, _) => Quit?.Invoke();
            menu = new Border
            {
                IsVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) },
                        resumeButton,
                        quitButton,
                    },
                },
            };

            Children.Add(picture);
            Children.Add(pad);
            Children.Add(menuButton);
            Children.Add(menu);

            refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, (_, _) => Present());
            AttachedToVisualTree += (_, _) =>
            {
                refresh.Start();
                if (TopLevel.GetTopLevel(this) is TopLevel top)
                    top.BackRequested += Back;
            };
            DetachedFromVisualTree += (_, _) =>
            {
                refresh.Stop();
                if (TopLevel.GetTopLevel(this) is TopLevel top)
                    top.BackRequested -= Back;
            };
        }

        private static Button MenuButton(string text) => new()
        {
            Content = text,
            MinWidth = 260,
            Height = 48,
            FontSize = 18,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

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

        public bool IsMenuOpen => menu.IsVisible;

        private void Back(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (menu.IsVisible) CloseMenu(); else OpenMenu();
        }

        public void OpenMenu()
        {
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
                menuChoice = button == RetroPlayer.Up ? 0 : 1;
                ShowChoice();
            }
            else if (button is RetroPlayer.B or RetroPlayer.Start)
            {
                if (menuChoice == 0) CloseMenu(); else Quit?.Invoke();
            }
            else if (button == RetroPlayer.A)
                CloseMenu();
        }

        private void ShowChoice()
        {
            resumeButton.BorderBrush = menuChoice == 0 ? Brushes.White : Brushes.Transparent;
            quitButton.BorderBrush = menuChoice == 1 ? Brushes.White : Brushes.Transparent;
            resumeButton.BorderThickness = quitButton.BorderThickness = new Thickness(2);
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
