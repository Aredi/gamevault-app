using Avalonia.Threading;
using GameVault.Core;
using GameVault.Core.Input;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace gamevault.Helper.Platform
{
    /// <summary>
    /// The first connected gamepad, read while the living room mode is open: XInput on Windows (Xbox and compatible
    /// pads), the kernel joystick devices (/dev/input/js*) on Linux. Raises <see cref="Action"/> on the UI thread.
    /// </summary>
    internal sealed class Gamepads : IDisposable
    {
        public event Action<PadAction>? Action;
        /// <summary>A gamepad was connected or disconnected (true: one is there).</summary>
        public event Action<bool>? ConnectedChanged;

        private readonly GamepadMapping mapping = new();
        private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private bool connected;
        private volatile bool stopped;
        private Thread? linuxReader;
        private PadState linuxState;
        private readonly object linuxLock = new();
        private bool linuxConnected;

        public bool IsConnected => connected;

        public void Start()
        {
            stopped = false;
            if (OperatingSystem.IsLinux() && linuxReader == null)
            {
                linuxReader = new Thread(ReadLinuxJoysticks) { IsBackground = true, Name = "Gamepad" };
                linuxReader.Start();
            }
            poll.Tick += Poll;
            poll.Start();
        }

        public void Stop()
        {
            poll.Stop();
            poll.Tick -= Poll;
            stopped = true;
        }

        public void Dispose() => Stop();

        private void Poll(object? sender, EventArgs e)
        {
            PadState? state = OperatingSystem.IsWindows() ? ReadXInput() : ReadLinuxState();
            bool nowConnected = state != null;
            if (nowConnected != connected)
            {
                connected = nowConnected;
                ConnectedChanged?.Invoke(connected);
            }
            foreach (PadAction action in mapping.Update(state ?? PadState.Released, DateTime.UtcNow))
            {
                try { Action?.Invoke(action); }
                catch (Exception ex) { Log.Ignored(ex); }
            }
        }

        #region Windows: XInput
        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger, RightTrigger;
            public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState14(int userIndex, out XInputState state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState910(int userIndex, out XInputState state);
        private static bool useOldXInput;
        private static bool xinputMissing;

        private static int connectedIndex = -1;
        private static DateTime nextScanUtc;

        /// <summary>
        /// XInputGetState is slow for empty slots: the known pad is read every frame, the empty slots are looked at
        /// every two seconds only (to notice a pad being plugged in).
        /// </summary>
        private static PadState? ReadXInput()
        {
            if (xinputMissing)
                return null;
            if (connectedIndex >= 0)
            {
                PadState? known = ReadXInputSlot(connectedIndex);
                if (known != null)
                    return known;
                connectedIndex = -1;
            }
            if (DateTime.UtcNow < nextScanUtc)
                return null;
            nextScanUtc = DateTime.UtcNow.AddSeconds(2);
            for (int index = 0; index < 4; index++)
            {
                PadState? state = ReadXInputSlot(index);
                if (state != null)
                {
                    connectedIndex = index;
                    return state;
                }
            }
            return null;
        }

        private static PadState? ReadXInputSlot(int index)
        {
            if (xinputMissing)
                return null;
            {
                int result;
                XInputState state;
                try
                {
                    result = useOldXInput ? XInputGetState910(index, out state) : XInputGetState14(index, out state);
                }
                catch (DllNotFoundException) when (!useOldXInput)
                {
                    useOldXInput = true;
                    return ReadXInputSlot(index);
                }
                catch (Exception ex)
                {
                    Log.Ignored(ex);
                    xinputMissing = true;
                    return null;
                }
                if (result != 0)
                    return null;// not connected
                ushort b = state.Gamepad.Buttons;
                return new PadState(
                    (b & 0x0001) != 0, (b & 0x0002) != 0, (b & 0x0004) != 0, (b & 0x0008) != 0,
                    (b & 0x1000) != 0, (b & 0x2000) != 0, (b & 0x4000) != 0, (b & 0x8000) != 0,
                    (b & 0x0100) != 0, (b & 0x0200) != 0, (b & 0x0010) != 0,
                    state.Gamepad.ThumbLX / 32768.0, -state.Gamepad.ThumbLY / 32768.0);
            }
            return null;
        }
        #endregion

        #region Linux: /dev/input/js*
        private PadState? ReadLinuxState()
        {
            lock (linuxLock)
                return linuxConnected ? linuxState : null;
        }

        /// <summary>Opens the first joystick device, reads its events, and waits for another one when it goes away.</summary>
        private void ReadLinuxJoysticks()
        {
            var buffer = new byte[8];
            while (true)
            {
                if (stopped)
                {
                    Thread.Sleep(500);
                    continue;
                }
                string? device = null;
                for (int i = 0; i < 8 && device == null; i++)
                {
                    string path = $"/dev/input/js{i}";
                    if (File.Exists(path) && IsLinuxGamepad(i))
                        device = path;
                }
                if (device == null)
                {
                    Thread.Sleep(2000);
                    continue;
                }
                try
                {
                    using var stream = new FileStream(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8, FileOptions.None);
                    lock (linuxLock)
                    {
                        linuxState = PadState.Released;
                        linuxConnected = true;
                    }
                    while (stream.ReadAtLeast(buffer, 8, throwOnEndOfStream: true) == 8)
                    {
                        // struct js_event { u32 time; s16 value; u8 type; u8 number; }
                        short value = BitConverter.ToInt16(buffer, 4);
                        byte type = (byte)(buffer[6] & 0x7F);// 0x80: initial state
                        byte number = buffer[7];
                        lock (linuxLock)
                            linuxState = type == 1 ? Button(linuxState, number, value != 0) : type == 2 ? Axis(linuxState, number, value) : linuxState;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
                {
                    // Unplugged, or not readable: look again a bit later
                }
                lock (linuxLock)
                    linuxConnected = false;
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Mice, KVM pointers or LED controllers can show up as js devices too: only the ones with gamepad buttons count.
        /// </summary>
        private static bool IsLinuxGamepad(int index)
        {
            try
            {
                string capabilities = $"/sys/class/input/js{index}/device/capabilities/key";
                return File.Exists(capabilities) && KernelCapabilities.IsGamepad(File.ReadAllText(capabilities));
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return false;
            }
        }

        /// <summary>Button numbers of the Linux xpad driver (Xbox layout, used by most pads).</summary>
        private static PadState Button(PadState s, byte number, bool down) => number switch
        {
            0 => s with { A = down },
            1 => s with { B = down },
            2 => s with { X = down },
            3 => s with { Y = down },
            4 => s with { LeftShoulder = down },
            5 => s with { RightShoulder = down },
            7 => s with { Start = down },
            // Pads reporting the D-pad as buttons
            11 => s with { Left = down },
            12 => s with { Right = down },
            13 => s with { Up = down },
            14 => s with { Down = down },
            _ => s,
        };

        private static PadState Axis(PadState s, byte number, short value)
        {
            double v = value / 32767.0;
            return number switch
            {
                0 => s with { StickX = v },
                1 => s with { StickY = v },
                // D-pad as a hat: axes 6 / 7
                6 => s with { Left = value < -16000, Right = value > 16000 },
                7 => s with { Up = value < -16000, Down = value > 16000 },
                _ => s,
            };
        }
        #endregion
    }
}
