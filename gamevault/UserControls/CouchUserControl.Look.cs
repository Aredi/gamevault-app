using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;

namespace gamevault.UserControls
{
    /// <summary>What moves on the screen besides the selection: a screen that comes in, the picture that drifts.</summary>
    public partial class CouchUserControl
    {
        /// <summary>A screen or a menu comes in: from a little aside, faded in (none in the light mode).</summary>
        private void Enter(Control control, double fromX, double fromY)
        {
            if (Settings.Performance || !Settings.Animations || control.RenderTransform is not TranslateTransform)
                return;
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(240),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, fromX), new Setter(TranslateTransform.YProperty, fromY) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d), new Setter(TranslateTransform.YProperty, 0d) } },
                },
            };
            _ = animation.RunAsync(control);
        }

        /// <summary>
        /// Twice a second: the picture behind drifts slowly while the mode is looked at; still behind a game and in the
        /// light mode (a moving picture is drawn again at each frame).
        /// </summary>
        private void KeepDrift(bool active)
        {
            bool drift = active && !Settings.Performance && Settings.Drift && playing == null;
            uiBack.Classes.Set("drift", drift);
            uiShots.Classes.Set("drift", drift);
            uiPack.Classes.Set("drift", drift);
        }
    }
}
