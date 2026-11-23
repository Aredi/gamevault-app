using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace gamevault.UserControls
{
    internal sealed record ModRow(SwitchMods.InstalledMod Mod)
    {
        public string Name => Mod.Folder;
        public bool Enabled => Mod.Enabled;
    }

    /// <summary>
    /// A Switch game's mods on its page, when it is installed and Eden (or a fork) plays it: on or off for its next
    /// start, found on GameBanana. Nothing changes while the game runs.
    /// </summary>
    public partial class SwitchModsPanel : UserControl
    {
        private RetroGame? game;

        public SwitchModsPanel()
        {
            InitializeComponent();
            void Refresh(RetroGame changed)
            {
                if (changed == game)
                    Dispatcher.UIThread.Post(Show);
            }
            // The game lives the whole session, a page is made at each visit: listened to while shown only
            AttachedToVisualTree += (_, _) =>
            {
                SwitchModsService.Changed += Refresh;
                if (game != null)
                    game.PropertyChanged += Game_PropertyChanged;
                Show();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                SwitchModsService.Changed -= Refresh;
                if (game != null)
                    game.PropertyChanged -= Game_PropertyChanged;
            };
        }

        internal RetroGame? Game
        {
            get => game;
            set
            {
                bool shown = VisualRoot != null;
                if (game != null && shown)
                    game.PropertyChanged -= Game_PropertyChanged;
                game = value;
                if (game != null && shown)
                    game.PropertyChanged += Game_PropertyChanged;
                Show();
            }
        }

        private void Game_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RetroGame.Busy) or nameof(RetroGame.Installed))
                Dispatcher.UIThread.Post(Show);
        }

        internal void Show()
        {
            IsVisible = game is { Installed: true } && SwitchModsService.EmulatorOf(game) != null;
            if (!IsVisible || game == null)
                return;
            try
            {
                var mods = SwitchModsService.Installed(game);
                uiMods.ItemsSource = mods.Select(m => new ModRow(m)).ToList();
                uiEmpty.IsVisible = mods.Count == 0;
                int on = mods.Count(m => m.Enabled);
                uiCount.Text = mods.Count == 0 ? "" : Loc.F("{0} of {1} on", on, mods.Count);
                uiAllOff.IsVisible = on > 0;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                uiStatus.Text = ex.Message;
            }
            // While the game runs, Eden has its mods open
            bool playing = game.Busy;
            uiMods.IsEnabled = uiFind.IsEnabled = uiAllOff.IsEnabled = !playing;
            uiStatus.Text = playing ? Loc.T("The mods change once the game is closed.") : "";
        }

        private void Find_Click(object? sender, RoutedEventArgs e)
        {
            if (game != null)
                MainWindowViewModel.Instance.OpenPopup(new ModsPopup(game));
        }

        private void Toggle_Click(object? sender, RoutedEventArgs e)
        {
            if (game == null || sender is not ToggleSwitch { DataContext: ModRow row } toggle)
                return;
            Change(() => SwitchModsService.SetEnabled(game, row.Mod.Folder, toggle.IsChecked == true));
        }

        private void Remove_Click(object? sender, RoutedEventArgs e)
        {
            if (game != null && sender is Control { DataContext: ModRow row })
                Change(() => SwitchModsService.Remove(game, row.Mod.Folder));
        }

        private void AllOff_Click(object? sender, RoutedEventArgs e)
        {
            if (game == null)
                return;
            RetroGame shown = game;
            Change(() =>
            {
                foreach (var mod in SwitchModsService.Installed(shown).Where(m => m.Enabled))
                    SwitchModsService.SetEnabled(shown, mod.Folder, false);
            });
        }

        private void Change(Action change)
        {
            try { change(); }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                MainWindowViewModel.Instance.AppBarText = ex.Message;
            }
            Show();
        }
    }
}
