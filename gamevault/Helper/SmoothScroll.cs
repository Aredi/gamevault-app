using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Runtime.CompilerServices;

namespace gamevault.Helper
{
    /// <summary>
    /// Mouse wheel steps glide instead of jumping (touchpads already scroll smoothly and are left alone).
    /// Set on the pages' ScrollViewer: <c>helper:SmoothScroll.IsEnabled="True"</c>.
    /// </summary>
    public class SmoothScroll : AvaloniaObject
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<SmoothScroll, ScrollViewer, bool>("IsEnabled");

        public static bool GetIsEnabled(ScrollViewer element) => element.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(ScrollViewer element, bool value) => element.SetValue(IsEnabledProperty, value);

        /// <summary>Pixels per wheel notch, like web browsers.</summary>
        private const double Step = 110;
        private const double DurationMs = 180;

        private sealed class State
        {
            public double From, Target;
            public DateTime Start;
            public bool Running;
            public DateTime LastMove;
        }
        private static readonly ConditionalWeakTable<ScrollViewer, State> states = new();

        static SmoothScroll()
        {
            IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((scroll, e) =>
            {
                if (e.NewValue is true)
                {
                    scroll.AddHandler(InputElement.PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
                    scroll.ScrollChanged += (_, args) =>
                    {
                        if (args.OffsetDelta.Y != 0)
                            states.GetOrCreateValue(scroll).LastMove = DateTime.UtcNow;
                    };
                }
                else
                {
                    scroll.RemoveHandler(InputElement.PointerWheelChangedEvent, Wheel);
                }
            });
        }

        private static bool IsMouseWheel(PointerWheelEventArgs e) =>
            e.Delta.X == 0 && Math.Abs(e.Delta.Y) >= 1 && Math.Abs(e.Delta.Y - Math.Round(e.Delta.Y)) < 0.001
            && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        private static void Wheel(object? sender, PointerWheelEventArgs e)
        {
            var scroll = (ScrollViewer)sender!;
            double max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            if (!IsMouseWheel(e) || max <= 0)
                return;
            State state = states.GetOrCreateValue(scroll);
            // A row of games under the pointer turns sideways with the wheel, unless the page is moving already
            if (DateTime.UtcNow - state.LastMove > TimeSpan.FromMilliseconds(450) && RowUnderPointerTakesIt(e, scroll))
                return;

            double from = scroll.Offset.Y;
            double target = (state.Running ? state.Target : from) - e.Delta.Y * Step;
            target = Math.Clamp(target, 0, max);
            e.Handled = true;
            if (Math.Abs(target - from) < 0.5)
                return;
            state.From = from;
            state.Target = target;
            state.Start = DateTime.UtcNow;
            if (!state.Running)
            {
                state.Running = true;
                Animate(scroll, state);
            }
        }

        private static bool RowUnderPointerTakesIt(PointerWheelEventArgs e, ScrollViewer page)
        {
            for (var visual = e.Source as Visual; visual != null && visual != page; visual = visual.GetVisualParent())
            {
                if (visual is ScrollViewer row && row.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)
                {
                    double max = row.Extent.Width - row.Viewport.Width;
                    if (max <= 0)
                        return false;
                    return e.Delta.Y > 0 ? row.Offset.X > 0 : row.Offset.X < max;
                }
            }
            return false;
        }

        private static void Animate(ScrollViewer scroll, State state)
        {
            var top = TopLevel.GetTopLevel(scroll);
            if (top == null)
            {
                scroll.Offset = new Vector(scroll.Offset.X, state.Target);
                state.Running = false;
                return;
            }
            top.RequestAnimationFrame(_ =>
            {
                double t = Math.Min(1, (DateTime.UtcNow - state.Start).TotalMilliseconds / DurationMs);
                double eased = 1 - Math.Pow(1 - t, 3);
                scroll.Offset = new Vector(scroll.Offset.X, state.From + (state.Target - state.From) * eased);
                if (t < 1)
                    Animate(scroll, state);
                else
                    state.Running = false;
            });
        }
    }
}
