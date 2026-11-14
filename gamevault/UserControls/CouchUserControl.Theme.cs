using Avalonia.Controls;
using gamevault.Helper;
using gamevault.ViewModels;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>The selected game's theme: its music instead of the menu's, a moment after it is chosen, as on a console.</summary>
    public partial class CouchUserControl
    {
        private static readonly TimeSpan ThemeAfter = TimeSpan.FromMilliseconds(1200);

        private CouchPlayer? themePlayer;
        /// <summary>The game whose theme waits or plays.</summary>
        private CouchItem? themeItem;
        private readonly Stopwatch themeWait = new();
        private bool themeStarting;
        // The games without a theme found (or whose sound would not play): not searched again this session
        private readonly HashSet<string> themeless = new();

        /// <summary>The game whose theme was started last, null when none plays.</summary>
        internal string? ThemePlaying { get; private set; }

        /// <summary>The theme is heard: the menu's music is quiet meanwhile.</summary>
        internal bool ThemeHeard => ThemePlaying != null && themePlayer?.Now == CouchPlayer.State.Heard;

        /// <summary>Another game chosen: the theme playing stops, the new one comes if the player stays on it.</summary>
        private void QueueTheme(CouchItem? item)
        {
            CouchItem? game = item is { IsTile: false } ? item : null;
            // The same game (its screen opened, or back from it): its theme goes on
            if (game != null && game.Key == themeItem?.Key)
                return;
            StopTheme();
            themeItem = game;
            themeWait.Restart();
        }

        private bool ThemeMayPlay => themeItem != null && Settings.Themes && !Settings.Performance && SoundsOn && IsVisible
            && !LoginShown && playing == null && curtain == Curtain.None && MainWindowViewModel.Instance.Popup == null
            && !TrailerHeard && TopLevel.GetTopLevel(this) is Window { IsActive: true };

        /// <summary>Twice a second: started after a moment on a game, stopped when a game is played, a trailer heard, the window left.</summary>
        internal void KeepTheme()
        {
            if (!ThemeMayPlay)
            {
                if (ThemePlaying != null)
                    StopTheme();
                return;
            }
            if (ThemePlaying == null)
            {
                if (themeWait.Elapsed >= ThemeAfter && !themeless.Contains(themeItem!.Key))
                    _ = StartThemeAsync(themeItem);
                return;
            }
            _ = WatchThemeAsync();
        }

        private async Task StartThemeAsync(CouchItem item)
        {
            if (themeStarting)
                return;
            themeStarting = true;
            try
            {
                string search = GameThemes.SearchOf(item.Title, item.Retro?.Platform.Name);
                string? sound = await GameThemes.SoundOfAsync(search);
                // Another game was chosen meanwhile, or something came over it
                if (!ReferenceEquals(item, themeItem) || !ThemeMayPlay)
                    return;
                if (sound == null)
                {
                    themeless.Add(item.Key);
                    return;
                }
                if (themePlayer == null)
                {
                    themePlayer = new CouchPlayer();
                    uiThemeHost.Child = themePlayer;
                }
                themePlayer.Play(null, sound, volume: 0.6 * Settings.MusicVolume / 100.0, loop: true);
                ThemePlaying = item.Key;
            }
            catch (Exception ex) { Log.Ignored(ex); }
            finally { themeStarting = false; }
        }

        private async Task WatchThemeAsync()
        {
            if (themePlayer == null || ThemePlaying is not string key)
                return;
            if (await themePlayer.ReadStateAsync() == CouchPlayer.State.Failed && ThemePlaying == key)
            {
                themeless.Add(key);
                StopTheme();
            }
        }

        private void StopTheme()
        {
            if (ThemePlaying == null)
                return;
            ThemePlaying = null;
            themePlayer?.Stop();
        }
    }
}
