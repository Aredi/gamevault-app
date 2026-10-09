using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace gamevault.UserControls
{
    /// <summary>
    /// The trailers and gameplay videos of a game, played in a window over the page. Keeping the web player out of
    /// the game page lets the page scroll smoothly and no video starts by itself.
    /// </summary>
    public partial class TrailerPopup : UserControl
    {
        private readonly GameMetadata metadata;
        private static YoutubeClient? youtube;

        public TrailerPopup(GameMetadata metadata, string title)
        {
            InitializeComponent();
            this.metadata = metadata;
            uiTitle.Text = title;
            Loaded += async (_, _) =>
            {
                Focus();
                await Load();
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        /// <summary>A game page shows the button only when there is something to play.</summary>
        public static bool HasVideos(GameMetadata? metadata) => metadata?.Trailers?.Length > 0 || metadata?.Gameplays?.Length > 0;

        private async Task Load()
        {
            try
            {
                await uiPlayer.InitVideoPlayer();
                var videos = (metadata.Trailers ?? Array.Empty<string>()).Concat(metadata.Gameplays ?? Array.Empty<string>()).ToList();
                var urls = new List<Tuple<string, string>>();
                // The first one plays as soon as it is ready, the others are added after
                for (int i = 0; i < videos.Count; i++)
                {
                    var url = await PlayableUrl(videos[i]);
                    if (url == null)
                        continue;
                    urls.Add(url);
                    if (urls.Count == 1)
                    {
                        uiPlayer.SetMediaList(urls);
                        await uiPlayer.LoadFirstElement();
                        uiLoading.IsActive = false;
                        uiLoading.IsVisible = false;
                    }
                }
                uiPlayer.SetMediaList(urls);
                if (urls.Count == 0)
                    MainWindowViewModel.Instance.AppBarText = gamevault.Localization.Loc.T("The videos of this game could not be loaded");
            }
            catch (Exception ex) { Log.Ignored(ex); }
            uiLoading.IsActive = false;
            uiLoading.IsVisible = false;
        }

        /// <summary>YouTube links become the addresses of their video and audio streams; other links stay as they are.</summary>
        private static async Task<Tuple<string, string>?> PlayableUrl(string input)
        {
            try
            {
                if (!input.Contains("youtu", StringComparison.OrdinalIgnoreCase))
                    return new Tuple<string, string>(input, "");
                youtube ??= new YoutubeClient();
                var manifest = await youtube.Videos.Streams.GetManifestAsync(input);
                var video = manifest.GetVideoStreams().GetWithHighestVideoQuality();
                var audio = manifest.GetAudioStreams().GetWithHighestBitrate();
                return new Tuple<string, string>(video.Url, audio.Url);
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

        private void Close()
        {
            try { uiPlayer.UnloadMediaSlider(); }
            catch (Exception ex) { Log.Ignored(ex); }
            MainWindowViewModel.Instance.ClosePopup();
        }
    }
}
