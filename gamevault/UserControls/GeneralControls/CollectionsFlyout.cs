using gamevault.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GameVault.Core;
using gamevault.Helper;
using gamevault.ViewModels;
using System;
using System.Linq;

namespace gamevault.UserControls
{
    /// <summary>
    /// Pop-ups for the personal collections: add/remove a game, and create/rename/delete collections.
    /// </summary>
    internal static class CollectionsFlyout
    {
        /// <summary>Check boxes of all collections for one game, plus a field to create a new one.</summary>
        /// <param name="store">The retro games have their own collections; the library's by default.</param>
        /// <param name="changed">Told when a collection was created or a game added or removed.</param>
        public static void ShowForGame(Control anchor, int gameId, string gameTitle, GameVault.Core.Library.GameCollectionStore? store = null, Action? changed = null)
        {
            GameVault.Core.Library.GameCollectionStore Store() => store ?? LibraryData.Collections;
            var panel = new StackPanel { Spacing = 6, Width = 280 };
            var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };

            void Fill()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock { Text = Loc.F("Collections of {0}", gameTitle), FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
                var collections = Store().Load().OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (collections.Count == 0)
                    panel.Children.Add(new TextBlock { Text = Loc.T("No collection yet."), Opacity = 0.7 });
                foreach (var collection in collections)
                {
                    var box = new CheckBox { Content = collection.Name, IsChecked = collection.GameIds.Contains(gameId) };
                    box.IsCheckedChanged += (_, _) =>
                    {
                        Run(() => Store().SetMembership(collection.Name, gameId, box.IsChecked == true));
                        changed?.Invoke();
                    };
                    panel.Children.Add(box);
                }
                panel.Children.Add(NewCollectionRow(name =>
                {
                    var collectionStore = Store();
                    collectionStore.Create(name);
                    collectionStore.SetMembership(name, gameId, true);
                    changed?.Invoke();
                    Fill();
                }));
            }

            Fill();
            flyout.ShowAt(anchor);
        }

        /// <summary>Rename (edit the name, Enter) or delete collections, or create new ones.</summary>
        public static void ShowManager(Control anchor)
        {
            var panel = new StackPanel { Spacing = 6, Width = 360 };
            var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };

            void Fill()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock { Text = Loc.T("Collections"), FontWeight = FontWeight.Bold });
                panel.Children.Add(new TextBlock { Text = Loc.T("Add games from their page (collection button next to the bookmark). Edit a name and press Enter to rename."), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
                var collections = LibraryData.Collections.Load().OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (var collection in collections)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
                    var name = new TextBox { Text = collection.Name };
                    void Rename()
                    {
                        if (!string.IsNullOrWhiteSpace(name.Text) && name.Text.Trim() != collection.Name)
                            Run(() => LibraryData.Collections.Rename(collection.Name, name.Text!));
                    }
                    name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Rename(); Fill(); } };
                    name.LostFocus += (_, _) => Rename();
                    var count = new TextBlock { Text = Loc.F("{0} game(s)", collection.GameIds.Count), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.7 };
                    var delete = new IconButton { Text = Loc.T("Delete"), Kind = ButtonKind.Danger, Height = 28, Width = 70, FontSize = 12 };
                    delete.Click += async (_, _) =>
                    {
                        if (await DialogService.ConfirmAsync(Loc.F("Delete the collection '{0}'? The games stay in the library.", collection.Name), "Collections"))
                        {
                            Run(() => LibraryData.Collections.Delete(collection.Name));
                            Fill();
                        }
                    };
                    Grid.SetColumn(count, 1);
                    Grid.SetColumn(delete, 2);
                    row.Children.Add(name);
                    row.Children.Add(count);
                    row.Children.Add(delete);
                    panel.Children.Add(row);
                }
                panel.Children.Add(NewCollectionRow(created =>
                {
                    LibraryData.Collections.Create(created);
                    Fill();
                }));
            }

            Fill();
            flyout.ShowAt(anchor);
        }

        private static Control NewCollectionRow(Action<string> create)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            var name = new TextBox { Watermark = Loc.T("New collection") };
            var add = new IconButton { Text = Loc.T("Create"), Height = 30, Width = 80, FontSize = 13 };
            void Create()
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                    return;
                Run(() => create(name.Text!));
            }
            add.Click += (_, _) => Create();
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Create(); };
            Grid.SetColumn(add, 1);
            row.Children.Add(name);
            row.Children.Add(add);
            return row;
        }

        private static void Run(Action action)
        {
            try
            {
                action();
                LibraryData.NotifyCollectionsChanged();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                MainWindowViewModel.Instance.AppBarText = WebExceptionHelper.TryGetServerMessage(ex);
            }
        }
    }
}
