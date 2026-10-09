using gamevault.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    public partial class CacheImage : UserControl
    {
        #region Properties
        public static readonly StyledProperty<ImageCache> ImageCacheTypeProperty = AvaloniaProperty.Register<CacheImage, ImageCache>(nameof(ImageCacheType));
        public ImageCache ImageCacheType
        {
            get => GetValue(ImageCacheTypeProperty);
            set => SetValue(ImageCacheTypeProperty, value);
        }

        public static readonly StyledProperty<bool> UseUriSourceProperty = AvaloniaProperty.Register<CacheImage, bool>(nameof(UseUriSource));
        public bool UseUriSource
        {
            get => GetValue(UseUriSourceProperty);
            set => SetValue(UseUriSourceProperty, value);
        }

        public static readonly StyledProperty<bool> DoNotCacheProperty = AvaloniaProperty.Register<CacheImage, bool>(nameof(DoNotCache));
        public bool DoNotCache
        {
            get => GetValue(DoNotCacheProperty);
            set => SetValue(DoNotCacheProperty, value);
        }

        public static readonly StyledProperty<Stretch> StretchProperty = AvaloniaProperty.Register<CacheImage, Stretch>(nameof(Stretch), Stretch.UniformToFill);
        public Stretch Stretch
        {
            get => GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        /// <summary>A game without a background image shows its cover instead (blurred by the page if it wants).</summary>
        public static readonly StyledProperty<bool> FallbackToCoverProperty = AvaloniaProperty.Register<CacheImage, bool>(nameof(FallbackToCover));
        public bool FallbackToCover
        {
            get => GetValue(FallbackToCoverProperty);
            set => SetValue(FallbackToCoverProperty, value);
        }

        public static readonly StyledProperty<bool> IsShowingFallbackProperty = AvaloniaProperty.Register<CacheImage, bool>(nameof(IsShowingFallback));
        /// <summary>The cover is shown because the game has no background image.</summary>
        public bool IsShowingFallback
        {
            get => GetValue(IsShowingFallbackProperty);
            private set => SetValue(IsShowingFallbackProperty, value);
        }

        /// <summary>Width the image is decoded at: covers are shown small, backgrounds large (0: as the type says).</summary>
        public static readonly StyledProperty<int> DecodeWidthOverrideProperty = AvaloniaProperty.Register<CacheImage, int>(nameof(DecodeWidthOverride));
        public int DecodeWidthOverride
        {
            get => GetValue(DecodeWidthOverrideProperty);
            set => SetValue(DecodeWidthOverrideProperty, value);
        }
        private int? DecodeWidth => DecodeWidthOverride > 0 ? DecodeWidthOverride : ImageCacheType switch
        {
            ImageCache.GameCover => 480,
            ImageCache.GameBackground => IsShowingFallback ? 480 : 2560,
            ImageCache.UserAvatar => 256,
            ImageCache.UserBackground => 2560,
            _ => null,
        };

        public static readonly StyledProperty<bool> ComputeAccentProperty = AvaloniaProperty.Register<CacheImage, bool>(nameof(ComputeAccent));
        /// <summary>Analyse the shown image for <see cref="AccentColor"/>.</summary>
        public bool ComputeAccent
        {
            get => GetValue(ComputeAccentProperty);
            set => SetValue(ComputeAccentProperty, value);
        }

        public static readonly StyledProperty<Color?> AccentColorProperty = AvaloniaProperty.Register<CacheImage, Color?>(nameof(AccentColor));
        /// <summary>The dominant vivid color of the image (null until known, or for images without color).</summary>
        public Color? AccentColor
        {
            get => GetValue(AccentColorProperty);
            private set => SetValue(AccentColorProperty, value);
        }

        //Data is a separate property (instead of DataContext), because DataContext changes before ImageCacheType is set inside a DataTemplate.
        public static readonly StyledProperty<object?> DataProperty = AvaloniaProperty.Register<CacheImage, object?>(nameof(Data));
        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override async void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == StretchProperty)
            {
                if (Stretch != Stretch.None)
                {
                    uiImg.Stretch = Stretch;
                    if (uiGif != null)
                        uiGif.Stretch = Stretch;
                }
            }
            else if (change.Property == CornerRadiusProperty)
            {
                uiBorder.CornerRadius = CornerRadius;
            }
            else if (change.Property == DataProperty)
            {
                await DataChanged(change.NewValue);
            }
        }
        #endregion

        #region Display
        private int loadGeneration;

        // GifImage throws when it is attached without a source, so it only exists while a GIF is shown
        private Avalonia.Labs.Gif.GifImage? uiGif;

        internal void SetImage(IImage? image) => SetImage(image, fade: true);

        internal void SetImage(IImage? image, bool fade)
        {
            if (uiGif != null)
            {
                uiPanel.Children.Remove(uiGif);
                uiGif = null;
            }
            uiImg.Source = image;
            uiImg.IsVisible = true;
            IsShowingReplacement = false;
            SetOpacity(1, fade);
        }

        /// <summary>Opacity with or without its fade (fades only for images that took a moment to arrive).</summary>
        private void SetOpacity(double opacity, bool fade)
        {
            if (fade)
            {
                uiImg.Opacity = opacity;
                return;
            }
            var transitions = uiImg.Transitions;
            uiImg.Transitions = null;
            uiImg.Opacity = opacity;
            uiImg.Transitions = transitions;
        }

        internal void SetGif(Stream gif)
        {
            uiImg.Source = null;
            uiImg.IsVisible = false;
            if (uiGif != null)
                uiPanel.Children.Remove(uiGif);
            uiGif = new Avalonia.Labs.Gif.GifImage { Stretch = Stretch == Stretch.None ? Stretch.UniformToFill : Stretch, Source = gif };
            uiPanel.Children.Add(uiGif);
        }

        /// <summary>
        /// Shows an image file, animated if it is a GIF. Decoding happens off the UI thread.
        /// </summary>
        internal async Task SetImageFileAsync(string path)
        {
            int generation = ++loadGeneration;
            if (GifHelper.IsGif(path))
            {
                byte[] data = await File.ReadAllBytesAsync(path);
                if (generation == loadGeneration)
                    SetGif(new MemoryStream(data));
                return;
            }
            bool computeAccent = ComputeAccent;
            int? decodeWidth = DecodeWidth;// read on the UI thread
            // Already in memory (scrolling back): shown at once, without a fade
            if (DecodedImages.TryGet(path, decodeWidth) is Bitmap known)
            {
                SetImage(known, fade: false);
                AccentColor = computeAccent ? CoverColors.Get(known, path) : null;
                return;
            }
            (Bitmap bitmap, Color? accent) = await Task.Run(() =>
            {
                Bitmap decoded = DecodedImages.Load(path, decodeWidth);
                return (decoded, computeAccent ? CoverColors.Get(decoded, path) : null);
            });
            if (generation == loadGeneration)
            {
                SetImage(bitmap);
                AccentColor = accent;
            }
        }

        /// <summary>The "no cover" image is shown: the real one is missing or could not be read.</summary>
        public bool IsShowingReplacement { get; private set; }

        internal void SetReplacement()
        {
            loadGeneration++;
            IsShowingReplacement = true;
            SetImage(CacheHelper.GetReplacementImage(IsShowingFallback ? ImageCache.GameCover : ImageCacheType));
            IsShowingReplacement = true;
            AccentColor = null;
        }
        #endregion


        #region Convertion Object
        public struct CacheImageMedia
        {
            public int Identifier;
            public int CoverID;
            public int BackgroundID;
            public void Convert(object dataToConvert)
            {
                if (typeof(Game) == dataToConvert.GetType())
                {
                    Identifier = ((Game)dataToConvert) == null ? -1 : ((Game)dataToConvert).ID;
                    CoverID = ((Game)dataToConvert)?.Metadata?.Cover?.ID ?? -1;
                    BackgroundID = ((Game)dataToConvert)?.Metadata?.Background?.ID ?? -1;
                }
                else if (typeof(Progress) == dataToConvert.GetType())
                {
                    Identifier = ((Progress)dataToConvert).Game == null ? -1 : ((Progress)dataToConvert).Game!.ID;
                    CoverID = ((Progress)dataToConvert).Game?.Metadata?.Cover?.ID ?? -1;
                    BackgroundID = ((Progress)dataToConvert).Game?.Metadata?.Background?.ID ?? -1;
                }
                else if (typeof(GameMetadata) == dataToConvert.GetType())
                {
                    Identifier = -1;
                    CoverID = ((GameMetadata)dataToConvert)?.Cover?.ID ?? -1;
                    BackgroundID = ((GameMetadata)dataToConvert)?.Background?.ID ?? -1;
                }
                else if (typeof(User) == dataToConvert.GetType())
                {
                    Identifier = ((User)dataToConvert) == null ? -1 : ((User)dataToConvert).ID;
                    CoverID = ((User)dataToConvert)?.Avatar?.ID ?? -1;
                    BackgroundID = ((User)dataToConvert)?.Background?.ID ?? -1;
                }
            }
        }
        #endregion

        public CacheImage()
        {
            InitializeComponent();
        }

        /// <summary>Changes with each new image asked for: a download that ends late does not replace a newer image.</summary>
        internal int DataGeneration { get; private set; }

        private async Task DataChanged(object? newData)
        {
            if (newData == null)
                return;
            DataGeneration++;
            // A recycled card must not show the previous game's image while the new one loads
            if (ImageCacheType is ImageCache.GameCover or ImageCache.GameBackground)
                SetOpacity(0, fade: false);

            if (UseUriSource)
            {
                string uri = newData.ToString();
                if (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                {
                    // Screenshots: decoded off the UI thread, at the size they are shown at, kept in memory
                    int generation = ++loadGeneration;
                    int? width = DecodeWidthOverride > 0 ? DecodeWidthOverride : 1600;
                    if (DecodedImages.TryGetUrl(uri, width) is Bitmap known)
                    {
                        SetImage(known, fade: false);
                        return;
                    }
                    try
                    {
                        using HttpResponseMessage response = await GameVault.Core.HttpClients.Shared.GetAsync(uri);
                        if (!response.IsSuccessStatusCode || generation != loadGeneration)
                            return;
                        byte[] data = await response.Content.ReadAsByteArrayAsync();
                        if (generation != loadGeneration)
                            return;
                        if (GifHelper.IsGif(new MemoryStream(data)))
                        {
                            SetGif(new MemoryStream(data));
                            return;
                        }
                        Bitmap bitmap = await Task.Run(() => DecodedImages.LoadUrl(uri, data, width));
                        // A newer image may have been asked for meanwhile (screenshots changed quickly)
                        if (generation == loadGeneration)
                            SetImage(bitmap);
                    }
                    catch (Exception ex)
                    {
                        Log.Ignored(ex);
                    }
                }
                else
                {
                    try
                    {
                        if (string.IsNullOrEmpty(uri))
                            throw new FileNotFoundException();
                        await SetImageFileAsync(uri);
                    }
                    catch (Exception ex)
                    {
                        GameVault.Core.Log.Ignored(ex);
                        SetReplacement();
                    }
                }
                return;
            }

            int imageId = -1;
            string? cachePath = LoginManager.Instance.GetUserProfile()?.ImageCacheDir;
            if (cachePath == null)
            {
                SetReplacement();
                return;
            }

            CacheImageMedia media = new CacheImageMedia();
            bool fallback = false;
            try
            {
                media.Convert(newData);
                switch (ImageCacheType)
                {
                    case ImageCache.GameCover:
                        {
                            cachePath = Path.Combine(cachePath, "gbox");
                            imageId = media.CoverID;
                            break;
                        }
                    case ImageCache.GameBackground:
                        {
                            if (media.BackgroundID == -1 && FallbackToCover && media.CoverID != -1)
                            {
                                cachePath = Path.Combine(cachePath, "gbox");
                                imageId = media.CoverID;
                                fallback = true;
                                break;
                            }
                            cachePath = Path.Combine(cachePath, "gbg");
                            imageId = media.BackgroundID;
                            break;
                        }
                    case ImageCache.UserAvatar:
                        {
                            cachePath = Path.Combine(cachePath, "uico");
                            imageId = media.CoverID;
                            break;
                        }
                    case ImageCache.UserBackground:
                        {
                            cachePath = Path.Combine(cachePath, "ubg");
                            imageId = media.BackgroundID;
                            break;
                        }
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            IsShowingFallback = fallback;
            if (DoNotCache)
            {
                try
                {
                    if (imageId == -1) { throw new Exception(Loc.T("image id does not exist")); }
                    SetImage(await WebHelper.DownloadImageFromUrlAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media/{imageId}"));
                }
                catch (Exception ex)
                {
                    GameVault.Core.Log.Ignored(ex);
                    SetReplacement();
                }
            }
            else
            {
                await CacheHelper.LoadImageCacheToUIAsync(media.Identifier, imageId, cachePath, fallback ? ImageCache.GameCover : ImageCacheType, this);
            }
        }
        public IImage? GetImageSource()
        {
            return uiImg.Source;
        }
    }
}
