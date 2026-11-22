using GameVault.Core;
using GameVault.Core.Couch;
using gamevault.Helper.Platform;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace gamevault.Helper
{
    /// <summary>
    /// The gamepads as the living-room mode reads them: every plugged one moves the menus. On Windows the Xbox ones are
    /// read with XInput, which Windows has; the others (PlayStation, Switch) and the other systems with SDL2, RetroArch's
    /// or the system's.
    /// </summary>
    internal sealed class CouchGamepad : IDisposable
    {
        private const uint InitGameController = 0x2000;
        private const short Pushed = 18000, Pulled = 12000;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfUInt(uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidOfUInt(uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidOfNone();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfNone();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfInt(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PtrOfInt(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidOfPtr(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ByteOfPtrInt(IntPtr device, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short ShortOfPtrInt(IntPtr device, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfPtr(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr StringOfPtr(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfStrings([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        private IntPtr library;
        private VoidOfUInt? quit;
        private VoidOfNone? pump;
        private IntOfNone? count;
        private IntOfInt? isController;
        private PtrOfInt? open;
        private VoidOfPtr? close;
        private ByteOfPtrInt? button;
        private ShortOfPtrInt? axis;
        private readonly List<IntPtr> controllers = new();
        // The kind of each controller opened (CouchSettings.Xbox, PlayStation, Switch), in the same order
        private readonly List<string> kinds = new();
        private IntOfPtr? typeOf;
        private StringOfPtr? nameOf;

        /// <summary>The kind of the gamepad a button was last pressed on (none yet): the hints name its buttons.</summary>
        public string? LastUsed { get; private set; }
        private int devices = -1;
        private bool xinput = OperatingSystem.IsWindows();

        public CouchGamepad()
        {
            try
            {
                if (!TryLoadSdl(out library))
                    return;
                T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
                // No window of SDL's has the focus here: the client says when its own window has it
                Function<IntOfStrings>("SDL_SetHint")("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
                // Read without taking them: through HIDAPI, SDL turns PlayStation and Switch gamepads to their own reports,
                // and the emulator started from here (RetroArch through DirectInput) no longer gets their buttons. The
                // system's shared reading (Raw Input, DirectInput, evdev) leaves them as they are
                foreach (string kind in new[] { "SDL_JOYSTICK_HIDAPI_PS4", "SDL_JOYSTICK_HIDAPI_PS5", "SDL_JOYSTICK_HIDAPI_SWITCH", "SDL_JOYSTICK_HIDAPI_JOY_CONS" })
                    Function<IntOfStrings>("SDL_SetHint")(kind, "0");
                if (Function<IntOfUInt>("SDL_Init")(InitGameController) != 0)
                {
                    library = IntPtr.Zero;
                    return;
                }
                quit = Function<VoidOfUInt>("SDL_QuitSubSystem");
                pump = Function<VoidOfNone>("SDL_PumpEvents");
                count = Function<IntOfNone>("SDL_NumJoysticks");
                isController = Function<IntOfInt>("SDL_IsGameController");
                open = Function<PtrOfInt>("SDL_GameControllerOpen");
                close = Function<VoidOfPtr>("SDL_GameControllerClose");
                button = Function<ByteOfPtrInt>("SDL_GameControllerGetButton");
                axis = Function<ShortOfPtrInt>("SDL_GameControllerGetAxis");
                // Its type since SDL 2.0.12; its name always
                if (NativeLibrary.TryGetExport(library, "SDL_GameControllerGetType", out IntPtr type))
                    typeOf = Marshal.GetDelegateForFunctionPointer<IntOfPtr>(type);
                nameOf = Function<StringOfPtr>("SDL_GameControllerName");
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                library = IntPtr.Zero;
            }
        }

        private static bool TryLoadSdl(out IntPtr library)
        {
            library = IntPtr.Zero;
            if (RetroArchManager.Sdl2Library is string known && NativeLibrary.TryLoad(known, out library))
                return true;
            // The system's, where it has one
            return !OperatingSystem.IsWindows() && !PlatformInfo.IsMac && NativeLibrary.TryLoad("libSDL2-2.0.so.0", out library);
        }

        /// <summary>A way to read gamepads was found (none: the keyboard only).</summary>
        public bool CanRead => library != IntPtr.Zero || xinput || PlatformInfo.IsAndroid;

        /// <summary>Where the right stick points, from -1 to 1 each way (down is positive): the mouse's move.</summary>
        public (double X, double Y) RightStick { get; private set; }

        /// <summary>The right stick the furthest from its rest, among the gamepads.</summary>
        private void Aim(short x, short y)
        {
            (double X, double Y) aim = (x / 32768.0, y / 32768.0);
            if (aim.X * aim.X + aim.Y * aim.Y > RightStick.X * RightStick.X + RightStick.Y * RightStick.Y)
                RightStick = aim;
        }

        /// <summary>The buttons held on any gamepad, the left stick as the cross.</summary>
        public CouchButtons Read()
        {
            RightStick = default;
            CouchButtons held = CouchButtons.None;
            // Android: its controllers as the activity saw them
            if (PlatformInfo.IsAndroid)
            {
                held = MobileGamepad.TakeHeld();
                RightStick = MobileGamepad.RightStick;
                if (held != CouchButtons.None)
                    LastUsed = MobileGamepad.Kind;
                return held;
            }
            if (library != IntPtr.Zero)
            {
                try { held |= ReadSdl(); }
                catch (Exception ex)
                {
                    Log.Ignored(ex);
                    library = IntPtr.Zero;
                }
            }
            if (xinput)
            {
                try
                {
                    CouchButtons fromXInput = ReadXInput();
                    if (fromXInput != CouchButtons.None && held == CouchButtons.None)
                        LastUsed = CouchSettings.Xbox;
                    held |= fromXInput;
                }
                catch (Exception ex)
                {
                    // No XInput on this Windows
                    Log.Ignored(ex);
                    xinput = false;
                }
            }
            return held;
        }

        // SDL's game controller: its buttons and axes
        private static readonly (int Button, CouchButtons Flag)[] SdlButtons =
        {
            (0, CouchButtons.A), (1, CouchButtons.B), (2, CouchButtons.X), (3, CouchButtons.Y), (4, CouchButtons.Back), (6, CouchButtons.Start),
            (7, CouchButtons.LeftStick), (8, CouchButtons.RightStick),
            (9, CouchButtons.LeftShoulder), (10, CouchButtons.RightShoulder),
            (11, CouchButtons.Up), (12, CouchButtons.Down), (13, CouchButtons.Left), (14, CouchButtons.Right),
        };

        private CouchButtons ReadSdl()
        {
            pump!();
            int now = count!();
            if (now != devices)
            {
                // One plugged or unplugged: all opened again
                devices = now;
                foreach (IntPtr controller in controllers)
                    close!(controller);
                controllers.Clear();
                kinds.Clear();
                for (int i = 0; i < now; i++)
                    if (isController!(i) != 0 && open!(i) is var opened && opened != IntPtr.Zero)
                    {
                        controllers.Add(opened);
                        kinds.Add(KindOf(opened));
                    }
            }
            CouchButtons held = CouchButtons.None;
            for (int c = 0; c < controllers.Count; c++)
            {
                IntPtr controller = controllers[c];
                CouchButtons mine = CouchButtons.None;
                foreach (var (index, flag) in SdlButtons)
                    if (button!(controller, index) != 0)
                        mine |= flag;
                mine |= Stick(axis!(controller, 0), axis(controller, 1), down: true);
                Aim(axis(controller, 2), axis(controller, 3));
                if (axis(controller, 4) > Pulled)
                    mine |= CouchButtons.LeftTrigger;
                if (axis(controller, 5) > Pulled)
                    mine |= CouchButtons.RightTrigger;
                if (mine != CouchButtons.None)
                    LastUsed = kinds[c];
                held |= mine;
            }
            return held;
        }

        /// <param name="down">A positive y is down (SDL) or up (XInput).</param>
        private string KindOf(IntPtr controller)
        {
            try
            {
                int type = typeOf?.Invoke(controller) ?? 0;
                string? name = nameOf?.Invoke(controller) is IntPtr text && text != IntPtr.Zero ? Marshal.PtrToStringUTF8(text) : null;
                return CouchGlyphs.KindOf(type, name);
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return CouchSettings.Xbox;
            }
        }

        private static CouchButtons Stick(short x, short y, bool down)
        {
            CouchButtons held = CouchButtons.None;
            // The stronger direction only: a stick pushed right is never quite level
            if (Math.Abs((int)x) >= Math.Abs((int)y))
                held |= x > Pushed ? CouchButtons.Right : x < -Pushed ? CouchButtons.Left : CouchButtons.None;
            else if (y > Pushed)
                held |= down ? CouchButtons.Down : CouchButtons.Up;
            else if (y < -Pushed)
                held |= down ? CouchButtons.Up : CouchButtons.Down;
            return held;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint Packet;
            public ushort Buttons;
            public byte LeftTrigger, RightTrigger;
            public short LeftX, LeftY, RightX, RightY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState(uint index, out XInputState state);

        private static readonly (ushort Button, CouchButtons Flag)[] XInputButtons =
        {
            (0x0001, CouchButtons.Up), (0x0002, CouchButtons.Down), (0x0004, CouchButtons.Left), (0x0008, CouchButtons.Right),
            (0x0010, CouchButtons.Start), (0x0020, CouchButtons.Back), (0x0040, CouchButtons.LeftStick), (0x0080, CouchButtons.RightStick),
            (0x0100, CouchButtons.LeftShoulder), (0x0200, CouchButtons.RightShoulder),
            (0x1000, CouchButtons.A), (0x2000, CouchButtons.B), (0x4000, CouchButtons.X), (0x8000, CouchButtons.Y),
        };

        /// <summary>The XInput slots with a gamepad; the empty ones are looked at every 2 seconds only.</summary>
        private readonly bool[] plugged = new bool[4];
        private long lookedForPads = long.MinValue;

        /// <summary>
        /// Asking an empty slot is slow (Windows looks for the device each time) and Microsoft advises against doing it at
        /// each frame: done 60 times a second on the screen's thread, it made the menus stutter.
        /// </summary>
        internal static bool LookAtEmptySlots(long now, ref long lastLook)
        {
            // Never looked yet: at once (now - long.MinValue would overflow)
            if (lastLook != long.MinValue && now - lastLook < 2000)
                return false;
            lastLook = now;
            return true;
        }

        private CouchButtons ReadXInput()
        {
            CouchButtons held = CouchButtons.None;
            bool lookAtEmpty = LookAtEmptySlots(Environment.TickCount64, ref lookedForPads);
            for (uint i = 0; i < 4; i++)
            {
                if (!plugged[i] && !lookAtEmpty)
                    continue;
                plugged[i] = XInputGetState(i, out XInputState state) == 0;
                if (!plugged[i])
                    continue;
                CouchButtons mine = CouchButtons.None;
                foreach (var (mask, flag) in XInputButtons)
                    if ((state.Buttons & mask) != 0)
                        mine |= flag;
                mine |= Stick(state.LeftX, state.LeftY, down: false);
                Aim(state.RightX, (short)Math.Clamp(-(int)state.RightY, short.MinValue, short.MaxValue));
                if (state.LeftTrigger > 90)
                    mine |= CouchButtons.LeftTrigger;
                if (state.RightTrigger > 90)
                    mine |= CouchButtons.RightTrigger;
                held |= mine;
            }
            return held;
        }

        public void Dispose()
        {
            if (library == IntPtr.Zero)
                return;
            try
            {
                foreach (IntPtr controller in controllers)
                    close!(controller);
                controllers.Clear();
                quit!(InitGameController);
            }
            catch (Exception ex) { Log.Ignored(ex); }
            library = IntPtr.Zero;
        }
    }
}
