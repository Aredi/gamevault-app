using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using gamevault.Converter;
using gamevault.Helper;
using gamevault.Localization;
using gamevault.Models;
using gamevault.ViewModels;
using GameVault.Core;
using GameVault.Core.Input;
using GameVault.Core.Library;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.UserControls
{
    internal sealed class LivingTile : ViewModelBase
    {
        public Game Game { get; init; } = null!;
        public bool IsInstalled { get; init; }
        private bool isFocused;
        public bool IsFocused
        {
            get => isFocused;
            set { isFocused = value; OnPropertyChanged(); }
        }
    }

    internal sealed class LivingRow : ViewModelBase
    {
        public string Title { get; init; } = "";
        public ObservableCollection<LivingTile> Tiles { get; } = new();
        private bool isCurrent;
        public bool IsCurrent
        {
            get => isCurrent;
            set { isCurrent = value; OnPropertyChanged(); }
        }
        /// <summary>The tile the row comes back to.</summary>
        public int Position { get; set; }
    }

    internal sealed class LivingRoomViewModel : ViewModelBase
    {
        public ObservableCollection<LivingRow> Rows { get; } = new();
        private Game? focusedGame;
        public Game? FocusedGame { get => focusedGame; set { focusedGame = value; OnPropertyChanged(); } }
        private string focusedTitle = "", focusedEyebrow = "", focusedInfo = "", focusedDescription = "", confirmHint = "";
        public string FocusedTitle { get => focusedTitle; set { focusedTitle = value; OnPropertyChanged(); } }
        public string FocusedEyebrow { get => focusedEyebrow; set { focusedEyebrow = value; OnPropertyChanged(); } }
        public string FocusedInfo { get => focusedInfo; set { focusedInfo = value; OnPropertyChanged(); } }
        public string FocusedDescription { get => focusedDescription; set { focusedDescription = value; OnPropertyChanged(); } }
        /// <summary>"Play" or "Install", for the A button.</summary>
        public string ConfirmHint { get => confirmHint; set { confirmHint = value; OnPropertyChanged(); } }
        private string clock = "";
        public string Clock { get => clock; set { clock = value; OnPropertyChanged(); } }
        private bool gamepadConnected;
        public bool GamepadConnected { get => gamepadConnected; set { gamepadConnected = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// The living room mode: full screen, large covers in rows, made for a TV and a gamepad (the keyboard works too:
    /// arrows, Enter, I for the game page, Escape to leave).
    /// </summary>
    public partial class LivingRoomUserControl : UserControl
    {
        private readonly LivingRoomViewModel model = new();
        private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(15) };
        private int row, column;
        /// <summary>Raised when the user leaves the mode (B, Escape).</summary>
        public event EventHandler? CloseRequested;

        public LivingRoomUserControl()
        {
            InitializeComponent();
            DataContext = model;
            clockTimer.Tick += (_, _) => model.Clock = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
            AddHandler(KeyDownEvent, Keyboard_KeyDown, RoutingStrategies.Tunnel);
        }

        public bool GamepadConnected
        {
            set => model.GamepadConnected = value;
        }

        public async Task OpenAsync()
        {
            model.Clock = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
            clockTimer.Start();
            await LoadRows();
            row = column = 0;
            ApplyFocus();
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
        }

        public void Closed() => clockTimer.Stop();

        /// <summary>Continue playing, recently added, the collections, then the whole library by title.</summary>
        private async Task LoadRows()
        {
            model.Rows.Clear();
            var installedIds = InstallViewModel.Instance.InstalledGames.Where(g => g.Key != null).Select(g => g.Key.ID).ToHashSet();
            void AddRow(string title, IEnumerable<Game> games)
            {
                var row = new LivingRow { Title = title };
                foreach (Game game in games.Where(g => g != null).DistinctBy(g => g.ID))
                    row.Tiles.Add(new LivingTile { Game = game, IsInstalled = installedIds.Contains(game.ID) });
                if (row.Tiles.Count > 0)
                    model.Rows.Add(row);
            }
            AddRow(Loc.T("Continue playing"), InstallViewModel.Instance.InstalledGames.Select(g => g.Key));
            LibraryViewModel library = MainWindowViewModel.Instance.Library.Model;
            AddRow(Loc.T("Recently added"), library.RecentGames);
            foreach (CollectionRow collection in library.CollectionRows)
                AddRow(collection.Name, collection.Games);
            if (LoginManager.Instance.IsLoggedIn())
            {
                try
                {
                    string json = await WebHelper.GetAsync($"{SettingsViewModel.Instance.ServerUrl}/api/games?sortBy=sort_title:ASC&limit=80");
                    AddRow(Loc.T("All games"), (JsonSerializer.Deserialize<PaginatedData<Game>>(json)?.Data ?? Array.Empty<Game>()).Where(g => g.DeletedAt == null));
                }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }

        public void Handle(PadAction action)
        {
            if (model.Rows.Count == 0)
            {
                if (action is PadAction.Back or PadAction.Menu)
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            switch (action)
            {
                case PadAction.Left:
                    column = Math.Max(0, column - 1);
                    break;
                case PadAction.Right:
                    column = Math.Min(model.Rows[row].Tiles.Count - 1, column + 1);
                    break;
                case PadAction.Up:
                case PadAction.PreviousRow:
                    ChangeRow(row - 1);
                    break;
                case PadAction.Down:
                case PadAction.NextRow:
                    ChangeRow(row + 1);
                    break;
                case PadAction.Confirm:
                    _ = PlayOrInstall();
                    return;
                case PadAction.Details:
                    if (Focused() is LivingTile tile)
                    {
                        CloseRequested?.Invoke(this, EventArgs.Empty);
                        MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(tile.Game));
                    }
                    return;
                case PadAction.Back:
                case PadAction.Menu:
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    return;
                default:
                    return;
            }
            ApplyFocus();
        }

        private void ChangeRow(int target)
        {
            target = Math.Clamp(target, 0, model.Rows.Count - 1);
            if (target == row)
                return;
            model.Rows[row].Position = column;
            row = target;
            column = Math.Min(model.Rows[row].Position, model.Rows[row].Tiles.Count - 1);
        }

        private LivingTile? Focused() => row < model.Rows.Count && column < model.Rows[row].Tiles.Count ? model.Rows[row].Tiles[column] : null;

        private void ApplyFocus()
        {
            for (int r = 0; r < model.Rows.Count; r++)
            {
                model.Rows[r].IsCurrent = r == row;
                for (int c = 0; c < model.Rows[r].Tiles.Count; c++)
                    model.Rows[r].Tiles[c].IsFocused = r == row && c == column;
            }
            LivingTile? tile = Focused();
            if (tile == null)
                return;
            Game game = tile.Game;
            model.FocusedGame = game;
            model.FocusedTitle = game.Metadata?.Title is { Length: > 0 } title && SettingsViewModel.Instance.ShowMappedTitle ? title : game.Title;
            model.FocusedEyebrow = model.Rows[row].Title.ToUpper(CultureInfo.CurrentCulture);
            var parts = new List<string?> { game.Metadata?.Genres?.FirstOrDefault()?.Name, game.Metadata?.ReleaseDate?.Year.ToString(CultureInfo.InvariantCulture) };
            if (tile.IsInstalled && InstallViewModel.Instance.PlayRecords.TryGetValue(game.ID, out PlayRecord? record) && record.MinutesPlayed > 0)
                parts.Add(Loc.F("{0} played", (string)new GameTimeConverter().Convert(record.MinutesPlayed, typeof(string), null, CultureInfo.CurrentCulture)!));
            else if (!tile.IsInstalled && !string.IsNullOrEmpty(game.Size))
                parts.Add((string)new GameSizeConverter().Convert(game.Size, typeof(string), null, CultureInfo.CurrentCulture)!);
            model.FocusedInfo = string.Join("   ·   ", parts.Where(p => !string.IsNullOrEmpty(p)));
            model.FocusedDescription = (string)new PlainTextConverter().Convert(game.Metadata?.Description, typeof(string), null, CultureInfo.CurrentCulture)!;
            model.ConfirmHint = tile.IsInstalled ? Loc.T("Play") : Loc.T("Install");
            Dispatcher.UIThread.Post(BringFocusedIntoView, DispatcherPriority.Background);
        }

        private void BringFocusedIntoView()
        {
            if (uiRows.ContainerFromIndex(row) is not Control rowContainer)
                return;
            var tiles = rowContainer.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();
            if (tiles?.ContainerFromIndex(column) is Control tileContainer)
                tileContainer.BringIntoView(new Avalonia.Rect(-120, -40, tileContainer.Bounds.Width + 240, tileContainer.Bounds.Height + 80));
            rowContainer.BringIntoView();
        }

        private async Task PlayOrInstall()
        {
            if (Focused() is not LivingTile tile)
                return;
            if (tile.IsInstalled)
            {
                await InstallUserControl.PlayGame(tile.Game.ID);
            }
            else
            {
                await MainWindowViewModel.Instance.Downloads.TryStartDownload(tile.Game);
            }
        }

        private void Keyboard_KeyDown(object? sender, KeyEventArgs e)
        {
            PadAction? action = e.Key switch
            {
                Key.Left => PadAction.Left,
                Key.Right => PadAction.Right,
                Key.Up => PadAction.Up,
                Key.Down => PadAction.Down,
                Key.Enter or Key.Space => PadAction.Confirm,
                Key.I => PadAction.Details,
                Key.Escape or Key.Back => PadAction.Back,
                Key.PageUp => PadAction.PreviousRow,
                Key.PageDown => PadAction.NextRow,
                _ => null,
            };
            if (action == null)
                return;
            e.Handled = true;
            Handle(action.Value);
        }

        private void Tile_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (((Control)sender!).DataContext is not LivingTile tile)
                return;
            for (int r = 0; r < model.Rows.Count; r++)
            {
                int c = model.Rows[r].Tiles.IndexOf(tile);
                if (c < 0)
                    continue;
                bool again = r == row && c == column;
                row = r;
                column = c;
                ApplyFocus();
                // A second click on the focused tile plays (or installs) it
                if (again)
                    _ = PlayOrInstall();
                return;
            }
        }
    }
}
