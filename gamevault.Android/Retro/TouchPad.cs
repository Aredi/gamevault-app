using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace gamevault.Android.Retro
{
    /// <summary>
    /// The touch controls over the game: a cross on the left, the console's buttons on the right, its shoulder buttons in
    /// the top corners, Select and Start at the bottom. Several fingers at once; a finger can slide from a button to
    /// another. Hidden while a controller is there; a finger on the game itself is the DS's touch screen.
    /// </summary>
    internal sealed class TouchPad : Control, ICustomHitTest
    {
        private sealed record Button(string Label, int Id, double X, double Y, double Radius);

        private readonly string platform;
        private readonly Dictionary<long, Point> fingers = new();
        private List<Button> buttons = new();
        private Point cross;
        private double crossRadius;
        private int held;

        /// <summary>The RetroPad buttons held by the fingers.</summary>
        public event Action<int>? ButtonsChanged;
        /// <summary>A finger on no button (the game's screen), or none.</summary>
        public event Action<Point?>? FreeFinger;

        public bool ShowButtons { get; set; } = true;

        /// <summary>The room the controls take on each side: the game's picture stays between them.</summary>
        public static double SideRoom(Size size) => Math.Min(size.Height, size.Width * 0.6) * 0.5;

        public TouchPad(string platform)
        {
            this.platform = platform;
            ClipToBounds = false;
        }

        public bool HitTest(Point point) => true;

        /// <summary>The console's face buttons, as on its controller: (label, RetroPad id, place: 0 right, 1 bottom, 2 left, 3 top).</summary>
        private (string Label, int Id, int Place)[] FaceButtons => platform switch
        {
            "nes" => new[] { ("A", RetroPlayer.A, 0), ("B", RetroPlayer.B, 2) },
            "gba" => new[] { ("A", RetroPlayer.A, 0), ("B", RetroPlayer.B, 2) },
            "psx" or "ps2" => new[] { ("○", RetroPlayer.A, 0), ("✕", RetroPlayer.B, 1), ("□", RetroPlayer.Y, 2), ("△", RetroPlayer.X, 3) },
            // FinalBurn Neo's Neo Geo: A B C D on B A Y X
            "neogeoaes" => new[] { ("B", RetroPlayer.A, 0), ("A", RetroPlayer.B, 1), ("C", RetroPlayer.Y, 2), ("D", RetroPlayer.X, 3) },
            // Dolphin's upright Wiimote: A B 1 2 on A B X Y
            "wii" => new[] { ("A", RetroPlayer.A, 0), ("B", RetroPlayer.B, 1), ("1", RetroPlayer.X, 2), ("2", RetroPlayer.Y, 3) },
            _ => new[] { ("A", RetroPlayer.A, 0), ("B", RetroPlayer.B, 1), ("Y", RetroPlayer.Y, 2), ("X", RetroPlayer.X, 3) },
        };

        private (string Label, int Id)[] Shoulders => platform switch
        {
            "nes" => Array.Empty<(string, int)>(),
            "psx" or "ps2" => new[] { ("L1", RetroPlayer.L), ("R1", RetroPlayer.R), ("L2", RetroPlayer.L2), ("R2", RetroPlayer.R2) },
            // Dolphin: the triggers are L2 and R2, Z is R (right, under R)
            "ngc" => new[] { ("L", RetroPlayer.L2), ("R", RetroPlayer.R2), ("", -1), ("Z", RetroPlayer.R) },
            // The Wiimote's Home is R3
            "wii" => new[] { ("", -1), ("HOME", RetroPlayer.R3) },
            _ => new[] { ("L", RetroPlayer.L), ("R", RetroPlayer.R) },
        };

        /// <summary>The buttons at the bottom, as the console names them; the GameCube has no Select (its menu quits).</summary>
        private (string Label, int Id)[] Middle => platform switch
        {
            "ngc" => new[] { ("START", RetroPlayer.Start) },
            "wii" => new[] { ("−", RetroPlayer.Select), ("+", RetroPlayer.Start) },
            // FinalBurn Neo: Select puts a coin in
            "neogeoaes" => new[] { ("COIN", RetroPlayer.Select), ("START", RetroPlayer.Start) },
            _ => new[] { ("SELECT", RetroPlayer.Select), ("START", RetroPlayer.Start) },
        };

        protected override Size ArrangeOverride(Size finalSize)
        {
            Place(finalSize);
            return base.ArrangeOverride(finalSize);
        }

        private void Place(Size size)
        {
            double w = size.Width, h = size.Height;
            // Upright: the controls share the bottom under the picture, sized by the width
            double unit = h > w ? Math.Min(w * 0.75, h * 0.42) : Math.Min(h, w * 0.6);
            var list = new List<Button>();
            cross = new Point(unit * 0.3, h - unit * 0.34);
            crossRadius = unit * 0.2;
            var face = new Point(w - unit * 0.3, h - unit * 0.34);
            double offset = unit * 0.12, radius = unit * 0.075;
            foreach (var (label, id, place) in FaceButtons)
            {
                var (dx, dy) = place switch { 0 => (1.0, 0.0), 1 => (0.0, 1.0), 2 => (-1.0, 0.0), _ => (0.0, -1.0) };
                // Two buttons (NES, GBA): side by side, a little apart
                if (FaceButtons.Length == 2)
                    (dx, dy) = place == 0 ? (0.75, -0.45) : (-0.75, 0.45);
                list.Add(new Button(label, id, face.X + dx * offset, face.Y + dy * offset, radius));
            }
            var shoulders = Shoulders;
            for (int i = 0; i < shoulders.Length; i++)
            {
                if (shoulders[i].Id < 0)
                    continue;
                bool right = i % 2 == 1;
                // Upright: above the cross and the buttons, under the picture
                double y = h > w ? h - unit * (0.72 + 0.15 * (i / 2)) : unit * (0.1 + 0.15 * (i / 2));
                list.Add(new Button(shoulders[i].Label, shoulders[i].Id, right ? w - unit * 0.16 : unit * 0.16, y, unit * 0.065));
            }
            // Upright: under the picture, in the middle. Lying: on the picture's edges, under the cross and the buttons
            double middle = h > w ? unit * 0.13 : w / 2 - unit * 0.435;
            foreach (var (label, id) in Middle)
            {
                bool right = id == RetroPlayer.Start;
                list.Add(new Button(label, id, right ? w / 2 + middle : w / 2 - middle, h - unit * 0.07, unit * 0.05));
            }
            buttons = list;
        }

        public override void Render(DrawingContext context)
        {
            if (!ShowButtons)
                return;
            // Dark with a light edge: seen on a light picture as on a dark one
            var fill = new SolidColorBrush(Color.FromArgb(110, 20, 20, 24));
            var pressed = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
            var line = new Pen(new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), 2);
            var text = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));

            // The cross: four arms
            double arm = crossRadius * 0.38;
            foreach (var (id, dx, dy) in new[] { (RetroPlayer.Up, 0, -1), (RetroPlayer.Down, 0, 1), (RetroPlayer.Left, -1, 0), (RetroPlayer.Right, 1, 0) })
            {
                var center = new Point(cross.X + dx * crossRadius * 0.6, cross.Y + dy * crossRadius * 0.6);
                var rect = new Rect(center.X - arm, center.Y - arm, arm * 2, arm * 2);
                context.DrawRectangle((held & (1 << id)) != 0 ? pressed : fill, line, rect, 6, 6);
            }
            foreach (var button in buttons)
            {
                var brush = (held & (1 << button.Id)) != 0 ? pressed : fill;
                context.DrawEllipse(brush, line, new Point(button.X, button.Y), button.Radius, button.Radius);
                var label = new FormattedText(button.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default,
                    button.Label.Length > 2 ? button.Radius * 0.45 : button.Radius * 0.8, text);
                context.DrawText(label, new Point(button.X - label.Width / 2, button.Y - label.Height / 2));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            fingers[e.Pointer.Id] = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            Update();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!fingers.ContainsKey(e.Pointer.Id))
                return;
            fingers[e.Pointer.Id] = e.GetPosition(this);
            Update();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            fingers.Remove(e.Pointer.Id);
            Update();
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            fingers.Remove(e.Pointer.Id);
            Update();
        }

        private void Update()
        {
            int mask = 0;
            Point? free = null;
            foreach (Point finger in fingers.Values)
            {
                int under = ShowButtons ? At(finger) : 0;
                if (under == 0)
                    free ??= finger;
                mask |= under;
            }
            FreeFinger?.Invoke(free);
            if (mask == held)
                return;
            held = mask;
            ButtonsChanged?.Invoke(mask);
            InvalidateVisual();
        }

        /// <summary>The buttons under a finger: the cross gives 8 directions, a button is a little larger than drawn.</summary>
        private int At(Point finger)
        {
            Vector fromCross = finger - cross;
            double distance = Math.Sqrt(fromCross.X * fromCross.X + fromCross.Y * fromCross.Y);
            if (distance < crossRadius * 1.4)
            {
                if (distance < crossRadius * 0.2)
                    return 0;
                double angle = Math.Atan2(fromCross.Y, fromCross.X) * 180 / Math.PI;// 0 right, 90 down
                int mask = 0;
                if (angle is > -67.5 and < 67.5) mask |= 1 << RetroPlayer.Right;
                if (angle is > 22.5 and < 157.5) mask |= 1 << RetroPlayer.Down;
                if (angle is > 112.5 or < -112.5) mask |= 1 << RetroPlayer.Left;
                if (angle is > -157.5 and < -22.5) mask |= 1 << RetroPlayer.Up;
                return mask;
            }
            Button? nearest = buttons
                .Select(b => (Button: b, Distance: Math.Sqrt((finger.X - b.X) * (finger.X - b.X) + (finger.Y - b.Y) * (finger.Y - b.Y))))
                .Where(b => b.Distance < b.Button.Radius * 1.35)
                .OrderBy(b => b.Distance)
                .Select(b => b.Button)
                .FirstOrDefault();
            return nearest != null ? 1 << nearest.Id : 0;
        }
    }
}
