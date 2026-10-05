using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace gamevault.UserControls
{
    public record PieSlice(string Name, double Percentage, Color Color, string Label);

    /// <summary>
    /// Doughnut chart with a legend on the right (replacement for the LiveCharts pie chart of the disk usage view).
    /// </summary>
    public class PieChart : Control
    {
        public static readonly StyledProperty<IReadOnlyList<PieSlice>?> SlicesProperty =
            AvaloniaProperty.Register<PieChart, IReadOnlyList<PieSlice>?>(nameof(Slices));
        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            TextBlock.ForegroundProperty.AddOwner<PieChart>();

        static PieChart()
        {
            AffectsRender<PieChart>(SlicesProperty, ForegroundProperty);
        }

        public IReadOnlyList<PieSlice>? Slices
        {
            get => GetValue(SlicesProperty);
            set => SetValue(SlicesProperty, value);
        }

        public IBrush? Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            var slices = Slices;
            if (slices == null || slices.Count == 0)
                return;

            double legendWidth = Math.Min(260, Bounds.Width * 0.45);
            double size = Math.Min(Bounds.Width - legendWidth - 20, Bounds.Height) - 20;
            if (size <= 0)
                return;

            var center = new Point(10 + size / 2, Bounds.Height / 2);
            double outer = size / 2;
            double thickness = Math.Min(50, outer * 0.45);
            double total = slices.Sum(s => Math.Max(0, s.Percentage));
            if (total <= 0)
                return;

            double angle = -90; // start at the top, clockwise
            foreach (var slice in slices)
            {
                double sweep = Math.Max(0, slice.Percentage) / total * 360;
                if (sweep <= 0)
                    continue;
                var pen = new Pen(new SolidColorBrush(slice.Color), thickness);
                var geometry = CreateArc(center, outer - thickness / 2, angle, Math.Min(sweep, 359.99));
                context.DrawGeometry(null, pen, geometry);
                angle += sweep;
            }

            // Legend
            IBrush textBrush = Foreground ?? Brushes.White;
            double x = Bounds.Width - legendWidth;
            double y = Math.Max(0, Bounds.Height / 2 - slices.Count * 22);
            foreach (var slice in slices)
            {
                context.DrawRectangle(new SolidColorBrush(slice.Color), null, new Rect(x, y + 4, 12, 12), 2, 2);
                var name = new FormattedText(slice.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 13, textBrush)
                {
                    MaxTextWidth = legendWidth - 20,
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                context.DrawText(name, new Point(x + 18, y));
                var label = new FormattedText(slice.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, new SolidColorBrush(slice.Color));
                context.DrawText(label, new Point(x + 18, y + 17));
                y += 44;
            }
        }

        private static Geometry CreateArc(Point center, double radius, double startAngle, double sweepAngle)
        {
            Point PointAt(double degrees)
            {
                double rad = degrees * Math.PI / 180;
                return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
            }
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(PointAt(startAngle), false);
                ctx.ArcTo(PointAt(startAngle + sweepAngle), new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            return geometry;
        }
    }
}
