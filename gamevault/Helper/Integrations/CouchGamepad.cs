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
        public bool CanRead => library != IntPtr.Zero || xinput;

        /// <summary>The buttons held on any gamepad, the left stick as the cross.</summary>
        public CouchButtons Read()
        {
            CouchButtons held = CouchButtons.None;
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
                try { held |= ReadXInput(); }
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
            (0, CouchButtons.A), (1, CouchButtons.B), (2, CouchButtons.X), (3, CouchButtons.Y), (6, CouchButtons.Start),
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
                for (int i = 0; i < now; i++)
                    if (isController!(i) != 0 && open!(i) is var opened && opened != IntPtr.Zero)
                        controllers.Add(opened);
            }
            CouchButtons held = CouchButtons.None;
            foreach (IntPtr controller in controllers)
            {
                foreach (var (index, flag) in SdlButtons)
                    if (button!(controller, index) != 0)
                        held |= flag;
                held |= Stick(axis!(controller, 0), axis(controller, 1), down: true);
                if (axis(controller, 4) > Pulled)
                    held |= CouchButtons.LeftTrigger;
                if (axis(controller, 5) > Pulled)
                    held |= CouchButtons.RightTrigger;
            }
            return held;
        }

        /// <param name="down">A positive y is down (SDL) or up (XInput).</param>
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
            (0x0010, CouchButtons.Start), (0x0100, CouchButtons.LeftShoulder), (0x0200, CouchButtons.RightShoulder),
            (0x1000, CouchButtons.A), (0x2000, CouchButtons.B), (0x4000, CouchButtons.X), (0x8000, CouchButtons.Y),
        };

        private static CouchButtons ReadXInput()
        {
            CouchButtons held = CouchButtons.None;
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out XInputState state) != 0)
                    continue;
                foreach (var (mask, flag) in XInputButtons)
                    if ((state.Buttons & mask) != 0)
                        held |= flag;
                held |= Stick(state.LeftX, state.LeftY, down: false);
                if (state.LeftTrigger > 90)
                    held |= CouchButtons.LeftTrigger;
                if (state.RightTrigger > 90)
                    held |= CouchButtons.RightTrigger;
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
