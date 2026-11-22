using Avalonia.Controls;
using gamevault.Helper;
using gamevault.ViewModels;
using GameVault.Core;
using System;
using System.Collections.Generic;
using System.Linq;
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
            // Its sound's address asked at once: ready when the moment has passed
            if (game != null && Settings.Themes && !Settings.Performance && !themeless.Contains(game.Key))
                _ = GameThemes.SoundOfAsync(SearchOf(game));
        }

        private static string SearchOf(CouchItem item) => GameThemes.SearchOf(item.Title, item.Retro?.Platform.Name);

        /// <summary>
        /// At start, in the background, a while after the window: every game's theme searched for once and kept on the PC
        /// (the installed ones first, then the ones played last), so none is searched for when it is chosen.
        /// </summary>
        internal async Task PreloadThemesAsync(TimeSpan after)
        {
            try
            {
                await Task.Delay(after);
                if (!Settings.Themes || Settings.Performance)
                    return;
                var (pc, retro) = await ReadGamesAsync();
                HashSet<int> installed = InstalledPc();
                var records = InstallViewModel.Instance.PlayRecords;
                var items = pc.Select(g => new CouchItem { Pc = g, Installed = installed.Contains(g.ID) })
                    .Concat(retro.Select(g => new CouchItem { Retro = g, Installed = g.Installed }));
                DateTime LastPlayed(CouchItem i) => i.Pc != null && records.TryGetValue(i.Pc.ID, out var record) && record.LastPlayedAt is DateTime at ? at
                    : i.Retro != null && RetroLibrary.LastPlayedOf(i.Retro) is DateTime retroAt ? retroAt : DateTime.MinValue;
                var order = items.OrderByDescending(i => i.Installed).ThenByDescending(LastPlayed).Select(SearchOf).ToList();
                await GameThemes.PreloadAsync(order);
            }
            catch (Exception ex) { Log.Ignored(ex); }
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
                string? sound = await GameThemes.SoundOfAsync(SearchOf(item));
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
