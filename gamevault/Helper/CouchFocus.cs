using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>
    /// A window of the client (a game's settings, a question with a text to type) moved through with a gamepad: the
    /// cross goes to the nearest control that way, as on a console. The keyboard's own focus is used, so the control
    /// reached is framed as with the Tab key.
    /// </summary>
    internal static class CouchFocus
    {
        /// <summary>What a gamepad can stop on: buttons, boxes, lists' lines; not the parts other controls are made of.</summary>
        internal static List<Control> Candidates(Control scope) => scope.GetVisualDescendants().OfType<Control>()
            .Where(c => c is Button or TextBox or ComboBox or Slider or ListBoxItem or TabItem)
            .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Bounds.Width > 0 && c.Bounds.Height > 0)
            // A box's clear button, a slider's or a scroll bar's ends, a number's arrows
            .Where(c => c is not Button || c.TemplatedParent is not (TextBox or ComboBox or Slider or ScrollBar or ButtonSpinner or NumericUpDown or Track))
            .ToList();

        /// <summary>The control of this window that has the keyboard; null when none of it has.</summary>
        internal static Control? Focused(Control scope) =>
            TopLevel.GetTopLevel(scope)?.FocusManager?.GetFocusedElement() is Control focused && (focused == scope || scope.IsVisualAncestorOf(focused))
                ? Candidates(scope).FirstOrDefault(c => c == focused || c.IsVisualAncestorOf(focused)) ?? focused
                : null;

        private static Rect? RectOf(Control control, Control scope) =>
            control.TranslatePoint(default, scope) is Point origin ? new Rect(origin, control.Bounds.Size) : null;

        /// <summary>The distance between two ranges; 0 when they overlap.</summary>
        private static double Gap(double aStart, double aEnd, double bStart, double bEnd) => Math.Max(0, Math.Max(bStart - aEnd, aStart - bEnd));

        /// <summary>The nearest control that way: the ones in line first; from nowhere, the first one of the window.</summary>
        internal static Control? Next(Control scope, Control? from, NavigationDirection direction)
        {
            var others = Candidates(scope).Where(c => c != from).Select(c => (Control: c, Rect: RectOf(c, scope))).Where(c => c.Rect != null).Select(c => (c.Control, Rect: c.Rect!.Value)).ToList();
            if (from == null || RectOf(from, scope) is not Rect start)
                return others.OrderBy(c => c.Rect.Y).ThenBy(c => c.Rect.X).Select(c => c.Control).FirstOrDefault();
            Control? best = null;
            double bestScore = double.MaxValue;
            foreach (var (control, rect) in others)
            {
                bool horizontal = direction is NavigationDirection.Left or NavigationDirection.Right;
                double along = direction switch
                {
                    NavigationDirection.Right => rect.Center.X - start.Center.X,
                    NavigationDirection.Left => start.Center.X - rect.Center.X,
                    NavigationDirection.Down => rect.Center.Y - start.Center.Y,
                    _ => start.Center.Y - rect.Center.Y,
                };
                if (along <= 1)
                    continue;
                double gap = horizontal ? Gap(start.Left, start.Right, rect.Left, rect.Right) : Gap(start.Top, start.Bottom, rect.Top, rect.Bottom);
                double across = horizontal ? Gap(start.Top, start.Bottom, rect.Top, rect.Bottom) : Gap(start.Left, start.Right, rect.Left, rect.Right);
                // Beside is far: what is in line that way comes first
                double score = gap + across * 4 + along * 0.01;
                if (score < bestScore)
                    (best, bestScore) = (control, score);
            }
            return best;
        }

        /// <summary>True when the keyboard's focus moved.</summary>
        internal static bool Move(Control scope, NavigationDirection direction)
        {
            Control? next = Next(scope, Focused(scope), direction);
            if (next == null)
                return false;
            next.Focus(NavigationMethod.Directional);
            next.BringIntoView();
            return true;
        }

        /// <summary>The Enter key given to a control for the gamepad's A: not a key of the player's, the window lets it through.</summary>
        internal static bool Activating { get; private set; }

        /// <summary>A on a control: a button is pressed, a box checked, a line chosen.</summary>
        internal static void Activate(Control control)
        {
            if (Activating)
                return;
            Activating = true;
            try { Press(control); }
            finally { Activating = false; }
        }

        private static void Press(Control control)
        {
            switch (control)
            {
                case ListBoxItem line: line.IsSelected = true; break;
                case TabItem tab: tab.IsSelected = true; break;
                // Its choices are changed with left and right
                case ComboBox or Slider: break;
                default:
                    control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = control });
                    control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Enter, Source = control });
                    break;
            }
        }

        /// <summary>Left and right on a list of choices or a slider change its value; false for any other control.</summary>
        internal static bool Adjust(Control? control, int by)
        {
            if (control is ComboBox { IsDropDownOpen: false } choices)
            {
                if (choices.ItemCount > 0)
                    choices.SelectedIndex = Math.Clamp(choices.SelectedIndex + by, 0, choices.ItemCount - 1);
                return true;
            }
            if (control is Slider slider)
            {
                double step = slider.SmallChange > 0 ? slider.SmallChange : (slider.Maximum - slider.Minimum) / 20;
                slider.Value = Math.Clamp(slider.Value + by * step, slider.Minimum, slider.Maximum);
                return true;
            }
            return false;
        }
    }
}
