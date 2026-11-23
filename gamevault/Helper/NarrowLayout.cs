using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Linq;
using System.Runtime.CompilerServices;

namespace gamevault.Helper
{
    /// <summary>
    /// A row of a page made for a computer (a title, then counters, then buttons, side by side): on a narrow screen (a
    /// phone), each of its columns on a line of its own, from the left. Set on the grid: <c>helper:NarrowLayout.Stacks="600"</c>
    /// (the width under which it stacks).
    /// </summary>
    public class NarrowLayout : AvaloniaObject
    {
        public static readonly AttachedProperty<double> StacksProperty =
            AvaloniaProperty.RegisterAttached<NarrowLayout, Grid, double>("Stacks");

        public static double GetStacks(Grid grid) => grid.GetValue(StacksProperty);
        public static void SetStacks(Grid grid, double value) => grid.SetValue(StacksProperty, value);

        /// <summary>
        /// A line of a table (a name, then a role, a switch, buttons): on a narrow screen, its first column on the whole
        /// width, the others on a second line, on the right.
        /// </summary>
        public static readonly AttachedProperty<double> FirstOnTopProperty =
            AvaloniaProperty.RegisterAttached<NarrowLayout, Grid, double>("FirstOnTop");

        public static double GetFirstOnTop(Grid grid) => grid.GetValue(FirstOnTopProperty);
        public static void SetFirstOnTop(Grid grid, double value) => grid.SetValue(FirstOnTopProperty, value);

        private sealed record Place(int Row, int Column, int ColumnSpan, HorizontalAlignment Alignment, Thickness Margin);
        private static readonly ConditionalWeakTable<Control, Place> places = new();
        private static readonly ConditionalWeakTable<Grid, StrongBox<bool>> stacked = new();
        private static readonly ConditionalWeakTable<Grid, System.Collections.Generic.List<(GridLength Width, double ColumnSpacing)>> columnWidths = new();

        static NarrowLayout()
        {
            StacksProperty.Changed.AddClassHandler<Grid>((grid, e) =>
            {
                if (e.NewValue is double width && width > 0)
                    grid.SizeChanged += (_, args) => Apply(grid, args.NewSize.Width < GetStacks(grid));
            });
            FirstOnTopProperty.Changed.AddClassHandler<Grid>((grid, e) =>
            {
                if (e.NewValue is double width && width > 0)
                    grid.SizeChanged += (_, args) => ApplyFirstOnTop(grid, args.NewSize.Width < GetFirstOnTop(grid));
            });
        }

        private static void ApplyFirstOnTop(Grid grid, bool narrow)
        {
            var state = stacked.GetOrCreateValue(grid);
            if (state.Value == narrow)
                return;
            state.Value = narrow;
            int columns = grid.ColumnDefinitions.Count;
            if (narrow && grid.RowDefinitions.Count < 2)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            grid.RowSpacing = narrow ? 8 : grid.RowSpacing;
            // The fixed widths of a computer's table no longer fit beside each other: each column as wide as what it holds
            var widths = columnWidths.GetValue(grid, g => g.ColumnDefinitions.Select(c => (c.Width, g.ColumnSpacing)).ToList());
            for (int i = 1; i < columns; i++)
                grid.ColumnDefinitions[i].Width = narrow ? GridLength.Auto : widths[i].Width;
            grid.ColumnSpacing = narrow ? 8 : widths[0].ColumnSpacing;
            foreach (Control child in grid.Children.OfType<Control>().ToList())
            {
                Place place = places.GetValue(child, c => new Place(Grid.GetRow(c), Grid.GetColumn(c), Grid.GetColumnSpan(c), c.HorizontalAlignment, c.Margin));
                bool first = place.Column == 0;
                Grid.SetRow(child, narrow && !first ? place.Row + 1 : place.Row);
                Grid.SetColumnSpan(child, narrow && first ? columns : place.ColumnSpan);
            }
        }

        private static void Apply(Grid grid, bool narrow)
        {
            var state = stacked.GetOrCreateValue(grid);
            if (state.Value == narrow)
                return;
            state.Value = narrow;
            int columns = grid.ColumnDefinitions.Count;
            if (narrow)
            {
                while (grid.RowDefinitions.Count < columns)
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            foreach (Control child in grid.Children.OfType<Control>().ToList())
            {
                Place place = places.GetValue(child, c => new Place(Grid.GetRow(c), Grid.GetColumn(c), Grid.GetColumnSpan(c), c.HorizontalAlignment, c.Margin));
                if (narrow && place.Column > 0)
                {
                    Grid.SetRow(child, place.Row + place.Column);
                    Grid.SetColumn(child, 0);
                    Grid.SetColumnSpan(child, columns);
                    child.HorizontalAlignment = HorizontalAlignment.Left;
                    child.Margin = new Thickness(0, 10, 0, 0);
                }
                else
                {
                    Grid.SetRow(child, place.Row);
                    Grid.SetColumn(child, place.Column);
                    Grid.SetColumnSpan(child, place.ColumnSpan);
                    child.HorizontalAlignment = place.Alignment;
                    child.Margin = place.Margin;
                }
            }
        }
    }
}
