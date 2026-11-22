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

namespace gamevault.UserControls.Mobile
{
    /// <summary>
    /// A game's cover in the grid, kept and given the next game as the list scrolls (its game is its DataContext): built
    /// once, not at each row that comes into sight.
    /// </summary>
    internal sealed class MobileCoverCard : Avalonia.Controls.Border
    {
        private readonly Avalonia.Controls.TextBlock tag = new();

        public MobileCoverCard(double width, double ratio, System.Action<RetroGame> open, System.Action<RetroGame>? hold)
        {
            var image = new Avalonia.Controls.Image { Stretch = Avalonia.Media.Stretch.UniformToFill };
            image.Bind(Avalonia.Controls.Image.SourceProperty, new Avalonia.Data.Binding(nameof(RetroGame.CoverImage)));
            var name = new Avalonia.Controls.TextBlock
            {
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 13, Margin = new Avalonia.Thickness(10),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, MaxLines = 3, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            name.Bind(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(RetroGame.Name)));
            name.Bind(IsVisibleProperty, new Avalonia.Data.Binding(nameof(RetroGame.CoverImage)) { Converter = Avalonia.Data.Converters.ObjectConverters.IsNull });
            var download = new Avalonia.Controls.Border
            {
                Width = 26, Height = 26, CornerRadius = new Avalonia.CornerRadius(13), Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#80000000")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Avalonia.Thickness(7),
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = Avalonia.Media.Geometry.Parse("M11 4h2v8.2l3.3-3.3 1.4 1.4L12 16l-5.7-5.7 1.4-1.4 3.3 3.3zM5 18h14v2H5z"),
                    Fill = Avalonia.Media.Brushes.White, Width = 13, Height = 13, Stretch = Avalonia.Media.Stretch.Uniform,
                },
            };
            download.Bind(IsVisibleProperty, new Avalonia.Data.Binding(nameof(RetroGame.Installed)) { Converter = Avalonia.Data.Converters.BoolConverters.Not });
            var cover = new Avalonia.Controls.Border
            {
                Classes = { "mCover" }, Width = width, Height = width * ratio,
                Child = new Avalonia.Controls.Panel { Children = { name, image, new Avalonia.Controls.Border { Classes = { "mTag" }, Child = tag }, download } },
            };
            var title = new Avalonia.Controls.TextBlock { FontSize = 12.5, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            title.Bind(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(RetroGame.Name)));
            var card = new Avalonia.Controls.StackPanel { Width = width, Spacing = 6, Children = { cover, title } };
            Child = MobileCards.Pressable(card, () => { if (DataContext is RetroGame game) open(game); },
                hold == null ? null : () => { if (DataContext is RetroGame game) hold(game); });
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            tag.Text = DataContext is RetroGame game ? MobileCards.ShortName(game.Platform) : "";
        }
    }
}
