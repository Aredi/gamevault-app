using Avalonia.Controls;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Couch;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// A game started from the living-room mode: its picture stays still behind it while it starts and runs (nothing
    /// moves or plays under the emulator), and the mode comes back in front when it is closed.
    /// </summary>
    public partial class CouchUserControl
    {
        private CouchItem? playing;
        private readonly Stopwatch playingFor = new();
        private bool playingSeen;
        private int playingChecks;
        private bool checkingPc;

        /// <summary>The game started from here and not closed yet.</summary>
        internal CouchItem? Playing => playing;

        /// <summary>A program of the library game installed in this folder runs (the tests say it instead).</summary>
        internal static Func<int, string, bool> IsRunning { get; set; } = GameTimeTracker.IsRunning;
        /// <summary>A library game never seen running after this long is let go (a launcher that gave up, a game run from elsewhere).</summary>
        internal static TimeSpan GiveUpAfter { get; set; } = TimeSpan.FromSeconds(60);
        /// <summary>The times the window was brought back in front (the tests count them).</summary>
        internal int BroughtBack { get; private set; }

        internal void ListenToGames(bool listen)
        {
            RetroPlay.Started -= RetroStarted;
            RetroPlay.Closed -= RetroClosed;
            if (!listen)
                return;
            RetroPlay.Started += RetroStarted;
            RetroPlay.Closed += RetroClosed;
        }

        private CouchItem? ItemOf(RetroGame game) =>
            all.FirstOrDefault(i => ReferenceEquals(i.Retro, game)) ?? all.FirstOrDefault(i => i.Retro?.Id == game.Id && i.Retro.Platform.Slug == game.Platform.Slug);

        /// <summary>A retro game's emulator is running: "Install and play" ends here too, once the download is over.</summary>
        private void RetroStarted(RetroGame game)
        {
            if (IsVisible && ItemOf(game) is CouchItem item)
                GameStarted(item);
        }

        private void RetroClosed(RetroGame game)
        {
            if (playing?.Retro is RetroGame retro && retro.Id == game.Id && retro.Platform.Slug == game.Platform.Slug)
                GameClosed(bringBack: true);
        }

        /// <summary>The game's picture, still, until it is closed; the trailer and the screenshots stop.</summary>
        internal void GameStarted(CouchItem item)
        {
            playing = item;
            Play(CouchSound.Launch);
            playingFor.Restart();
            playingSeen = false;
            playingChecks = 0;
            StopTrailer();
            string hint = item.Retro != null && item.Retro.Status is { Length: > 0 } status ? status : Loc.T("In game");
            if (Settings.Splash)
                ShowCurtain(Curtain.Splash, item, item.Title, hint);
        }

        /// <summary>Back from the game: its picture goes, the window comes in front with the gamepad listened to.</summary>
        internal void GameClosed(bool bringBack)
        {
            if (playing == null)
                return;
            playing = null;
            playingFor.Reset();
            if (curtain == Curtain.Splash)
                HideCurtain();
            trailerWait.Restart();
            if (bringBack)
                BringToFront();
            // Its time played and its status changed
            if (Navigator.Screen == CouchScreen.Details)
                ShowScreen();
        }

        /// <summary>
        /// Twice a second while a game runs. A library game is not started by us but by its own program: it is over when
        /// none of its programs runs anymore (looked at every 5 seconds, away from the screen's thread). A player who
        /// comes back to the window while the game runs gets the mode back.
        /// </summary>
        internal void WatchGame()
        {
            if (playing == null)
                return;
            bool active = TopLevel.GetTopLevel(this) is Window { IsActive: true };
            if (active && playingFor.Elapsed > TimeSpan.FromSeconds(15) && (playing.Retro != null || playingSeen))
            {
                GameClosed(bringBack: false);
                return;
            }
            if (playing.Pc is not Game game || checkingPc || ++playingChecks % 10 != 0)
                return;
            string? folder = InstallViewModel.Instance.InstalledGames.FirstOrDefault(g => g.Key?.ID == game.ID).Value;
            if (folder == null)
            {
                GameClosed(bringBack: false);
                return;
            }
            checkingPc = true;
            CouchItem item = playing;
            _ = Task.Run(() => IsRunning(game.ID, folder)).ContinueWith(running =>
            {
                checkingPc = false;
                if (!ReferenceEquals(item, playing))
                    return;
                if (!running.IsCompletedSuccessfully)
                    return;
                if (running.Result)
                    playingSeen = true;
                // Seen, then closed: the mode comes back in front
                else if (playingSeen)
                    GameClosed(bringBack: true);
                // Never seen: it may run from elsewhere, so the window is not put over it
                else if (playingFor.Elapsed > GiveUpAfter)
                    GameClosed(bringBack: false);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>The window in front again, the mode's keys and gamepad with it.</summary>
        private void BringToFront()
        {
            if (TopLevel.GetTopLevel(this) is not Window window)
                return;
            BroughtBack++;
            try
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.FullScreen;
                if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is IntPtr handle && handle != IntPtr.Zero)
                    CouchSystem.ToFront(handle);
                window.Activate();
                Focus();
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }
    }
}
