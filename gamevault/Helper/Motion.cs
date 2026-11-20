using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// Short animations that run once (a page arrives, the featured game changes) and then stop: the renderer works
    /// only while they play, nothing ticks when the launcher sits still. Only the opacity and a transform's numbers
    /// move, which the compositor draws without measuring or laying out anything again. Once done, the control is
    /// given back as it was.
    /// </summary>
    internal static class Motion
    {
        private static readonly Easing Out = new CubicEaseOut();
        /// <summary>GV_NO_MOTION=1: none, to measure what they cost (or for whoever does not want them).</summary>
        private static readonly bool Off = Environment.GetEnvironmentVariable("GV_NO_MOTION") == "1";

        /// <summary>What a control was before its animation, and the animation playing on it.</summary>
        private sealed class Playing
        {
            public required double Opacity { get; init; }
            public required ITransform? Transform { get; init; }
            public required RelativePoint Origin { get; init; }
            public CancellationTokenSource Cancel { get; set; } = new();
        }

        /// <summary>
        /// One per control: a new animation (the next featured game) stops the one playing, and the control is given
        /// back as it was before the first, not as the stopped one left it.
        /// </summary>
        private static readonly ConditionalWeakTable<Visual, Playing> playing = new();

        /// <summary>Fades in while sliding up a little, after <paramref name="delayMs"/> (one item after another).</summary>
        /// <param name="fade">Off: it only slides; nothing is drawn apart, cheaper for many controls at once.</param>
        public static void Enter(Visual? visual, int delayMs = 0, double fromY = 16, int durationMs = 420, bool fade = true)
        {
            if (visual == null || Off)
                return;
            // At rest where it ends: the animation holds the start while it waits, and when it is over nothing jumps
            var shift = new TranslateTransform();
            var slide = (shift, Animate(TranslateTransform.YProperty, fromY, 0d, delayMs, durationMs));
            _ = fade
                ? PlayAsync(visual, shift, RelativePoint.Center, (visual, Animate(Visual.OpacityProperty, 0d, 1d, delayMs, durationMs)), slide)
                : PlayAsync(visual, shift, RelativePoint.Center, slide);
        }

        /// <summary>Only fades in, where it is.</summary>
        public static void FadeIn(Visual? visual, int durationMs = 180)
        {
            if (visual == null || Off)
                return;
            _ = PlayAsync(visual, visual.RenderTransform ?? new TranslateTransform(), visual.RenderTransformOrigin,
                (visual, Animate(Visual.OpacityProperty, 0d, 1d, 0, durationMs)));
        }

        /// <summary>A picture that arrives a little closer and settles in place, like a camera coming to rest.</summary>
        public static void Settle(Visual? visual, double fromScale = 1.06, int durationMs = 1100)
        {
            if (visual == null || Off)
                return;
            var zoom = new ScaleTransform();
            _ = PlayAsync(visual, zoom, RelativePoint.Center,
                (zoom, Animate(ScaleTransform.ScaleXProperty, fromScale, 1d, 0, durationMs)),
                (zoom, Animate(ScaleTransform.ScaleYProperty, fromScale, 1d, 0, durationMs)));
        }

        private static Animation Animate(AvaloniaProperty property, double from, double to, int delayMs, int durationMs) => new()
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
            Easing = Out,
            // Held while it waits for its turn; once done the control's own value is back, which is where it ended
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
            },
        };

        private static async Task PlayAsync(Visual visual, ITransform transform, RelativePoint origin, params (Animatable Target, Animation Animation)[] parts)
        {
            if (playing.TryGetValue(visual, out Playing? before))
                before.Cancel.Cancel();
            else
            {
                before = new Playing { Opacity = visual.Opacity, Transform = visual.RenderTransform, Origin = visual.RenderTransformOrigin };
                playing.Add(visual, before);
            }
            Playing state = before;
            var cancel = new CancellationTokenSource();
            state.Cancel = cancel;
            visual.RenderTransformOrigin = origin;
            visual.RenderTransform = transform;
            try
            {
                var running = new Task[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    running[i] = parts[i].Animation.RunAsync(parts[i].Target, cancel.Token);
                await Task.WhenAll(running);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }// an animation is never worth an error
            finally
            {
                // Given back as it was, unless a newer animation took it over meanwhile
                if (state.Cancel == cancel)
                {
                    playing.Remove(visual);
                    visual.Opacity = state.Opacity;
                    visual.RenderTransform = state.Transform;
                    visual.RenderTransformOrigin = state.Origin;
                }
                cancel.Dispose();
            }
        }
    }
}
