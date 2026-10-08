namespace GameVault.Core.Input
{
    /// <summary>What the living room mode does with a gamepad (or the keyboard).</summary>
    public enum PadAction { Up, Down, Left, Right, Confirm, Back, Details, Search, PreviousRow, NextRow, Menu }

    /// <summary>The state of a gamepad at one moment, whatever the system it comes from.</summary>
    public readonly record struct PadState(
        bool Up, bool Down, bool Left, bool Right,
        bool A, bool B, bool X, bool Y, bool LeftShoulder, bool RightShoulder, bool Start,
        double StickX, double StickY)
    {
        public static PadState Released => default;
    }

    /// <summary>
    /// Turns successive gamepad states into actions: one action per button press, directions from the D-pad or the
    /// left stick (with a dead zone), repeated while held (after a first delay, like a keyboard).
    /// </summary>
    public sealed class GamepadMapping
    {
        public const double DeadZone = 0.5;
        public static readonly TimeSpan FirstRepeat = TimeSpan.FromMilliseconds(380);
        public static readonly TimeSpan NextRepeats = TimeSpan.FromMilliseconds(110);

        private PadState previous;
        private PadAction? heldDirection;
        private DateTime nextRepeat;

        public IReadOnlyList<PadAction> Update(PadState state, DateTime now)
        {
            var actions = new List<PadAction>();
            void Pressed(bool before, bool after, PadAction action)
            {
                if (after && !before)
                    actions.Add(action);
            }
            Pressed(previous.A, state.A, PadAction.Confirm);
            Pressed(previous.B, state.B, PadAction.Back);
            Pressed(previous.X, state.X, PadAction.Details);
            Pressed(previous.Y, state.Y, PadAction.Search);
            Pressed(previous.LeftShoulder, state.LeftShoulder, PadAction.PreviousRow);
            Pressed(previous.RightShoulder, state.RightShoulder, PadAction.NextRow);
            Pressed(previous.Start, state.Start, PadAction.Menu);

            PadAction? direction = Direction(state);
            if (direction != heldDirection)
            {
                heldDirection = direction;
                if (direction != null)
                {
                    actions.Add(direction.Value);
                    nextRepeat = now + FirstRepeat;
                }
            }
            else if (direction != null && now >= nextRepeat)
            {
                actions.Add(direction.Value);
                nextRepeat = now + NextRepeats;
            }
            previous = state;
            return actions;
        }

        /// <summary>The D-pad wins over the stick; the stick's larger axis decides (Y up is negative).</summary>
        public static PadAction? Direction(PadState state)
        {
            if (state.Up) return PadAction.Up;
            if (state.Down) return PadAction.Down;
            if (state.Left) return PadAction.Left;
            if (state.Right) return PadAction.Right;
            double x = state.StickX, y = state.StickY;
            if (Math.Max(Math.Abs(x), Math.Abs(y)) < DeadZone)
                return null;
            return Math.Abs(x) >= Math.Abs(y)
                ? (x < 0 ? PadAction.Left : PadAction.Right)
                : (y < 0 ? PadAction.Up : PadAction.Down);
        }
    }
}
