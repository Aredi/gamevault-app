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

        internal void SetImage(IImage? image)
        {
            if (uiGif != null)
            {
                uiPanel.Children.Remove(uiGif);
                uiGif = null;
            }
            uiImg.Source = image;
            uiImg.IsVisible = true;
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
            Bitmap bitmap = await Task.Run(() => BitmapHelper.GetBitmapImage(path));
            if (generation == loadGeneration)
                SetImage(bitmap);
        }

        internal void SetReplacement()
        {
            loadGeneration++;
            SetImage(CacheHelper.GetReplacementImage(ImageCacheType));
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

        private async Task DataChanged(object? newData)
        {
            if (newData == null)
                return;

            if (UseUriSource)
            {
                string uri = newData.ToString();
                if (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                {
                    try
                    {
                        using (MemoryStream stream = new MemoryStream())
                        {
                            {
                                using (HttpResponseMessage response = await GameVault.Core.HttpClients.Shared.GetAsync(uri))
                                {
                                    if (response.IsSuccessStatusCode)
                                    {
                                        await response.Content.CopyToAsync(stream);
                                        stream.Position = 0;
                                        if (GifHelper.IsGif(stream))
                                        {
                                            stream.Position = 0;
                                            SetGif(new MemoryStream(stream.ToArray()));
                                        }
                                        else
                                        {
                                            SetImage(await BitmapHelper.GetBitmapImageAsync(stream));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MainWindowViewModel.Instance.AppBarText = ex.Message;
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
                await CacheHelper.LoadImageCacheToUIAsync(media.Identifier, imageId, cachePath, ImageCacheType, this);
            }
        }
        public IImage? GetImageSource()
        {
            return uiImg.Source;
        }
    }
}
