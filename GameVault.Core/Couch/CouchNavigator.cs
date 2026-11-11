namespace GameVault.Core.Couch
{
    /// <summary>What the living-room mode shows.</summary>
    public enum CouchScreen { Home, Details, Menu }

    /// <summary>How the games of a tab are laid: a strip of covers, rows of covers, or a list of names.</summary>
    public enum CouchView { Strip, Grid, List }

    /// <summary>What a command did: what the screen has to do about it.</summary>
    public enum CouchEffect
    {
        None,
        /// <summary>Another game, another tab or another row of the screen is selected.</summary>
        Moved,
        /// <summary>Another screen is shown.</summary>
        ScreenChanged,
        /// <summary>The strip became the grid, or the grid the strip.</summary>
        ViewChanged,
        /// <summary>The selected action of the game's screen, or the selected line of the menu, is asked.</summary>
        Action,
    }

    /// <summary>
    /// Where the player is in the living-room mode, moved by the gamepad's commands: tabs of games shown as a strip
    /// or a grid, the screen of a game with its actions, and the menu. It knows the games by their titles only.
    /// </summary>
    public sealed class CouchNavigator
    {
        private IReadOnlyList<IReadOnlyList<string>> tabs = Array.Empty<IReadOnlyList<string>>();
        private int[] selected = Array.Empty<int>();

        public CouchScreen Screen { get; private set; } = CouchScreen.Home;
        public int Tab { get; private set; }
        public CouchView View { get; private set; }
        public bool IsGrid => View == CouchView.Grid;
        public bool IsList => View == CouchView.List;
        /// <summary>The games of a row of the grid, as the screen lays them.</summary>
        public int Columns { get; set; } = 6;
        /// <summary>The actions of the game's screen (Play, the game's page...) and the lines of the menu.</summary>
        public int ActionCount { get; set; } = 1;
        public int MenuCount { get; set; } = 1;
        public int Action { get; private set; }
        public int MenuLine { get; private set; }

        public int TabCount => tabs.Count;
        public int Count => Tab < tabs.Count ? tabs[Tab].Count : 0;
        /// <summary>The selected game of the tab shown; -1 in an empty tab.</summary>
        public int Index => Count == 0 ? -1 : Math.Clamp(selected[Tab], 0, Count - 1);

        /// <summary>The games' titles by tab, in the order shown. Each tab keeps its selected game when it still has it.</summary>
        public void SetTabs(IReadOnlyList<IReadOnlyList<string>> titles)
        {
            int[] kept = new int[titles.Count];
            for (int i = 0; i < kept.Length && i < selected.Length; i++)
                kept[i] = selected[i];
            tabs = titles;
            selected = kept;
            Tab = Math.Clamp(Tab, 0, Math.Max(0, tabs.Count - 1));
            if (Screen == CouchScreen.Details && Index < 0)
                Screen = CouchScreen.Home;
        }

        public void Select(int tab, int index)
        {
            if (tab < 0 || tab >= tabs.Count)
                return;
            Tab = tab;
            selected[tab] = Math.Clamp(index, 0, Math.Max(0, tabs[tab].Count - 1));
        }

        public CouchEffect Apply(CouchCommand command) => Screen switch
        {
            CouchScreen.Menu => InMenu(command),
            CouchScreen.Details => InDetails(command),
            _ => AtHome(command),
        };

        private CouchEffect AtHome(CouchCommand command)
        {
            switch (command)
            {
                // The list goes up and down only
                case CouchCommand.Left: return IsList ? CouchEffect.None : Move(-1, sameRow: IsGrid);
                case CouchCommand.Right: return IsList ? CouchEffect.None : Move(1, sameRow: IsGrid);
                case CouchCommand.Up: return IsGrid ? Move(-Columns, false) : IsList ? Move(-1, false) : CouchEffect.None;
                case CouchCommand.Down: return IsGrid ? MoveDown() : IsList ? Move(1, false) : CouchEffect.None;
                case CouchCommand.PreviousTab: return ToTab(Tab - 1);
                case CouchCommand.NextTab: return ToTab(Tab + 1);
                case CouchCommand.PreviousLetter: return ToLetter(-1);
                case CouchCommand.NextLetter: return ToLetter(1);
                // The strip, then the grid, then the list
                case CouchCommand.View:
                    View = (CouchView)(((int)View + 1) % 3);
                    return CouchEffect.ViewChanged;
                case CouchCommand.Accept:
                    if (Index < 0)
                        return CouchEffect.None;
                    Action = 0;
                    Screen = CouchScreen.Details;
                    return CouchEffect.ScreenChanged;
                case CouchCommand.Menu:
                    OpenMenu();
                    return CouchEffect.ScreenChanged;
                default: return CouchEffect.None;
            }
        }

        private CouchEffect Move(int by, bool sameRow)
        {
            if (Index < 0)
                return CouchEffect.None;
            int to = Index + by;
            // The ends of a row of the grid do not lead to the row beside it
            if (to < 0 || to >= Count || (sameRow && to / Columns != Index / Columns))
                return CouchEffect.None;
            selected[Tab] = to;
            return CouchEffect.Moved;
        }

        /// <summary>The game under, or the last game when the last row is shorter.</summary>
        private CouchEffect MoveDown()
        {
            if (Index < 0 || Index / Columns == (Count - 1) / Columns)
                return CouchEffect.None;
            selected[Tab] = Math.Min(Index + Columns, Count - 1);
            return CouchEffect.Moved;
        }

        private CouchEffect ToTab(int tab)
        {
            if (tabs.Count == 0)
                return CouchEffect.None;
            // Round the tabs: after the last one comes the first
            Tab = (tab + tabs.Count) % tabs.Count;
            return CouchEffect.Moved;
        }

        /// <summary>The first game whose title starts with the letter before or after the selected game's (the tabs sorted by title).</summary>
        private CouchEffect ToLetter(int direction)
        {
            if (Index < 0)
                return CouchEffect.None;
            IReadOnlyList<string> titles = tabs[Tab];
            char current = Letter(titles[Index]);
            int to = Index;
            if (direction > 0)
            {
                while (to < Count && Letter(titles[to]) == current)
                    to++;
                if (to >= Count)
                    return CouchEffect.None;
            }
            else
            {
                // The start of this letter, else the start of the letter before
                int start = Index;
                while (start > 0 && Letter(titles[start - 1]) == current)
                    start--;
                if (start == Index)
                {
                    if (start == 0)
                        return CouchEffect.None;
                    char before = Letter(titles[start - 1]);
                    start--;
                    while (start > 0 && Letter(titles[start - 1]) == before)
                        start--;
                }
                to = start;
            }
            selected[Tab] = to;
            return CouchEffect.Moved;
        }

        /// <summary>A to Z, and '#' for everything else (digits, signs).</summary>
        public static char Letter(string title)
        {
            foreach (char c in title.Normalize(System.Text.NormalizationForm.FormD))
            {
                if (char.IsLetter(c))
                    return char.ToUpperInvariant(c) is >= 'A' and <= 'Z' and char letter ? letter : '#';
                if (char.IsDigit(c))
                    return '#';
            }
            return '#';
        }

        private CouchEffect InDetails(CouchCommand command)
        {
            switch (command)
            {
                case CouchCommand.Up when Action > 0:
                    Action--;
                    return CouchEffect.Moved;
                case CouchCommand.Down when Action < ActionCount - 1:
                    Action++;
                    return CouchEffect.Moved;
                // The game before or after, without going back to the list
                case CouchCommand.PreviousTab: return Move(-1, false);
                case CouchCommand.NextTab: return Move(1, false);
                case CouchCommand.Accept: return CouchEffect.Action;
                case CouchCommand.Back:
                    Screen = CouchScreen.Home;
                    return CouchEffect.ScreenChanged;
                case CouchCommand.Menu:
                    OpenMenu();
                    return CouchEffect.ScreenChanged;
                default: return CouchEffect.None;
            }
        }

        private CouchEffect InMenu(CouchCommand command)
        {
            switch (command)
            {
                case CouchCommand.Up when MenuLine > 0:
                    MenuLine--;
                    return CouchEffect.Moved;
                case CouchCommand.Down when MenuLine < MenuCount - 1:
                    MenuLine++;
                    return CouchEffect.Moved;
                case CouchCommand.Accept: return CouchEffect.Action;
                case CouchCommand.Back or CouchCommand.Menu:
                    CloseMenu();
                    return CouchEffect.ScreenChanged;
                default: return CouchEffect.None;
            }
        }

        /// <summary>A menu is opened over what is shown (the quick access, the filters): its first line selected.</summary>
        public void OpenMenu()
        {
            MenuLine = 0;
            if (Screen != CouchScreen.Menu)
                beforeMenu = Screen;
            Screen = CouchScreen.Menu;
        }

        private CouchScreen beforeMenu = CouchScreen.Home;
        /// <summary>What the menu was opened over, and goes back to.</summary>
        public CouchScreen BeforeMenu => beforeMenu;

        /// <summary>The mouse points at a tab: shown with the game it had.</summary>
        public void SelectTab(int tab)
        {
            if (tab >= 0 && tab < tabs.Count)
                Tab = tab;
        }

        /// <summary>The mouse points at a line: the action of a game's screen, or the line of the menu.</summary>
        public void SelectLine(int line)
        {
            if (Screen == CouchScreen.Menu)
                MenuLine = Math.Clamp(line, 0, Math.Max(0, MenuCount - 1));
            else
                Action = Math.Clamp(line, 0, Math.Max(0, ActionCount - 1));
        }

        /// <summary>Back to what the menu was opened over: the list of games, or a game's screen.</summary>
        public void CloseMenu() => Screen = beforeMenu == CouchScreen.Details && Index >= 0 ? CouchScreen.Details : CouchScreen.Home;

        /// <summary>Back to the list of games, whatever the menu was opened over.</summary>
        public void BackToList() => Screen = CouchScreen.Home;
    }
}
