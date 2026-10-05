using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Helper.Platform;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for MediaSlider.xaml
    /// </summary>
    public partial class MediaSlider : UserControl
    {
        private List<Tuple<string, string>> MediaUrls = new List<Tuple<string, string>>();
        private int mediaIndex = 0;
        private bool isMediaSliderFullscreen = false;
        private Panel webViewAnchor;
        private bool navigationHooked;
        public MediaSlider()
        {
            InitializeComponent();
            uiRoot.KeyDown += MediaSliderFullscreen_Escape_KeyDown;
            uiVolumeSlider.AddHandler(Thumb.DragCompletedEvent, VolumeSlider_DragCompleted, RoutingStrategies.Bubble);
            uiVolumeSlider.PropertyChanged += async (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                    await ApplyVolume();
            };
        }
        #region Events
        private void ReloadMediaSlider_Click(object sender, RoutedEventArgs e)
        {
            ReloadMediaSlider();
        }
        private void MediaSliderFullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }
        private void MediaSliderFullscreen_Escape_KeyDown(object? sender, KeyEventArgs e)
        {
            if (isMediaSliderFullscreen && e.Key == Key.Escape)
            {
                ToggleFullscreen();
            }
        }
        private async void NextMedia_Click(object sender, RoutedEventArgs e)
        {
            if (MediaUrls.Count < 1)
                return;

            if (mediaIndex < MediaUrls.Count - 1)
            {
                mediaIndex++;
                await MediaSliderNavigate(MediaUrls[mediaIndex].Item1);
            }
            else
            {
                mediaIndex = 0;
                await MediaSliderNavigate(MediaUrls[mediaIndex].Item1);
            }
            uiTxtMediaIndex.Text = $"{mediaIndex + 1}/{MediaUrls.Count}";
        }
        private async void PrevMedia_Click(object sender, RoutedEventArgs e)
        {
            if (MediaUrls.Count < 1)
                return;

            if (mediaIndex > 0)
            {
                mediaIndex--;
                await MediaSliderNavigate(MediaUrls[mediaIndex].Item1);
            }
            else
            {
                mediaIndex = MediaUrls.Count - 1;
                await MediaSliderNavigate(MediaUrls[mediaIndex].Item1);
            }
            uiTxtMediaIndex.Text = $"{mediaIndex + 1}/{MediaUrls.Count}";
        }
        #endregion
        #region Public 
        public void UnloadMediaSlider()
        {
            if (uiWebView != null)
            {
                uiWebView.IsVisible = false;
                uiWebView.NavigateToString("<html><body style='background:black'></body></html>");
            }
        }
        public async Task RestoreLastMediaVolume()
        {
            string result = Preferences.Get(AppConfigKey.MediaSliderVolume,LoginManager.Instance.GetUserProfile().UserConfigFile);
            string lastMediaVolume = string.IsNullOrWhiteSpace(result) ? "0.0" : result;
            if (double.TryParse(lastMediaVolume.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double volume))
            {
                uiVolumeSlider.Value = volume;
                string restoreLastMediaVolumeScript = @"
    if (typeof audio !== 'undefined' && audio) {
        audio.volume = " + lastMediaVolume + @";
    }
    var video = document.querySelector('video[name=""media""]');
    if (typeof video !== 'undefined' && video) {
        video.volume = " + lastMediaVolume + @";
    }";
                await RunScript(restoreLastMediaVolumeScript);
            }
        }
        public async Task SetAndSaveMediaVolume()
        {
            try
            {
                string result = uiVolumeSlider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Preferences.Set(AppConfigKey.MediaSliderVolume, result,LoginManager.Instance.GetUserProfile().UserConfigFile);
                await ApplyVolume();
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async Task ApplyVolume()
        {
            try
            {
                if (uiWebView == null)
                    return;
                string result = uiVolumeSlider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

                string setAndSaveMediaVolumeScript = @"
    if(typeof audio !== 'undefined' && audio) {
        audio.volume=" + result + @";
    }
    if(typeof video !== 'undefined' && video) {
        video.volume=" + result + @";
    }";
                await RunScript(setAndSaveMediaVolumeScript);

            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private async void VolumeSlider_DragCompleted(object? sender, VectorEventArgs e)
        {
            await SetAndSaveMediaVolume();
        }
        /// <summary>
        /// WebKitGTK fails scripts whose completion value is not serializable ("Unsupported result type"),
        /// e.g. an assignment of a DOM element, so every script ends with an empty string.
        /// </summary>
        private async Task RunScript(string script)
        {
            if (uiWebView == null)
                return;
            await uiWebView.InvokeScript(script + "\n;'';");
        }
        public Task InitVideoPlayer()
        {
            if (navigationHooked || uiWebView == null)
                return Task.CompletedTask;
            navigationHooked = true;
            uiWebView.NavigationCompleted += async (s, e) =>
            {
                if (!e.IsSuccess)
                    return;
                NativeWebViewPlacement.Nudge(uiWebView);
                if (mediaIndex < 0 || mediaIndex >= MediaUrls.Count)
                    return;// Blank page (unloaded slider or a game without media)
                try
                {
                    await CreateAudioStream();
                    await RestoreLastMediaVolume();
                    await ResizeMediaSlider();
                    await RunScript(cssscript);
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            };
            return Task.CompletedTask;
        }
        public void SetMediaList(List<Tuple<string, string>> mediaUrls)
        {
            this.MediaUrls = mediaUrls;
            uiMediaCountLoadingRing.IsActive = false;
            uiTxtMediaIndex.IsVisible = true;
            uiTxtMediaIndex.Text = MediaUrls.Count == 0 ? "0/0" : $"{mediaIndex + 1}/{MediaUrls.Count}";
        }

        public async Task LoadFirstElement(Tuple<string, string>? first = null)
        {
            if (first != null)
            {
                MediaUrls.Add(first);
            }
            if (uiWebView != null && uiWebView.IsVisible && MediaUrls.Count > 0)//Prevent only in this case from navigating because the Media Slider could be rendered on top of the game settings
            {
                mediaIndex = 0;
                await MediaSliderNavigate(MediaUrls[mediaIndex].Item1);
            }
        }
        public bool IsWebViewNull()
        {
            return uiWebView == null;
        }


        #endregion
        #region Private
        private void ReloadMediaSlider()
        {
            uiWebView.IsVisible = true;
            if (MediaUrls.Count > 0)
            {
                uiWebView.Navigate(new Uri(MediaUrls[mediaIndex].Item1));
            }
        }
        private async Task ResizeMediaSlider()
        {
            if (uiWebView == null)
                return;

            await RunScript(resizescript);
        }
        private async Task MediaSliderNavigate(string url)
        {
            if (uiWebView == null)
                return;

            if (uiWebView.IsVisible == false)
            {
                uiWebView.IsVisible = true;
            }
            try
            {
                uiWebView.Navigate(new Uri(url));
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private void ToggleFullscreen()
        {
            try
            {
                // Keep the native web view alive while it moves between parents.
                using IDisposable reparenting = uiWebView.BeginReparenting();
                if (!isMediaSliderFullscreen)
                {
                    isMediaSliderFullscreen = true;
                    webViewAnchor = (Panel)this.Parent!;
                    webViewAnchor.Children.Remove(this);
                    MainWindowViewModel.Instance.OpenPopup(this);
                }
                else
                {
                    isMediaSliderFullscreen = false;
                    MainWindowViewModel.Instance.ClosePopup();
                    webViewAnchor.Children.Add(this);
                }
            }
            catch (Exception ex)
            { Log.Ignored(ex); }//Probably is the Visual not disconnected from its Parent
        }
        #endregion

        public void Dispose()
        {
            if (uiWebView == null)
                return;

            uiWebView.NavigateToString("<html></html>");
        }
        #region JS_SCRIPTS
        private string cssscript = @"
        var style = document.createElement('style');
        style.type = 'text/css';
        var cssRules = `video::-webkit-media-controls-fullscreen-button {
            display: none !important;
        }
        video::-ms-media-controls-fullscreen-button {
            display: none !important;
        }
      
video::-webkit-media-controls-volume-slider {
display:none;
}

video::-webkit-media-controls-mute-button {
display:none;
}

`;
        style.appendChild(document.createTextNode(cssRules));
        (document.head || document.documentElement).appendChild(style); // WebKitGTK media documents have no <head>
    ";
        private string getVolumeScript = @"
(function() {
    var audio = document.getElementById('externalAudio'); // Get the audio element by ID
    return audio.volume; // Return current volume
})();";


        string resizescript = @"
 var video = document.querySelector('video[name=""media""]');
if(video)
{
           "
             + @" video.style.width = 16000 + 'px';
            video.style.height = 9000 + 'px';"
             + @"
            video.style.position = 'fixed'; // Ensure it is positioned to cover the viewport
            video.style.top = '0';
            video.style.left = '0';
            video.style.zIndex = '1000'; // Ensure it is on top of other elements
}
   ";
        private async Task CreateAudioStream()
        {
            // Navigations to the blank page (unload, game without media) have no media entry
            if (mediaIndex < 0 || mediaIndex >= MediaUrls.Count)
                return;
            string audioScript = @"
    // Select the video element by name attribute 'media'
    var video = document.querySelector('video[name=""media""]');
    if(video)
{
    // Dynamically create an audio element
    var audio = document.createElement('audio');
    audio.setAttribute('id', 'externalAudio');
    audio.src = '" + MediaUrls[mediaIndex].Item2 + @"';  // Specify your external audio source
    audio.controls = false;
    document.body.appendChild(audio);  // Add the audio element to the body

    // Synchronize audio with video playback
    video.addEventListener('play', function() {
        audio.currentTime = video.currentTime;
        audio.play();
    });

    video.addEventListener('pause', function() {
        audio.pause();
    });

    video.addEventListener('seeking', function() {
        audio.currentTime = video.currentTime;
    });

    video.addEventListener('timeupdate', function() {
        var diff = Math.abs(video.currentTime - audio.currentTime);
        if (diff > 0.3) {
            audio.currentTime = video.currentTime;
        }
    });
}
";
            if (MediaUrls[mediaIndex] != null && MediaUrls[mediaIndex].Item2 != "")
            {
                await RunScript(audioScript);
            }
        }




        #endregion

    }
}
