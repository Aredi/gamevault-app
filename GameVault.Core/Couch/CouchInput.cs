namespace GameVault.Core.Couch
{
    /// <summary>What a gamepad (or the keyboard) asks the living-room mode.</summary>
    public enum CouchCommand
    {
        Up, Down, Left, Right,
        /// <summary>A: open, start.</summary>
        Accept,
        /// <summary>B: back.</summary>
        Back,
        /// <summary>X: the strip of covers or the grid.</summary>
        View,
        /// <summary>LB and RB: the tab before, the tab after.</summary>
        PreviousTab, NextTab,
        /// <summary>LT and RT: the first game of the letter before, of the letter after.</summary>
        PreviousLetter, NextLetter,
        /// <summary>Start: the menu.</summary>
        Menu,
    }

    /// <summary>The buttons held on the gamepads at one moment.</summary>
    [Flags]
    public enum CouchButtons
    {
        None = 0,
        Up = 1, Down = 2, Left = 4, Right = 8,
        A = 16, B = 32, X = 64, Y = 128,
        LeftShoulder = 256, RightShoulder = 512, LeftTrigger = 1024, RightTrigger = 2048,
        Start = 4096,
    }

    /// <summary>
    /// The buttons held, read again and again, made the commands of a menu: one at the press, and a direction held
    /// goes on by itself (after a moment, then fast), as on a console.
    /// </summary>
    public sealed class CouchRepeater
    {
        public static readonly TimeSpan FirstRepeat = TimeSpan.FromMilliseconds(420), NextRepeat = TimeSpan.FromMilliseconds(95);

        private static readonly (CouchButtons Button, CouchCommand Command, bool Repeats)[] Map =
        {
            (CouchButtons.Up, CouchCommand.Up, true), (CouchButtons.Down, CouchCommand.Down, true),
            (CouchButtons.Left, CouchCommand.Left, true), (CouchButtons.Right, CouchCommand.Right, true),
            (CouchButtons.A, CouchCommand.Accept, false), (CouchButtons.B, CouchCommand.Back, false),
            (CouchButtons.X, CouchCommand.View, false),
            (CouchButtons.LeftShoulder, CouchCommand.PreviousTab, false), (CouchButtons.RightShoulder, CouchCommand.NextTab, false),
            (CouchButtons.LeftTrigger, CouchCommand.PreviousLetter, true), (CouchButtons.RightTrigger, CouchCommand.NextLetter, true),
            (CouchButtons.Start, CouchCommand.Menu, false),
        };

        private CouchButtons held;
        private readonly Dictionary<CouchButtons, TimeSpan> nextAt = new();

        /// <summary>What was held while nobody looked (a game was played) is not a press when the menu is back.</summary>
        public void Reset(CouchButtons now)
        {
            held = now;
            nextAt.Clear();
        }

        /// <param name="now">The buttons held.</param>
        /// <param name="time">Any clock that goes forward.</param>
        public IReadOnlyList<CouchCommand> Next(CouchButtons now, TimeSpan time)
        {
            var commands = new List<CouchCommand>();
            foreach (var (button, command, repeats) in Map)
            {
                bool down = (now & button) != 0, was = (held & button) != 0;
                if (down && !was)
                {
                    commands.Add(command);
                    nextAt[button] = time + FirstRepeat;
                }
                else if (down && repeats && nextAt.TryGetValue(button, out TimeSpan at) && time >= at)
                {
                    commands.Add(command);
                    nextAt[button] = time + NextRepeat;
                }
                else if (!down)
                    nextAt.Remove(button);
            }
            held = now;
            return commands;
        }
    }
}
