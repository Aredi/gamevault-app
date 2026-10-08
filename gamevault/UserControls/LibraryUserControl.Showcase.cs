using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    /// <summary>
    /// The showcase above the games (featured game, continue playing, recently added), the gallery / shelf layouts
    /// and the colors taken from the covers.
    /// </summary>
    public partial class LibraryUserControl
    {
        private const int MaxHeroItems = 5;
        private const int RecentGamesCount = 14;
        private readonly DispatcherTimer heroTimer = new() { Interval = TimeSpan.FromSeconds(9) };
        private bool pointerOverHero;

        private void InitShowcase()
        {
            string? configFile = LoginManager.Instance.GetUserProfile()?.UserConfigFile;
            if (configFile != null)
            {
                if (double.TryParse(Preferences.Get(AppConfigKey.LibraryCardWidth, configFile), NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
                    ViewModel.CardWidth = width;
                ViewModel.IsShelfMode = Preferences.Get(AppConfigKey.LibraryLayout, configFile) == "shelf";
                ViewModel.ShowcaseEnabled = Preferences.Get(AppConfigKey.LibraryShowcase, configFile) != "0";
            }
            UpdateGridLayout();
            ViewModel.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(LibraryViewModel.EffectiveCardWidth):
                    case nameof(LibraryViewModel.IsShelfMode):
                        UpdateGridLayout();
                        break;
                    case nameof(LibraryViewModel.CurrentHero):
                        Dispatcher.UIThread.Post(() => ApplyHeroColor(uiHeroCover.AccentColor));
                        break;
                    case nameof(LibraryViewModel.SelectedGame):
                        if (ViewModel.IsShelfMode)
                            Dispatcher.UIThread.Post(() => ApplyShelfColor(uiShelfCover.AccentColor));
                        break;
                }
            };
            uiCardSize.PropertyChanged += (_, e) =>
            {
                if (e.Property == Slider.ValueProperty)
                    SavePreference(AppConfigKey.LibraryCardWidth, ViewModel.CardWidth.ToString("0", CultureInfo.InvariantCulture));
            };
            uiHeroCover.PropertyChanged += (_, e) =>
            {
                if (e.Property == CacheImage.AccentColorProperty)
                    ApplyHeroColor(uiHeroCover.AccentColor);
            };
            uiShelfCover.PropertyChanged += (_, e) =>
            {
                if (e.Property == CacheImage.AccentColorProperty)
                    ApplyShelfColor(uiShelfCover.AccentColor);
            };

            InstallViewModel installs = InstallViewModel.Instance;
            installs.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InstallViewModel.VisibleInstalledCount))
                    ViewModel.InstalledCount = installs.VisibleInstalledCount;
                else if (e.PropertyName == nameof(InstallViewModel.InstalledGames))
                    ObserveInstalledGames();
            };
            ObserveInstalledGames();

            // The library is created while the main window's view model is built: subscribe once it exists
            Dispatcher.UIThread.Post(() =>
            {
                MainWindowViewModel.Instance.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.IsOffline))
                        ViewModel.IsOffline = MainWindowViewModel.Instance.IsOffline;
                };
                ViewModel.IsOffline = MainWindowViewModel.Instance.IsOffline;
            });

            heroTimer.Tick += (_, _) =>
            {
                if (!pointerOverHero && IsEffectivelyVisible && ViewModel.HeroItems.Count > 1)
                    ViewModel.HeroIndex = (ViewModel.HeroIndex + 1) % ViewModel.HeroItems.Count;
            };
            heroTimer.Start();

            SizeChanged += (_, e) => UpdateWidthDependentLayout(e.NewSize.Width);
            AddHandler(KeyDownEvent, Shelf_KeyDown, RoutingStrategies.Tunnel);
            uiRecentScroll.AddHandler(PointerWheelChangedEvent, HorizontalWheel, RoutingStrategies.Tunnel);
            LibraryData.CollectionsChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
            {
                if (LoginManager.Instance.IsLoggedIn())
                    await LoadCollectionRows();
            });
        }

        private System.Collections.ObjectModel.ObservableCollection<KeyValuePair<Game, string>>? observedInstalls;
        private void ObserveInstalledGames()
        {
            if (observedInstalls != null)
                observedInstalls.CollectionChanged -= InstalledGamesChanged;
            observedInstalls = InstallViewModel.Instance.InstalledGames;
            observedInstalls.CollectionChanged += InstalledGamesChanged;
            InstalledGamesChanged(null, null);
        }

        private void InstalledGamesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs? e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    ViewModel.UpdateCount = InstallViewModel.Instance.InstalledGames.Count(g => g.Key != null && g.Value != null && InstalledGameState.HasUpdate(g.Key, g.Value));
                }
                catch (Exception ex) { Log.Ignored(ex); }
                if (ViewModel.UpdateCount == 0 && ViewModel.OnlyUpdates)
                    ViewModel.OnlyUpdates = false;
                RebuildHero();
            });
        }

        /// <summary>Recently added games and the user's play data, then the banner.</summary>
        private async Task LoadShowcase()
        {
            if (!LoginManager.Instance.IsLoggedIn())
            {
                RebuildHero();
                return;
            }
            try
            {
                InstallViewModel.Instance.SetPlayRecords(await LibraryData.GetMyPlayRecordsAsync());
            }
            catch (Exception ex) { Log.Ignored(ex); }
            try
            {
                string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?sortBy=created_at:DESC&limit={RecentGamesCount}");
                Game[] recent = JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>();
                ViewModel.RecentGames.Clear();
                foreach (Game game in recent.Where(g => g.DeletedAt == null))
                    ViewModel.RecentGames.Add(game);
                ViewModel.RefreshVisibility();
            }
            catch (Exception ex) { Log.Ignored(ex); }
            await LoadCollectionRows();
            RebuildHero();
        }

        private const int MaxCollectionRows = 4;
        /// <summary>One row per collection with games (the fullest first), with the games the server still has.</summary>
        private async Task LoadCollectionRows()
        {
            var rows = new List<CollectionRow>();
            try
            {
                var collections = LibraryData.Collections.Load()
                    .Where(c => c.GameIds.Count > 0)
                    .OrderByDescending(c => c.GameIds.Count)
                    .Take(MaxCollectionRows)
                    .ToList();
                foreach (var collection in collections)
                {
                    string ids = string.Join(',', collection.GameIds.Take(30));
                    string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?filter.id=$in:{ids}&limit=30");
                    var byId = (JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>()).Where(g => g.DeletedAt == null).ToDictionary(g => g.ID);
                    // In the order the games were added to the collection
                    var games = collection.GameIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
                    if (games.Count > 0)
                        rows.Add(new CollectionRow { Name = collection.Name, Count = collection.GameIds.Count, Games = games });
                }
            }
            catch (Exception ex) { Log.Ignored(ex); }
            ViewModel.CollectionRows.Clear();
            foreach (CollectionRow row in rows)
                ViewModel.CollectionRows.Add(row);
            ViewModel.RefreshVisibility();
        }

        /// <summary>"See all": the library filtered on that collection.</summary>
        private void ShowCollection_Click(object? sender, RoutedEventArgs e)
        {
            if (((Control)sender!).DataContext is not CollectionRow row)
                return;
            ViewModel.SelectedCollection = row.Name;
            ViewModel.FilterVisibility = true;
            uiMainScrollBar.ScrollToHome();
        }

        private void Rail_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer rail && rail.Tag == null)
            {
                rail.Tag = "wheel";
                rail.AddHandler(PointerWheelChangedEvent, HorizontalWheel, RoutingStrategies.Tunnel);
            }
        }

        /// <summary>The banner: the installed games last played first, completed with the newest games of the server.</summary>
        private void RebuildHero()
        {
            int? currentId = ViewModel.CurrentHero?.Game.ID;
            var items = new List<HeroItem>();
            var records = InstallViewModel.Instance.PlayRecords;
            foreach (KeyValuePair<Game, string> install in InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null).Take(MaxHeroItems))
            {
                records.TryGetValue(install.Key.ID, out PlayRecord? record);
                bool played = record != null && (record.MinutesPlayed > 0 || record.LastPlayedAt != null);
                string info = played
                    ? record!.LastPlayedAt != null
                        ? Loc.F("{0} played · last session {1}", FormatTime(record.MinutesPlayed), RelativeDate.Format(record.LastPlayedAt.Value))
                        : Loc.F("{0} played", FormatTime(record!.MinutesPlayed))
                    : Loc.T("Installed, not played yet");
                items.Add(new HeroItem
                {
                    Game = install.Key,
                    IsInstalled = true,
                    Eyebrow = (played ? Loc.T("CONTINUE PLAYING") : Loc.T("READY TO PLAY")),
                    Info = info,
                });
            }
            foreach (Game game in ViewModel.RecentGames)
            {
                if (items.Count >= Math.Max(3, Math.Min(MaxHeroItems, items.Count + 2)) || items.Count >= MaxHeroItems)
                    break;
                if (items.Any(i => i.Game.ID == game.ID))
                    continue;
                string info = string.Join(" · ", new[]
                {
                    game.CreatedAt != null ? Loc.F("Added {0}", RelativeDate.Format(game.CreatedAt.Value)) : null,
                    game.Metadata?.Genres?.FirstOrDefault()?.Name,
                    game.Metadata?.ReleaseDate?.Year.ToString(CultureInfo.InvariantCulture),
                }.Where(p => !string.IsNullOrEmpty(p)));
                items.Add(new HeroItem { Game = game, Eyebrow = Loc.T("NEW ON THE SERVER"), Info = info });
            }

            if (items.Select(i => i.Game.ID).SequenceEqual(ViewModel.HeroItems.Select(i => i.Game.ID))
                && items.Select(i => i.Info).SequenceEqual(ViewModel.HeroItems.Select(i => i.Info)))
                return;
            ViewModel.HeroItems.Clear();
            foreach (HeroItem item in items)
                ViewModel.HeroItems.Add(item);
            int index = items.FindIndex(i => i.Game.ID == currentId);
            ViewModel.HeroIndex = index >= 0 ? index : 0;
            ViewModel.HeroItemsChanged();
        }

        private static string FormatTime(int minutes) => (string)new GameTimeConverter().Convert(minutes, typeof(string), null, CultureInfo.CurrentCulture)!;

        private void ApplyHeroColor(Color? color)
        {
            Color accent = color ?? CoverColors.ThemeAccent;
            ViewModel.HeroAccentBrush.Color = accent;
            if (!ViewModel.IsShelfMode && ViewModel.ShowHero)
                ViewModel.AmbientBrush.Color = accent;
        }

        private void ApplyShelfColor(Color? color)
        {
            Color accent = color ?? CoverColors.ThemeAccent;
            ViewModel.ShelfAccentBrush.Color = accent;
            ViewModel.ShelfAccentSoftBrush.Color = accent;
            ViewModel.AmbientBrush.Color = accent;
        }

        private void Hero_PointerEntered(object? sender, PointerEventArgs e) => pointerOverHero = true;
        private void Hero_PointerExited(object? sender, PointerEventArgs e) => pointerOverHero = false;

        private void HeroPrevious_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.HeroItems.Count > 0)
                ViewModel.HeroIndex = (ViewModel.HeroIndex + ViewModel.HeroItems.Count - 1) % ViewModel.HeroItems.Count;
            heroTimer.Stop();
            heroTimer.Start();
        }

        private void HeroNext_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.HeroItems.Count > 0)
                ViewModel.HeroIndex = (ViewModel.HeroIndex + 1) % ViewModel.HeroItems.Count;
            heroTimer.Stop();
            heroTimer.Start();
        }

        private void HeroDetails_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentHero != null)
                OpenGame(ViewModel.CurrentHero.Game);
        }

        /// <summary>Play an installed game, or download it.</summary>
        private async void PrimaryAction_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (((Control)sender!).DataContext is not Game game)
                return;
            await PlayOrDownload((Control)sender, game);
        }

        private async void CardQuickAction_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (((Control)sender!).DataContext is Game game)
                await PlayOrDownload((Control)sender, game);
        }

        private static async Task PlayOrDownload(Control button, Game game)
        {
            button.IsEnabled = false;
            try
            {
                if (InstallViewModel.Instance.InstalledGames.Any(g => g.Key?.ID == game.ID))
                    await InstallUserControl.PlayGame(game.ID);
                else
                    await MainWindowViewModel.Instance.Downloads.TryStartDownload(game);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private void GameCard_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (ViewModel.IsShelfMode)
                return;
            // The top of the page takes the color of the hovered game
            var cover = ((Control)sender!).GetVisualDescendants().OfType<CacheImage>().FirstOrDefault();
            if (cover?.AccentColor is Color color)
                ViewModel.AmbientBrush.Color = color;
        }

        private void ViewGallery_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel.IsShelfMode = false;
            SavePreference(AppConfigKey.LibraryLayout, "gallery");
            ApplyHeroColor(uiHeroCover.AccentColor);
        }

        private void ViewShelf_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel.IsShelfMode = true;
            ViewModel.SelectedGame ??= ViewModel.GameCards.FirstOrDefault();
            SavePreference(AppConfigKey.LibraryLayout, "shelf");
            ApplyShelfColor(uiShelfCover.AccentColor);
        }

        private void Showcase_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel.ShowcaseEnabled = !ViewModel.ShowcaseEnabled;
            SavePreference(AppConfigKey.LibraryShowcase, ViewModel.ShowcaseEnabled ? "1" : "0");
        }

        private void RecentPrevious_Click(object? sender, RoutedEventArgs e) => ScrollRow(uiRecentScroll, -1);
        private void RecentNext_Click(object? sender, RoutedEventArgs e) => ScrollRow(uiRecentScroll, 1);

        private static void ScrollRow(ScrollViewer scroll, int direction)
        {
            double page = Math.Max(166, scroll.Viewport.Width - 166);
            scroll.Offset = new Vector(Math.Clamp(scroll.Offset.X + direction * page, 0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width)), 0);
        }

        /// <summary>The mouse wheel scrolls a row of games sideways, or the page at its ends.</summary>
        private static void HorizontalWheel(object? sender, PointerWheelEventArgs e)
        {
            var scroll = (ScrollViewer)sender!;
            double target = scroll.Offset.X - e.Delta.Y * 120;
            double max = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
            if (max <= 0 || (target < 0 && scroll.Offset.X <= 0) || (target > max && scroll.Offset.X >= max))
                return;
            scroll.Offset = new Vector(Math.Clamp(target, 0, max), 0);
            e.Handled = true;
        }

        /// <summary>Card size of the grid: margins around the cover, the title and info lines below it in the gallery.</summary>
        private UniformGridLayout GamesLayout => (UniformGridLayout)uiServerGamesItemsControl.Layout;

        private void UpdateGridLayout()
        {
            double width = ViewModel.EffectiveCardWidth;
            GamesLayout.MinItemWidth = width + 16;
            GamesLayout.MinItemHeight = width * 1.5 + (ViewModel.IsShelfMode ? 16 : 70);
        }

        private void UpdateWidthDependentLayout(double width)
        {
            ViewModel.IsWide = width >= 1050;
            ViewModel.HeroHeight = width < 900 ? 300 : width < 1400 ? 340 : 380;
            ViewModel.HeroTitleSize = width < 900 ? 32 : width < 1400 ? 40 : 46;
        }

        /// <summary>Shelf: the arrow keys move the selection, Enter opens the game.</summary>
        private void Shelf_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!ViewModel.ShowShelfPanel || e.Source is TextBox || ViewModel.GameCards.Count == 0)
                return;
            int index = ViewModel.SelectedGame == null ? -1 : ViewModel.GameCards.IndexOf(ViewModel.GameCards.FirstOrDefault(g => g?.ID == ViewModel.SelectedGame.ID)!);
            int columns = Math.Max(1, (int)(uiServerGamesItemsControl.Bounds.Width / GamesLayout.MinItemWidth));
            int next = e.Key switch
            {
                Key.Left => index - 1,
                Key.Right => index + 1,
                Key.Up => index - columns,
                Key.Down => index + columns,
                _ => int.MinValue,
            };
            if (e.Key == Key.Enter && ViewModel.SelectedGame != null)
            {
                OpenGame(ViewModel.SelectedGame);
                e.Handled = true;
                return;
            }
            if (next == int.MinValue)
                return;
            e.Handled = true;
            next = Math.Clamp(next, 0, ViewModel.GameCards.Count - 1);
            ViewModel.SelectedGame = ViewModel.GameCards[next];
            uiServerGamesItemsControl.GetOrCreateElement(next).BringIntoView();
        }

        private static void SavePreference(AppConfigKey key, string value)
        {
            string? configFile = LoginManager.Instance.GetUserProfile()?.UserConfigFile;
            if (configFile != null)
                Preferences.Set(key, value, configFile);
        }
    }
}
