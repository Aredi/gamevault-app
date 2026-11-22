using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GameVault.Core;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.UserControls.Mobile
{
    /// <summary>
    /// The phone's games: the retro page's lists and filters (its loading, its view model), as a phone shows them. Unfolded
    /// (a wide screen), a game's page opens beside the grid instead of over it.
    /// </summary>
    public partial class MobileLibraryUserControl : UserControl, IGoesBack
    {
        private static RetroUserControl Retro => MainWindowViewModel.Instance.Retro;
        private RetroViewModel ViewModel => Retro.ViewModel;
        private double cardWidth;
        private bool wide;

        public MobileLibraryUserControl()
        {
            InitializeComponent();
            DataContext = ViewModel;
            BuildListChips();
            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RetroViewModel.Games))
                    ShowGames();
                else if (e.PropertyName == nameof(RetroViewModel.Collection))
                    MarkListChips();
            };
            AttachedToVisualTree += async (_, _) =>
            {
                try { await Retro.EnsureLoadedAsync(); }
                catch (Exception ex) { Log.Ignored(ex); }
                ShowGames();
            };
            SizeChanged += (_, e) => Layout(e.NewSize.Width);
        }

        /// <summary>The lists made by themselves, the hidden games: chips after "Installed" and "Bookmarks".</summary>
        private void BuildListChips()
        {
            foreach (string name in RetroViewModel.SmartCollections)
            {
                var chip = new ToggleButton { Classes = { "mChip" }, Content = name, Tag = name };
                chip.Click += async (_, _) =>
                {
                    int index = ViewModel.Collections.IndexOf(name);
                    ViewModel.Collection = chip.IsChecked == true && index > 0 ? index : 0;
                    MarkListChips();
                    // A list is across the consoles
                    if (ViewModel.ShowsACollection && !ViewModel.ShowsAll)
                        await Retro.SelectAllAsync();
                };
                uiLists.Children.Add(chip);
            }
            var hidden = new ToggleButton { Classes = { "mChip" }, Content = Loc.T("Hidden") };
            hidden.Bind(ToggleButton.IsCheckedProperty, new Avalonia.Data.Binding(nameof(RetroViewModel.OnlyHidden)));
            uiLists.Children.Add(hidden);
        }

        private void MarkListChips()
        {
            string? shown = ViewModel.ShowsACollection ? ViewModel.Collections[ViewModel.Collection] : null;
            foreach (ToggleButton chip in uiLists.Children.OfType<ToggleButton>().Where(c => c.Tag is string))
                chip.IsChecked = (string)chip.Tag! == shown;
        }

        /// <summary>As many columns as fit, three at least on a phone; unfolded, the page beside them.</summary>
        private void Layout(double width)
        {
            if (width <= 0)
                return;
            bool isWide = width >= 760;
            if (isWide != wide)
            {
                wide = isWide;
                uiPanes.ColumnDefinitions = new ColumnDefinitions(wide ? "*,420" : "*,0");
                uiDetail.IsVisible = wide;
                if (wide && uiDetail.Child == null)
                    uiDetail.Child = Placeholder();
            }
            double gridWidth = (wide ? width - 420 : width) - 24;
            int columns = Math.Max(3, (int)(gridWidth / 128));
            double card = Math.Floor(gridWidth / columns) - 10;
            if (Math.Abs(card - cardWidth) < 1)
                return;
            cardWidth = card;
            var layout = (UniformGridLayout)uiGrid.Layout!;
            layout.MinItemWidth = card + 10;
            layout.MinItemHeight = card * 4 / 3 + 34;
            // Recycled: the cards that leave the screen take the games that come into it
            uiGrid.ItemTemplate = new FuncDataTemplate<RetroGame>((_, _) => new MobileCoverCard(cardWidth, 4 / 3.0, Open, ShowSheet), supportsRecycling: true);
            ShowGames();
        }

        private void ShowGames()
        {
            if (cardWidth <= 0)
                return;
            uiGrid.ItemsSource = ViewModel.Games;
            uiCount.Text = ViewModel.CountText;
        }

        private Control Placeholder() => new TextBlock
        {
            Text = Loc.T("Choose a game: its page opens here."), Classes = { "mMuted" }, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(32),
        };

        /// <summary>A game's page: beside the grid when unfolded, else over it.</summary>
        private void Open(RetroGame game)
        {
            CloseSheet();
            if (wide)
                uiDetail.Child = new RetroGameViewUserControl(game);
            else
                RetroUserControl.OpenGame(game);
        }

        private void All_Click(object? sender, RoutedEventArgs e) => _ = Retro.SelectAllAsync();

        private void Platform_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is RetroPlatform platform)
                _ = Retro.TogglePlatformAsync(platform);
        }

        private void Random_Click(object? sender, RoutedEventArgs e)
        {
            var games = ViewModel.Games;
            if (games.Count > 0)
                Open(games[Random.Shared.Next(games.Count)]);
        }

        private void Couch_Click(object? sender, RoutedEventArgs e) => Windows.MainView.Current?.EnterCouch();

        // --- The sheet of a long press ---

        private void ShowSheet(RetroGame game)
        {
            uiSheetCover.Source = game.CoverImage;
            uiSheetTitle.Text = game.Name;
            uiSheetInfo.Text = string.Join(" · ", new[] { game.Platform.Name, game.SizeText, game.Installed ? Loc.T("Installed") : Loc.T("Not installed") }
                .Where(p => !string.IsNullOrEmpty(p)));
            uiSheetActions.Children.Clear();
            if (game.Installed)
                Action("M.Play", Loc.T("Play"), null, () => _ = RetroPlay.PlayAsync(game), primary: true);
            else
            {
                Action("M.Play", Loc.T("Install and play"), Loc.T("Downloaded in the background"), () => _ = RetroPlay.InstallAsync(game, playWhenDone: true), primary: true);
                Action("M.Download", Loc.T("Install"), game.SizeText, () => _ = RetroPlay.InstallAsync(game));
            }
            Action("M.Bookmark", game.IsFavorite ? Loc.T("Remove from the favorites") : Loc.T("Add to the favorites"), null, () => game.IsFavorite = !game.IsFavorite);
            Action("M.Send", Loc.T("Recommend to a friend"), null, () => MainWindowViewModel.Instance.OpenPopup(new RecommendPopup(ChatGame.Of(game, invite: false))));
            Action("M.Hide", game.IsHidden ? Loc.T("Show in the lists again") : Loc.T("Hide from the lists"), null, () => game.IsHidden = !game.IsHidden);
            Action("M.More", Loc.T("Open its page"), null, () => Open(game));
            uiSheetLayer.IsVisible = true;
        }

        private void Action(string icon, string text, string? hint, Action run, bool primary = false)
        {
            var iconBox = new Border { Classes = { "mRowIcon" }, Child = new Avalonia.Controls.Shapes.Path() };
            ((Avalonia.Controls.Shapes.Path)iconBox.Child).Bind(Avalonia.Controls.Shapes.Path.DataProperty, this.GetResourceObservable(icon));
            if (primary)
                iconBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("Brush.Accent"));
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeight.Medium } } };
            if (hint != null)
                words.Children.Add(new TextBlock { Text = hint, Classes = { "mMuted" }, FontSize = 12.5 });
            var row = new Button { Classes = { "mRow" }, Content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, Children = { iconBox, words } } };
            Grid.SetColumn(words, 1);
            row.Click += (_, _) =>
            {
                CloseSheet();
                run();
            };
            uiSheetActions.Children.Add(row);
        }

        private void CloseSheet() => uiSheetLayer.IsVisible = false;

        private void SheetDim_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) => CloseSheet();

        /// <summary>Android's back: the sheet closes first.</summary>
        public bool GoBack()
        {
            if (!uiSheetLayer.IsVisible)
                return false;
            CloseSheet();
            return true;
        }
    }
}
