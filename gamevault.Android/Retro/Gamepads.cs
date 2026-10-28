using Android.Content;
using Android.Hardware.Input;
using Android.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.Android.Retro
{
    /// <summary>
    /// The controllers of the phone: wired, Bluetooth, or a handheld's own buttons. Known as they come and go (the touch
    /// pad hides while one is there); their buttons and sticks go to the player, each controller on its own port in the
    /// order they are first used.
    /// </summary>
    internal sealed class Gamepads : Java.Lang.Object, InputManager.IInputDeviceListener
    {
        private readonly InputManager inputs;
        private readonly Dictionary<int, int> ports = new();
        private readonly Dictionary<int, State> states = new();

        private sealed class State
        {
            public int Keys, Hat, Stick, Triggers;
            public int Buttons => Keys | Hat | Stick | Triggers;
        }

        /// <summary>A controller came or went.</summary>
        public event Action<bool>? PresenceChanged;

        public RetroPlayer? Player { get; set; }

        public Gamepads(Context context)
        {
            inputs = (InputManager)context.GetSystemService(Context.InputService)!;
            inputs.RegisterInputDeviceListener(this, null);
        }

        public bool Any => (inputs.GetInputDeviceIds() ?? Array.Empty<int>()).Select(InputDevice.GetDevice).Any(IsController);

        private static bool IsController(InputDevice? device) =>
            device != null && !device.IsVirtual
            && (Has(device.Sources, InputSourceType.Gamepad) || Has(device.Sources, InputSourceType.Joystick));

        private static bool Has(InputSourceType sources, InputSourceType kind) => (sources & kind) == kind;

        public void OnInputDeviceAdded(int deviceId) => PresenceChanged?.Invoke(Any);
        public void OnInputDeviceChanged(int deviceId) => PresenceChanged?.Invoke(Any);
        public void OnInputDeviceRemoved(int deviceId)
        {
            ports.Remove(deviceId);
            states.Remove(deviceId);
            PresenceChanged?.Invoke(Any);
        }

        private static int? ButtonOf(Keycode key) => key switch
        {
            // The button at the bottom is the RetroPad's B, as in RetroArch
            Keycode.ButtonA => RetroPlayer.B,
            Keycode.ButtonB => RetroPlayer.A,
            Keycode.ButtonX => RetroPlayer.Y,
            Keycode.ButtonY => RetroPlayer.X,
            Keycode.ButtonL1 => RetroPlayer.L,
            Keycode.ButtonR1 => RetroPlayer.R,
            Keycode.ButtonL2 => RetroPlayer.L2,
            Keycode.ButtonR2 => RetroPlayer.R2,
            Keycode.ButtonThumbl => RetroPlayer.L3,
            Keycode.ButtonThumbr => RetroPlayer.R3,
            Keycode.ButtonStart => RetroPlayer.Start,
            Keycode.ButtonSelect => RetroPlayer.Select,
            Keycode.DpadUp => RetroPlayer.Up,
            Keycode.DpadDown => RetroPlayer.Down,
            Keycode.DpadLeft => RetroPlayer.Left,
            Keycode.DpadRight => RetroPlayer.Right,
            _ => null,
        };

        /// <summary>A controller's button in the player's menu (only its RetroPad id).</summary>
        public static int? MenuButtonOf(Keycode key) => ButtonOf(key);

        private int PortOf(int deviceId)
        {
            if (!ports.TryGetValue(deviceId, out int port))
            {
                port = Enumerable.Range(0, 4).FirstOrDefault(p => !ports.ContainsValue(p), -1);
                if (port < 0)
                    return -1;
                ports[deviceId] = port;
            }
            return port;
        }

        private State StateOf(int deviceId)
        {
            if (!states.TryGetValue(deviceId, out State? state))
                states[deviceId] = state = new State();
            return state;
        }

        /// <summary>A controller's button, while a game is played: true when it is the game's.</summary>
        public bool OnKey(KeyEvent e)
        {
            RetroPlayer? player = Player;
            // By the event's source: some handhelds' buttons come from a device Android does not list as a controller
            if (player == null || !(Has(e.Source, InputSourceType.Gamepad) || Has(e.Source, InputSourceType.Joystick) || IsController(e.Device))
                || ButtonOf(e.KeyCode) is not int button)
                return false;
            int port = PortOf(e.DeviceId);
            if (port < 0)
                return true;
            State state = StateOf(e.DeviceId);
            if (e.Action == KeyEventActions.Down)
                state.Keys |= 1 << button;
            else if (e.Action == KeyEventActions.Up)
                state.Keys &= ~(1 << button);
            player.SetButtons(port, state.Buttons);
            ControllerUsed?.Invoke();
            return true;
        }

        /// <summary>A controller was used: the touch pad hides even when Android lists none (some handhelds).</summary>
        public event Action? ControllerUsed;

        /// <summary>Sticks, the hat (a cross reported as axes) and analog triggers.</summary>
        public bool OnMotion(MotionEvent e)
        {
            RetroPlayer? player = Player;
            if (player == null || e.Action != MotionEventActions.Move || !(Has(e.Source, InputSourceType.Joystick) || Has(e.Source, InputSourceType.Gamepad)))
                return false;
            int port = PortOf(e.DeviceId);
            if (port < 0)
                return true;
            State state = StateOf(e.DeviceId);
            float x = e.GetAxisValue(Axis.X), y = e.GetAxisValue(Axis.Y);
            float hatX = e.GetAxisValue(Axis.HatX), hatY = e.GetAxisValue(Axis.HatY);
            state.Hat = Direction(hatX, hatY, 0.5f);
            // The left stick also moves the cross: the 2D games have no stick
            state.Stick = Direction(x, y, 0.5f);
            float left = Math.Max(e.GetAxisValue(Axis.Ltrigger), e.GetAxisValue(Axis.Brake));
            float right = Math.Max(e.GetAxisValue(Axis.Rtrigger), e.GetAxisValue(Axis.Gas));
            state.Triggers = (left > 0.5f ? 1 << RetroPlayer.L2 : 0) | (right > 0.5f ? 1 << RetroPlayer.R2 : 0);
            player.Sticks[port, 0] = Analog(x);
            player.Sticks[port, 1] = Analog(y);
            player.Sticks[port, 2] = Analog(e.GetAxisValue(Axis.Z));
            player.Sticks[port, 3] = Analog(e.GetAxisValue(Axis.Rz));
            player.SetButtons(port, state.Buttons);
            ControllerUsed?.Invoke();
            return true;
        }

        private static short Analog(float value) => (short)Math.Clamp(value * 32767, -32767, 32767);

        private static int Direction(float x, float y, float threshold) =>
            (x < -threshold ? 1 << RetroPlayer.Left : 0) | (x > threshold ? 1 << RetroPlayer.Right : 0)
            | (y < -threshold ? 1 << RetroPlayer.Up : 0) | (y > threshold ? 1 << RetroPlayer.Down : 0);

        /// <summary>A new game: the ports are given again in the order the controllers are used.</summary>
        public void Reset()
        {
            ports.Clear();
            states.Clear();
        }
    }
}
