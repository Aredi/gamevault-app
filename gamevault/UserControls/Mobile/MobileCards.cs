using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using gamevault.Helper;
using System;

namespace gamevault.UserControls.Mobile
{
    /// <summary>The phone's cards: a game's cover (its console's tag, its name until the cover is there), a friend.</summary>
    internal static class MobileCards
    {
        /// <summary>A retro game's cover, the whole card a button: a tap opens it, a long press asks what to do.</summary>
        /// <param name="ratio">The cover's height for its width: its console's boxes' when none (a grid gives all the same).</param>
        public static Control Cover(RetroGame game, double width, Action<RetroGame> open, Action<RetroGame>? hold = null, bool title = true, double? ratio = null)
        {
            double height = width * (ratio ?? game.Platform.System.CoverRatio);
            var image = new Image { Stretch = Stretch.UniformToFill };
            image.Bind(Image.SourceProperty, new Binding(nameof(RetroGame.CoverImage)) { Source = game });
            var name = new TextBlock
            {
                Text = game.Name, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold, FontSize = 13, Margin = new Thickness(10),
                VerticalAlignment = VerticalAlignment.Bottom, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            name.Bind(Visual.IsVisibleProperty, new Binding(nameof(RetroGame.CoverImage)) { Source = game, Converter = Avalonia.Data.Converters.ObjectConverters.IsNull });
            var tag = new Border { Classes = { "mTag" }, Child = new TextBlock { Text = ShortName(game.Platform) } };
            var download = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.Parse("#80000000")),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7),
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M11 4h2v8.2l3.3-3.3 1.4 1.4L12 16l-5.7-5.7 1.4-1.4 3.3 3.3zM5 18h14v2H5z"),
                    Fill = Brushes.White, Width = 13, Height = 13, Stretch = Stretch.Uniform,
                },
            };
            download.Bind(Visual.IsVisibleProperty, new Binding(nameof(RetroGame.Installed)) { Source = game, Converter = Avalonia.Data.Converters.BoolConverters.Not });
            var cover = new Border
            {
                Classes = { "mCover" }, Width = width, Height = height,
                Child = new Panel { Children = { name, image, tag, download } },
            };
            var card = new StackPanel { Width = width, Spacing = 6, Children = { cover } };
            if (title)
                card.Children.Add(new TextBlock { Text = game.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            return Pressable(card, () => open(game), hold == null ? null : () => hold(game));
        }

        /// <summary>"GBA", "PS1": a console's name, short enough for its tag.</summary>
        public static string ShortName(RetroPlatform platform) => platform.Slug switch
        {
            "psx" => "PS1", "ps2" => "PS2", "gba" => "GBA", "nds" => "DS", "3ds" => "3DS", "snes" => "SNES", "nes" => "NES",
            "ngc" => "GC", "wii" => "WII", "neogeoaes" => "NEO GEO", _ => platform.Name.ToUpperInvariant(),
        };

        /// <summary>
        /// A tap runs it; held half a second, the other action (a game's sheet). A finger that moves (the list scrolled) runs
        /// nothing.
        /// </summary>
        public static Control Pressable(Control content, Action tap, Action? hold)
        {
            var host = new Border { Background = Brushes.Transparent, Child = content, Cursor = new Cursor(StandardCursorType.Hand) };
            Point start = default;
            bool pressed = false, held = false;
            Avalonia.Threading.DispatcherTimer? timer = null;
            host.PointerPressed += (_, e) =>
            {
                pressed = true;
                held = false;
                start = e.GetPosition(host);
                if (hold == null)
                    return;
                timer?.Stop();
                timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (_, _) =>
                {
                    timer!.Stop();
                    if (!pressed)
                        return;
                    held = true;
                    pressed = false;
                    try { Helper.Platform.MobilePlatform.Current?.Vibrate(firm: true); }
                    catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                    hold();
                };
                timer.Start();
            };
            host.PointerMoved += (_, e) =>
            {
                if (pressed && Distance(e.GetPosition(host), start) > 12)
                {
                    pressed = false;
                    timer?.Stop();
                }
            };
            host.PointerReleased += (_, e) =>
            {
                timer?.Stop();
                if (pressed && !held)
                    tap();
                pressed = false;
            };
            host.PointerCaptureLost += (_, _) =>
            {
                timer?.Stop();
                pressed = false;
            };
            return host;
        }

        private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
