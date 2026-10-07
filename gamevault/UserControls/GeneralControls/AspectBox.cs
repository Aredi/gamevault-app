using Avalonia;
using Avalonia.Controls;

namespace gamevault.UserControls
{
    /// <summary>Takes the available width and a height of <see cref="Ratio"/> times that width (covers are 2:3, art 16:9).</summary>
    public class AspectBox : Decorator
    {
        public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<AspectBox, double>(nameof(Ratio), 1.5);
        /// <summary>Height divided by width.</summary>
        public double Ratio
        {
            get => GetValue(RatioProperty);
            set => SetValue(RatioProperty, value);
        }

        static AspectBox()
        {
            AffectsMeasure<AspectBox>(RatioProperty);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? (double.IsNaN(Width) ? 0 : Width) : availableSize.Width;
            var size = new Size(width, width * Ratio);
            Child?.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = new Size(finalSize.Width, finalSize.Width * Ratio);
            Child?.Arrange(new Rect(size));
            return size;
        }
    }
}
