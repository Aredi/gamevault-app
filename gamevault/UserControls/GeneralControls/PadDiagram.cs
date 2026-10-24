using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace gamevault.UserControls
{
    /// <summary>
    /// The plan of a console's gamepad (Régler mes touches): simple shapes on a 420×280 board, scaled to the control, each
    /// button named by the RetroPad input it is (<see cref="GameVault.Core.Retro.RetroKeyboard.Of"/>), the highlighted one lit.
    /// </summary>
    public class PadDiagram : Control
    {

        public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<PadDiagram, string?>(nameof(Kind));
        public static readonly StyledProperty<string?> HighlightProperty = AvaloniaProperty.Register<PadDiagram, string?>(nameof(Highlight));

        /// <summary>"snes", "psx", "wiiNunchuk"...: see <see cref="Plans"/>.</summary>
        public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
        /// <summary>The RetroPad input of the button lit ("b", "lstick"...).</summary>
        public string? Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

        static PadDiagram() => AffectsRender<PadDiagram>(KindProperty, HighlightProperty);

        /// <summary>A button of the plan clicked: its RetroPad input ("b", "lstick"...).</summary>
        public event Action<string>? ButtonClicked;

        /// <summary>The board's scale and place in the control, as drawn.</summary>
        private double Scale => Math.Min(Bounds.Width / 420, Bounds.Height / 280);
        private Point ToBoard(Point point) => new((point.X - (Bounds.Width - 420 * Scale) / 2) / Scale, (point.Y - (Bounds.Height - 280 * Scale) / 2) / Scale);

        /// <summary>The button under a point of the control (the topmost, a label on it counts as it); null for none.</summary>
        internal string? ButtonAt(Point point)
        {
            if (Kind == null || !Plans.TryGetValue(Kind, out Shape[]? shapes) || Scale <= 0)
                return null;
            Point p = ToBoard(point);
            static bool Inside(Shape s, Point p) => s.Form switch
            {
                Form.Rect => p.X >= s.X && p.X <= s.X + s.W && p.Y >= s.Y && p.Y <= s.Y + s.H,
                Form.Circle => Math.Pow(p.X - s.X, 2) + Math.Pow(p.Y - s.Y, 2) <= s.R * s.R,
                Form.Ellipse => Math.Pow((p.X - s.X) / s.W, 2) + Math.Pow((p.Y - s.Y) / s.H, 2) <= 1,
                Form.Poly => p.X >= s.Points!.Min(q => q.X) && p.X <= s.Points.Max(q => q.X) && p.Y >= s.Points.Min(q => q.Y) && p.Y <= s.Points.Max(q => q.Y),
                _ => false,
            };
            return shapes.Reverse().FirstOrDefault(s => s.Id is not (null or "screen" or "center") && Inside(s, p))?.Id;
        }

        protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Cursor = ButtonAt(e.GetPosition(this)) != null ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : Avalonia.Input.Cursor.Default;
        }

        protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (ButtonAt(e.GetPosition(this)) is string id)
            {
                e.Handled = true;
                ButtonClicked?.Invoke(id);
            }
        }

        private enum Form { Rect, Circle, Ellipse, Poly, Text }
        /// <param name="Id">The button's RetroPad input; null for the gamepad's body, "screen" for a screen.</param>
        private sealed record Shape(Form Form, double X, double Y, double W = 0, double H = 0, double R = 0, string? Id = null, string? Text = null, double Size = 11, bool Muted = false, Point[]? Points = null);

        private static Shape Body(double x, double y, double w, double h, double r) => new(Form.Rect, x, y, w, h, r);
        private static Shape Screen(double x, double y, double w, double h) => new(Form.Rect, x, y, w, h, 4, "screen");
        private static Shape Rect(string id, double x, double y, double w, double h, double r) => new(Form.Rect, x, y, w, h, r, id);
        private static Shape Circle(string id, double cx, double cy, double r) => new(Form.Circle, cx, cy, R: r, Id: id);
        private static Shape Text(double x, double y, string text, double size = 11, bool muted = false) => new(Form.Text, x, y, Text: text, Size: size, Muted: muted);
        /// <summary>A round button and its name.</summary>
        private static IEnumerable<Shape> Round(string id, double cx, double cy, double r, string text) => new[] { Circle(id, cx, cy, r), Text(cx, cy + r * 0.32, text, Math.Clamp(r * 0.8, 8, 13)) };
        /// <summary>A long button (shoulder, Select) and its name.</summary>
        private static IEnumerable<Shape> Bar(string id, double x, double y, double w, double h, string text) => new[] { Rect(id, x, y, w, h, h / 2), Text(x + w / 2, y + h * 0.72, text, Math.Max(8, h * 0.6)) };
        private static IEnumerable<Shape> Badge(string id, double x, double y, string text) => new[] { Rect(id, x, y, 104, 24, 12), Text(x + 52, y + 16, text, 10) };

        /// <summary>The cross, centered on (cx, cy); turned a quarter for the sideways Wiimote.</summary>
        private static IEnumerable<Shape> Cross(double cx, double cy, double scale = 1, bool sideways = false)
        {
            (string Id, double X, double Y, double W, double H)[] arms =
                { ("up", -15, -42, 30, 28), ("down", -15, 14, 30, 28), ("left", -42, -15, 28, 30), ("right", 14, -15, 28, 30), ("center", -15, -15, 30, 30) };
            foreach (var arm in arms)
            {
                // A quarter turn back: (x, y) → (y, -x), the box's width and height swapped
                var (x, y, w, h) = sideways ? (arm.Y, -arm.X - arm.W, arm.H, arm.W) : (arm.X, arm.Y, arm.W, arm.H);
                yield return Rect(arm.Id, cx + x * scale, cy + y * scale, w * scale, h * scale, arm.Id == "center" ? 0 : 3);
            }
        }

        private static readonly Dictionary<string, Shape[]> Plans = new()
        {
            ["nes"] = new[] { Body(30, 70, 360, 150, 12) }.Concat(Cross(100, 145))
                .Concat(Bar("select", 165, 148, 34, 12, "")).Append(Text(182, 178, "SELECT", 9, true)).Concat(Bar("start", 215, 148, 34, 12, "")).Append(Text(232, 178, "START", 9, true))
                .Concat(Round("b", 295, 150, 18, "B")).Concat(Round("a", 345, 150, 18, "A")).ToArray(),
            ["snes"] = new[] { Body(30, 70, 360, 150, 75) }.Concat(Bar("l", 50, 40, 90, 22, "L")).Concat(Bar("r", 280, 40, 90, 22, "R")).Concat(Cross(110, 145))
                .Concat(Bar("select", 172, 150, 30, 11, "")).Append(Text(187, 176, "SELECT", 9, true)).Concat(Bar("start", 214, 150, 30, 11, "")).Append(Text(229, 176, "START", 9, true))
                .Concat(Round("x", 310, 113, 16, "X")).Concat(Round("a", 342, 145, 16, "A")).Concat(Round("b", 310, 177, 16, "B")).Concat(Round("y", 278, 145, 16, "Y")).ToArray(),
            ["gba"] = new[] { Body(20, 70, 380, 150, 44) }.Concat(Bar("l", 30, 40, 80, 22, "L")).Concat(Bar("r", 310, 40, 80, 22, "R")).Append(Screen(135, 86, 150, 100)).Concat(Cross(78, 135, 0.8))
                .Concat(Round("a", 352, 124, 15, "A")).Concat(Round("b", 322, 150, 15, "B"))
                .Append(Circle("select", 186, 204, 6)).Append(Text(186, 196, "SELECT", 8, true)).Append(Circle("start", 234, 204, 6)).Append(Text(234, 196, "START", 8, true)).ToArray(),
            ["nds"] = new[] { Body(60, 4, 300, 106, 12), Screen(150, 12, 120, 90), Body(60, 142, 300, 124, 12) }.Concat(Bar("l", 60, 116, 76, 20, "L")).Concat(Bar("r", 284, 116, 76, 20, "R"))
                .Append(Screen(150, 154, 120, 90)).Concat(Cross(103, 195, 0.7))
                .Concat(Round("x", 317, 174, 11, "X")).Concat(Round("a", 339, 196, 11, "A")).Concat(Round("b", 317, 218, 11, "B")).Concat(Round("y", 295, 196, 11, "Y"))
                .Append(Circle("select", 300, 248, 5)).Append(Circle("start", 334, 248, 5)).Append(Text(300, 262, "SELECT", 7, true)).Append(Text(334, 262, "START", 7, true)).ToArray(),
            ["n3ds"] = new[] { Body(50, 4, 320, 106, 12), Screen(130, 11, 160, 92), Body(50, 142, 320, 126, 12) }.Concat(Bar("l", 50, 116, 66, 20, "L")).Concat(Bar("l2", 122, 116, 50, 20, "ZL"))
                .Concat(Bar("r2", 248, 116, 50, 20, "ZR")).Concat(Bar("r", 304, 116, 66, 20, "R")).Append(Screen(148, 150, 124, 88))
                .Concat(Round("lstick", 98, 172, 16, "PAD")).Concat(Cross(98, 230, 0.55)).Append(Circle("rstick", 300, 158, 5)).Append(Text(300, 150, "C", 7, true))
                .Concat(Round("x", 324, 176, 10, "X")).Concat(Round("a", 344, 196, 10, "A")).Concat(Round("b", 324, 216, 10, "B")).Concat(Round("y", 304, 196, 10, "Y"))
                .Append(Circle("select", 180, 252, 5)).Append(Circle("l3", 210, 252, 6)).Append(Circle("start", 240, 252, 5)).ToArray(),
            ["ngc"] = new[] { Body(40, 76, 340, 132, 62) }.Concat(Bar("l2", 58, 24, 96, 22, "L")).Concat(Bar("r2", 266, 24, 96, 22, "R")).Concat(Bar("r", 276, 50, 80, 20, "Z")).Append(new Shape(Form.Ellipse, 110, 232, 50, 34)).Append(new Shape(Form.Ellipse, 310, 232, 50, 34))
                .Append(Circle("lstick", 108, 134, 25)).Concat(Cross(110, 238, 0.5)).Append(Circle("start", 210, 138, 7))
                .Concat(Round("a", 300, 140, 22, "A")).Concat(Round("b", 264, 168, 12, "B")).Concat(Round("x", 338, 114, 12, "X")).Concat(Round("y", 288, 104, 12, "Y"))
                .Concat(Round("rstick", 310, 238, 15, "C")).ToArray(),
            ["psx"] = new[] { new Shape(Form.Ellipse, 92, 200, 50, 60), new Shape(Form.Ellipse, 328, 200, 50, 60), Body(30, 70, 360, 130, 60) }
                .Concat(Bar("l2", 62, 18, 80, 20, "L2")).Concat(Bar("l", 62, 44, 80, 20, "L1")).Concat(Bar("r2", 278, 18, 80, 20, "R2")).Concat(Bar("r", 278, 44, 80, 20, "R1")).Concat(Cross(100, 128, 0.85))
                .Concat(Bar("select", 172, 124, 26, 10, "")).Concat(Bar("start", 222, 124, 26, 10, "")).Append(Text(185, 148, "SELECT", 8, true)).Append(Text(235, 148, "START", 8, true))
                .Concat(Round("x", 320, 100, 15, "△")).Concat(Round("a", 348, 128, 15, "○")).Concat(Round("b", 320, 156, 15, "✕")).Concat(Round("y", 292, 128, 15, "□"))
                .Concat(Round("lstick", 160, 185, 22, "L3")).Concat(Round("rstick", 260, 185, 22, "R3")).ToArray(),
            ["neogeo"] = new[]
                {
                    Body(20, 60, 380, 170, 18),
                    new Shape(Form.Poly, 0, 0, Id: "up", Points: new Point[] { new(95, 78), new(85, 92), new(105, 92) }),
                    new Shape(Form.Poly, 0, 0, Id: "down", Points: new Point[] { new(95, 206), new(85, 192), new(105, 192) }),
                    new Shape(Form.Poly, 0, 0, Id: "left", Points: new Point[] { new(40, 145), new(54, 135), new(54, 155) }),
                    new Shape(Form.Poly, 0, 0, Id: "right", Points: new Point[] { new(150, 145), new(136, 135), new(136, 155) }),
                    Circle("center", 95, 112, 20),
                }
                .Concat(Round("b", 205, 168, 19, "A")).Concat(Round("a", 250, 152, 19, "B")).Concat(Round("y", 295, 142, 19, "C")).Concat(Round("x", 340, 136, 19, "D"))
                .Concat(Bar("select", 290, 76, 40, 14, "")).Append(Text(310, 104, "COIN", 8, true)).Concat(Bar("start", 342, 76, 40, 14, "")).Append(Text(362, 104, "START", 8, true)).ToArray(),
            ["wiiNunchuk"] = new[] { Body(80, 14, 76, 252, 30) }.Concat(Cross(118, 58, 0.7)).Concat(Round("a", 118, 108, 15, "A"))
                .Append(Rect("b", 156, 92, 10, 34, 4)).Append(Text(176, 113, "B", 9, true))
                .Append(Circle("l", 96, 146, 6)).Append(Circle("r3", 118, 146, 7)).Append(Circle("r", 140, 146, 6)).Append(Text(96, 165, "−", 9, true)).Append(Text(140, 165, "+", 9, true))
                .Concat(Round("start", 118, 204, 10, "1")).Concat(Round("select", 118, 232, 10, "2"))
                .Append(Body(302, 30, 90, 236, 45)).Concat(Round("x", 347, 66, 13, "C")).Concat(Bar("y", 321, 92, 52, 24, "Z")).Append(Circle("lstick", 347, 172, 28))
                .Concat(Badge("r2", 190, 96, "Shake Wiimote")).Concat(Badge("l2", 190, 128, "Shake Nunchuk")).Concat(Badge("rstick", 190, 160, "Tilt")).ToArray(),
            ["wiiRemote"] = new[] { Body(172, 14, 76, 252, 30) }.Concat(Cross(210, 58, 0.7)).Concat(Round("a", 210, 108, 15, "A"))
                .Append(Rect("b", 248, 92, 10, 34, 4)).Append(Text(290, 113, "B", 9, true))
                .Append(Circle("select", 188, 146, 6)).Append(Circle("r3", 210, 146, 7)).Append(Circle("start", 232, 146, 6)).Append(Text(188, 165, "−", 9, true)).Append(Text(232, 165, "+", 9, true))
                .Concat(Round("x", 210, 204, 10, "1")).Concat(Round("y", 210, 232, 10, "2")).Concat(Badge("r2", 300, 196, "Shake Wiimote")).Concat(Badge("lstick", 300, 228, "Tilt")).ToArray(),
            ["wiiSideways"] = new[] { Body(40, 96, 340, 80, 32) }.Concat(Cross(96, 136, 0.7, sideways: true)).Concat(Round("x", 160, 136, 13, "A"))
                .Append(Rect("y", 150, 176, 30, 10, 4)).Append(Text(165, 202, "B", 9, true))
                .Append(Circle("select", 214, 118, 5)).Append(Circle("r3", 214, 136, 6)).Append(Circle("start", 214, 154, 5))
                .Concat(Round("b", 300, 136, 13, "1")).Concat(Round("a", 340, 136, 13, "2")).Concat(Badge("r2", 96, 38, "Shake Wiimote")).Concat(Badge("lstick", 220, 38, "Tilt")).ToArray(),
            ["wiiClassic"] = new[] { Body(30, 70, 360, 150, 75) }.Concat(Bar("l2", 40, 40, 86, 22, "L")).Concat(Bar("l", 132, 40, 52, 22, "ZL")).Concat(Bar("r", 236, 40, 52, 22, "ZR")).Concat(Bar("r2", 294, 40, 86, 22, "R")).Concat(Cross(98, 122, 0.75))
                .Append(Circle("select", 186, 118, 6)).Append(Circle("r3", 210, 118, 7)).Append(Circle("start", 234, 118, 6))
                .Concat(Round("x", 322, 96, 13, "x")).Concat(Round("a", 348, 122, 13, "a")).Concat(Round("b", 322, 148, 13, "b")).Concat(Round("y", 296, 122, 13, "y"))
                .Append(Circle("lstick", 164, 178, 20)).Append(Circle("rstick", 256, 178, 20)).ToArray(),
        };

        /// <summary>The plan of a platform: the Wii's by its controller (RetroArch's id), the GameCube controller's for the 6th.</summary>
        public static string? KindOf(string slug, int? device) => slug switch
        {
            "nes" or "snes" or "gba" or "nds" or "ngc" or "psx" => slug,
            "neogeoaes" => "neogeo",
            "3ds" => "n3ds",
            "ps2" => "psx",
            "wii" => device switch { 1 => "wiiRemote", 513 => "wiiSideways", 1025 => "wiiClassic", 1537 => "ngc", _ => "wiiNunchuk" },
            _ => null,
        };

        private static readonly IBrush BodyFill = new SolidColorBrush(Color.Parse("#26223a")), ButtonFill = new SolidColorBrush(Color.Parse("#3b3558")),
            ScreenFill = new SolidColorBrush(Color.Parse("#0b0a12")), Lit = new SolidColorBrush(Color.Parse("#7b6cf6")),
            Label = Brushes.White, MutedLabel = new SolidColorBrush(Color.Parse("#c4bfd8"));
        private static readonly IPen Outline = new Pen(new SolidColorBrush(Color.Parse("#4a4466")), 2);

        public override void Render(DrawingContext context)
        {
            // Clicked anywhere on it (between the buttons too), never outside it: ButtonAt finds the button
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            if (Kind == null || !Plans.TryGetValue(Kind, out Shape[]? shapes))
                return;
            double scale = Scale;
            using var _ = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - 420 * scale) / 2, (Bounds.Height - 280 * scale) / 2));
            foreach (Shape shape in shapes)
            {
                IBrush fill = shape.Id == null ? BodyFill : shape.Id == "screen" ? ScreenFill : shape.Id == Highlight ? Lit : ButtonFill;
                IPen? pen = shape.Id == null ? Outline : null;
                switch (shape.Form)
                {
                    case Form.Rect:
                        context.DrawRectangle(fill, pen, new Rect(shape.X, shape.Y, shape.W, shape.H), shape.R, shape.R);
                        break;
                    case Form.Circle:
                        context.DrawEllipse(fill, pen, new Point(shape.X, shape.Y), shape.R, shape.R);
                        break;
                    case Form.Ellipse:
                        context.DrawEllipse(fill, Outline, new Point(shape.X, shape.Y), shape.W, shape.H);
                        break;
                    case Form.Poly:
                        var geometry = new StreamGeometry();
                        using (var draw = geometry.Open())
                        {
                            draw.BeginFigure(shape.Points![0], true);
                            foreach (Point point in shape.Points.Skip(1))
                                draw.LineTo(point);
                            draw.EndFigure(true);
                        }
                        context.DrawGeometry(fill, null, geometry);
                        break;
                    case Form.Text:
                        var text = new FormattedText(Translated(shape.Text!), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), shape.Size, shape.Muted ? MutedLabel : Label);
                        context.DrawText(text, new Point(shape.X - text.Width / 2, shape.Y - shape.Size * 0.85));
                        break;
                }
            }
        }

        private static string Translated(string text) => text switch
        {
            "Shake Wiimote" => Localization.Loc.T("Shake Wiimote"),
            "Shake Nunchuk" => Localization.Loc.T("Shake Nunchuk"),
            "Tilt" => Localization.Loc.T("Tilt"),
            "COIN" => Localization.Loc.T("Coin").ToUpperInvariant(),
            _ => text,
        };
    }
}
