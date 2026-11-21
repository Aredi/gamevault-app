using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Library;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// "Continue playing" with the retro games installed here and already played, between the PC games by their last
    /// session: one row to start again whatever was played last.
    /// </summary>
    public partial class InstallUserControl
    {
        /// <summary>The retro games installed here with a session, the same objects from one look on the disk to the next.</summary>
        private List<RetroGame> retroPlayed = new();
        private int retroLook;
        private string? playsAskedFor;

        /// <summary>Once: the library is made once for the client's life (each sign-in's window shows the same).</summary>
        private void WatchRetroGames()
        {
            RetroLibrary.Changed += RetroLibraryChanged;
            // A game installed then played comes with its session; one deleted leaves at the next look
            RetroPlay.Closed += RetroGameClosed;
            _ = LookForRetroGamesAsync(loadPlays: true);
        }

        private void RetroLibraryChanged() => Dispatcher.UIThread.Post(() => _ = LookForRetroGamesAsync());
        private void RetroGameClosed(RetroGame game) => _ = LookForRetroGamesAsync();

        private async Task LookForRetroGamesAsync(bool loadPlays = false)
        {
            int look = ++retroLook;
            List<RetroGame> installed;
            try { installed = await Task.Run(RetroLibrary.InstalledGames); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return;
            }
            if (look != retroLook)
                return;
            var known = retroPlayed.ToDictionary(RetroLibrary.Key);
            retroPlayed = installed.Where(g => RetroLibrary.LastPlayedOf(g) != null)
                .Select(g => known.TryGetValue(RetroLibrary.Key(g), out RetroGame? same) ? same : g).ToList();
            foreach (RetroGame game in retroPlayed)
                game.RefreshPlayer();
            RebuildContinue();
            // Their sessions are on the service: asked once per account, when this PC has retro games (Changed comes
            // back here); the retro pages ask again for theirs
            string account = $"{SettingsViewModel.Instance.ServerUrl}#{LoginManager.Instance.GetCurrentUser()?.ID}";
            if (loadPlays && installed.Count > 0 && LoginManager.Instance.IsLoggedIn() && playsAskedFor != account)
            {
                playsAskedFor = account;
                _ = RetroLibrary.LoadAsync();
            }
        }

        private bool continueScheduled;
        /// <summary>Once for all the changes in a row: the search empties the list and fills it again, game by game.</summary>
        private void ScheduleContinue()
        {
            if (continueScheduled)
                return;
            continueScheduled = true;
            Dispatcher.UIThread.Post(RebuildContinue);
        }

        private void RebuildContinue()
        {
            continueScheduled = false;
            var records = InstallViewModel.Instance.PlayRecords;
            var pc = (InstallViewModel.Instance.InstalledGamesFilter ?? (IEnumerable<KeyValuePair<Game, string>>)Array.Empty<KeyValuePair<Game, string>>())
                .Select(g => ((object)g, g.Key != null && records.TryGetValue(g.Key.ID, out PlayRecord? record) ? record.LastPlayedAt : null))
                .ToList();
            // Its session can be gone since the look on the disk (the plays loaded again meanwhile)
            var retro = retroPlayed.Where(g => searchText.Length == 0 || g.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .Select(g => (Game: g, Played: RetroLibrary.LastPlayedOf(g)))
                .Where(g => g.Played != null)
                .Select(g => ((object)g.Game, g.Played!.Value));
            Sync(InstallViewModel.Instance.ContinueItems, ContinuePlaying.Merge(pc, retro));
            // Only a real change: the page lays itself out again on it
            if (InstallViewModel.Instance.VisibleInstalledCount != InstallViewModel.Instance.ContinueItems.Count)
                InstallViewModel.Instance.VisibleInstalledCount = InstallViewModel.Instance.ContinueItems.Count;
        }

        /// <summary>Only what changed: the cards already shown are not made again (their images, their focus).</summary>
        private static void Sync(ObservableCollection<object> shown, IReadOnlyList<object> wanted)
        {
            var keep = new HashSet<object>(wanted);
            for (int i = shown.Count - 1; i >= 0; i--)
                if (!keep.Contains(shown[i]))
                    shown.RemoveAt(i);
            for (int i = 0; i < wanted.Count; i++)
            {
                int at = shown.IndexOf(wanted[i]);
                if (at == i)
                    continue;
                if (at < 0)
                    shown.Insert(i, wanted[i]);
                else
                    shown.Move(at, i);
            }
        }

        private void RetroCard_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is RetroGame game)
                RetroUserControl.OpenGame(game);
        }

        private async void RetroPlay_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if ((sender as Control)?.DataContext is RetroGame game)
                await RetroPlay.PlayAsync(game);
        }
    }

    /// <summary>The card of "Continue playing": a retro game's, or a PC game's.</summary>
    internal sealed class ContinueCardTemplate : IDataTemplate
    {
        public IDataTemplate? Retro { get; set; }
        public IDataTemplate? Pc { get; set; }
        public Control? Build(object? param) => (param is RetroGame ? Retro : Pc)?.Build(param);
        public bool Match(object? data) => true;
    }
}
