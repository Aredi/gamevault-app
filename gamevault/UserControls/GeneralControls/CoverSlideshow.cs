using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Helper;
using gamevault.Models;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// Over a game card's cover: while the card is hovered a moment, the game's screenshots one after the other, fading in and
    /// slowly zooming across (a preview of the game without a video player). Nothing is loaded before a card is hovered.
    /// </summary>
    public sealed class CoverSlideshow : Panel
    {
        /// <summary>Hovered this long before it starts: passing over the cards does not load anything.</summary>
        internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(600);
        internal static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(2400);
        private static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(450);
        private const int MaxSlides = 6;
        private const int DecodeWidth = 640;

        private readonly Image[] layers = new Image[2];
        private readonly DispatcherTimer timer = new();
        private Button? card;
        private int front;
        private int slide;
        private int generation;

        public CoverSlideshow()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i] = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0,
                    RenderTransformOrigin = RelativePoint.Center,
                    Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = FadeTime } },
                };
                Children.Add(layers[i]);
            }
            timer.Tick += async (_, _) => await NextAsync();
        }

        /// <summary>Running: a screenshot is shown (the UI tests check it).</summary>
        internal bool IsShowing => Shown != null;

        /// <summary>The screenshot shown (fading in or shown), null while the cover is.</summary>
        internal IImage? Shown { get; private set; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            card = this.FindAncestorOfType<Button>();
            if (card != null)
            {
                card.PointerEntered += Card_PointerEntered;
                card.PointerExited += Card_PointerExited;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (card != null)
            {
                card.PointerEntered -= Card_PointerEntered;
                card.PointerExited -= Card_PointerExited;
                card = null;
            }
            Stop();
            base.OnDetachedFromVisualTree(e);
        }

        // A recycled card shows another game: not this one's screenshots
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Stop();
        }

        private void Card_PointerEntered(object? sender, PointerEventArgs e) => Start();
        private void Card_PointerExited(object? sender, PointerEventArgs e) => Stop();

        internal void Start()
        {
            Stop();
            if (Sources(DataContext).Count == 0)
                return;
            slide = -1;
            timer.Interval = Delay;
            timer.Start();
        }

        internal void Stop()
        {
            generation++;
            timer.Stop();
            Shown = null;
            foreach (Image layer in layers)
                layer.Opacity = 0;
        }

        private async Task NextAsync()
        {
            timer.Stop();
            int asked = generation;
            List<string> sources = Sources(DataContext);
            if (sources.Count == 0)
                return;
            Bitmap? image = null;
            // A screenshot that can not be loaded is passed
            for (int tries = 0; tries < sources.Count && image == null; tries++)
            {
                slide = (slide + 1) % sources.Count;
                image = await LoadAsync(DataContext, sources[slide]);
                if (asked != generation)
                    return;
            }
            if (image == null)
                return;
            // A single screenshot stays and keeps moving slowly
            Image next = layers[1 - front];
            Image shown = layers[front];
            next.Transitions = null;
            // Across the screenshot, one way then the other: 4 % of the card's width
            string shift = (Bounds.Width * 0.04).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            string from = slide % 2 == 0 ? shift : "-" + shift, to = slide % 2 == 0 ? "-" + shift : shift;
            next.RenderTransform = TransformOperations.Parse($"scale(1.04) translateX({from}px)");
            next.Source = image;
            next.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = FadeTime },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = SlideTime + FadeTime, Easing = new LinearEasing() },
            };
            next.Opacity = 1;
            next.RenderTransform = TransformOperations.Parse($"scale(1.14) translateX({to}px)");
            shown.Opacity = 0;
            front = 1 - front;
            Shown = image;
            if (sources.Count > 1)
            {
                timer.Interval = SlideTime;
                timer.Start();
            }
        }

        /// <summary>The screenshots of a library game (their addresses) or of a retro game (RomM's media).</summary>
        internal static List<string> Sources(object? game) => game switch
        {
            RetroGame retro => retro.Screenshots.Take(MaxSlides).ToList(),
            Game pc => (pc.Metadata?.Screenshots ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Take(MaxSlides).ToList(),
            _ => new List<string>(),
        };

        private static async Task<Bitmap?> LoadAsync(object? game, string source)
        {
            try
            {
                if (game is RetroGame)
                {
                    string file = await RetroService.MediaFileAsync(source);
                    return DecodedImages.TryGet(file, DecodeWidth) ?? await Task.Run(() => DecodedImages.Load(file, DecodeWidth));
                }
                if (DecodedImages.TryGetUrl(source, DecodeWidth) is Bitmap known)
                    return known;
                using HttpResponseMessage response = await HttpClients.Shared.GetAsync(source);
                if (!response.IsSuccessStatusCode)
                    return null;
                byte[] data = await response.Content.ReadAsByteArrayAsync();
                return await Task.Run(() => DecodedImages.LoadUrl(source, data, DecodeWidth));
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }
    }
}
