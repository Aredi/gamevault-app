using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper;
using gamevault.Helper.Integrations;
using gamevault.Helper.Platform;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// A Switch game's mods on GameBanana, the most downloaded first, or found by words; a mod's file for the game's
    /// version chosen, installed in one click.
    /// </summary>
    public partial class ModsPopup : UserControl
    {
        private readonly RetroGame game;
        private readonly CancellationTokenSource closing = new();
        private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly List<GameBananaMod> mods = new();
        private int? gameId;
        private int page = 1, listing, showing;
        private GameBananaModPage? shown;

        public ModsPopup() : this(new RetroGame()) { }

        internal ModsPopup(RetroGame game)
        {
            InitializeComponent();
            this.game = game;
            uiTitle.Text = Loc.F("Mods for {0}", game.Name);
            searchTimer.Tick += async (_, _) =>
            {
                searchTimer.Stop();
                await ListAsync(reset: true);
            };
            uiSearch.TextChanged += (_, _) =>
            {
                searchTimer.Stop();
                searchTimer.Start();
            };
            uiList.SelectionChanged += async (_, _) =>
            {
                if (uiList.SelectedItem is GameBananaMod mod)
                    await ShowAsync(mod);
            };
            uiFiles.SelectionChanged += (_, _) => DescribeFile();
            AttachedToVisualTree += async (_, _) => await ListAsync(reset: true);
            DetachedFromVisualTree += (_, _) => closing.Cancel();
        }

        /// <summary>The game's mods, a page more or from the first.</summary>
        internal async Task ListAsync(bool reset)
        {
            int mine = ++listing;
            if (reset)
            {
                page = 1;
                mods.Clear();
            }
            uiMore.IsVisible = false;
            try
            {
                gameId ??= await GameBanana.FindGameAsync(game.Name);
                if (mine != listing)
                    return;
                if (gameId is not int id)
                {
                    uiHint.Text = Loc.T("GameBanana has no mods for this game.");
                    uiList.ItemsSource = null;
                    return;
                }
                var (found, more) = await GameBanana.ModsAsync(id, uiSearch.Text, page);
                if (mine != listing)
                    return;
                var installed = SwitchModsService.Installed(game).Select(m => m.ModId).OfType<int>().ToHashSet();
                foreach (GameBananaMod mod in found.Where(f => mods.All(m => m.Id != f.Id)))
                {
                    mod.Installed = installed.Contains(mod.Id);
                    mods.Add(mod);
                }
                uiList.ItemsSource = mods.ToList();
                uiMore.IsVisible = more;
                if (mods.Count == 0)
                    uiHint.Text = Loc.T("No mod found.");
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                if (mine == listing)
                    uiHint.Text = Loc.T("GameBanana could not be reached. Try again later.");
            }
        }

        private async void More_Click(object? sender, RoutedEventArgs e)
        {
            page++;
            await ListAsync(reset: false);
        }

        /// <summary>A mod's description and its files, the one for the game's version chosen.</summary>
        internal async Task ShowAsync(GameBananaMod mod)
        {
            int mine = ++showing;
            uiHint.IsVisible = false;
            uiDetail.IsVisible = true;
            uiName.Text = mod.Name;
            uiDescription.Text = "";
            uiFiles.ItemsSource = null;
            uiFileNote.Text = "";
            uiStatus.Text = Loc.T("Loading...");
            uiInstall.IsEnabled = false;
            try
            {
                GameBananaModPage? page = await GameBanana.ModAsync(mod.Id);
                if (mine != showing)
                    return;
                shown = page;
                uiDescription.Text = page?.Description ?? "";
                var files = page?.Files ?? new();
                uiFiles.ItemsSource = files;
                uiStatus.Text = files.Count == 0 ? Loc.T("No file of this mod can be installed (none passed GameBanana's antivirus).") : "";
                if (files.Count > 0)
                {
                    int best = SwitchMods.BestFile(files.Select(f => f.AsModFile).ToList(), SwitchModsService.VersionOf(game))!.Id;
                    uiFiles.SelectedItem = files.First(f => f.Id == best);
                }
                uiInstall.Content = Loc.T(mod.Installed ? "Install again" : "Install");
                uiInstall.IsEnabled = Installable;
                if (game.Busy)
                    uiStatus.Text = Loc.T("The mods change once the game is closed.");
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                if (mine == showing)
                    uiStatus.Text = Loc.T("GameBanana could not be reached. Try again later.");
            }
        }

        private void DescribeFile()
        {
            if (uiFiles.SelectedItem is not GameBananaFile file)
            {
                uiFileNote.Text = "";
                return;
            }
            string? version = SwitchModsService.VersionOf(game);
            uiInstall.IsEnabled = Installable;
            if (SwitchMods.MadeForAnother(file.AsModFile, version) is string other)
            {
                uiFileNote.Text = Loc.F("This file is made for version {0} of the game and yours is in {1}: the game would no longer start. Look on the mod's page for a file for {1}.", other, version!);
                return;
            }
            var notes = new List<string>();
            if (version != null)
                notes.Add(SwitchMods.Names(file.Name + " " + file.Description, version)
                    ? Loc.F("Made for your version of the game ({0}).", version)
                    : Loc.F("Your game is in version {0}: check on the mod's page that this file works with it.", version));
            if (SwitchMods.NeedsAnotherManager(file.AsModFile))
                notes.Add(Loc.T("This file is made for another mod manager: it may not work."));
            uiFileNote.Text = string.Join(" ", notes);
        }

        /// <summary>A file chosen, the game closed, and the file not made for another version of the game.</summary>
        private bool Installable => shown != null && uiFiles.SelectedItem is GameBananaFile file && !game.Busy
            && SwitchMods.MadeForAnother(file.AsModFile, SwitchModsService.VersionOf(game)) == null;

        private async void Install_Click(object? sender, RoutedEventArgs e) => await InstallAsync();

        internal async Task InstallAsync()
        {
            if (!Installable || uiFiles.SelectedItem is not GameBananaFile file)
                return;
            uiInstall.IsEnabled = uiFiles.IsEnabled = false;
            uiProgress.IsVisible = true;
            uiProgress.Value = 0;
            uiStatus.Text = Loc.T("Downloading...");
            try
            {
                await SwitchModsService.InstallAsync(game, shown, file, new Progress<double>(p => uiProgress.Value = p), closing.Token);
                uiStatus.Text = Loc.T("Installed: it is on the next time the game starts.");
                foreach (GameBananaMod mod in mods.Where(m => m.Id == shown.Id))
                    mod.Installed = true;
                uiList.ItemsSource = mods.ToList();
                MainWindowViewModel.Instance.AppBarText = Loc.F("{0} installed for {1}", shown.Name, game.Name);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                uiStatus.Text = ex.Message;
            }
            finally
            {
                uiProgress.IsVisible = false;
                uiFiles.IsEnabled = true;
                uiInstall.IsEnabled = Installable;
            }
        }

        private void Page_Click(object? sender, RoutedEventArgs e)
        {
            if (shown?.Url is string url)
                PlatformInfo.OpenUrl(url);
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => MainWindowViewModel.Instance.ClosePopup();
    }
}
