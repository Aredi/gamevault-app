using gamevault.Localization;
using GameVault.Core;
using GameVault.Core.Retro;
using System;
using System.Runtime.InteropServices;

namespace gamevault.Helper
{
    /// <summary>
    /// The first gamepad, read with the SDL2 library RetroArch comes with: the buttons, axes and hats get the numbers
    /// RetroArch's SDL2 driver gives them (a game controller SDL knows by its own buttons, any other by its raw ones), the
    /// driver the consoles set in <see cref="RetroPad"/> are played with.
    /// </summary>
    internal sealed class RetroGamepad : IDisposable
    {
        private const uint InitGameController = 0x2000;
        private const short Pressed = 20000, Moved = 16000;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfUInt(uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidOfUInt(uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidOfNone();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfNone();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfInt(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PtrOfInt(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PtrOfPtr(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfPtr(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidOfPtr(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ByteOfPtrInt(IntPtr device, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short ShortOfPtrInt(IntPtr device, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate JoystickGuid GuidOfInt(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfStrings([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        private readonly IntPtr library;
        private readonly VoidOfUInt quit;
        private readonly VoidOfNone pump;
        private readonly IntOfNone count;
        private readonly IntOfInt isController;
        private readonly PtrOfInt openController, openJoystick;
        private readonly PtrOfPtr controllerName, joystickName;
        private readonly ByteOfPtrInt controllerButton, joystickButton, joystickHat;
        private readonly ShortOfPtrInt controllerAxis, joystickAxis;
        private readonly IntOfPtr buttons, axes, hats;
        private readonly VoidOfPtr closeController, closeJoystick;

        private IntPtr controller, joystick;
        private int devices = -1;
        private byte[] lastButtons = Array.Empty<byte>(), lastHats = Array.Empty<byte>();
        private short[] restAxes = Array.Empty<short>();
        private bool[] heldAxes = Array.Empty<bool>();

        /// <summary>The gamepad's name, null when none is plugged.</summary>
        public string? Name { get; private set; }
        /// <summary>A game controller SDL knows (Xbox, PlayStation, Switch...): RetroArch's profiles' buttons are its own.</summary>
        public bool IsStandard => controller != IntPtr.Zero;

        private T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        /// <param name="libraryFile">RetroArch's SDL2: see <see cref="RetroArchManager.Sdl2LibraryAsync"/>.</param>
        public RetroGamepad(string libraryFile)
        {
            library = NativeLibrary.Load(libraryFile);
            // Read while the client's window is not the focused one too (the player looks at the screen, not the window)
            Function<IntOfStrings>("SDL_SetHint")("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (Function<IntOfUInt>("SDL_Init")(InitGameController) != 0)
                throw new InvalidOperationException(Loc.T("SDL could not read the gamepads"));
            quit = Function<VoidOfUInt>("SDL_QuitSubSystem");
            pump = Function<VoidOfNone>("SDL_PumpEvents");
            count = Function<IntOfNone>("SDL_NumJoysticks");
            isController = Function<IntOfInt>("SDL_IsGameController");
            openController = Function<PtrOfInt>("SDL_GameControllerOpen");
            openJoystick = Function<PtrOfInt>("SDL_JoystickOpen");
            controllerName = Function<PtrOfPtr>("SDL_GameControllerName");
            joystickName = Function<PtrOfPtr>("SDL_JoystickName");
            controllerButton = Function<ByteOfPtrInt>("SDL_GameControllerGetButton");
            joystickButton = Function<ByteOfPtrInt>("SDL_JoystickGetButton");
            joystickHat = Function<ByteOfPtrInt>("SDL_JoystickGetHat");
            controllerAxis = Function<ShortOfPtrInt>("SDL_GameControllerGetAxis");
            joystickAxis = Function<ShortOfPtrInt>("SDL_JoystickGetAxis");
            buttons = Function<IntOfPtr>("SDL_JoystickNumButtons");
            axes = Function<IntOfPtr>("SDL_JoystickNumAxes");
            hats = Function<IntOfPtr>("SDL_JoystickNumHats");
            closeController = Function<VoidOfPtr>("SDL_GameControllerClose");
            closeJoystick = Function<VoidOfPtr>("SDL_JoystickClose");
        }

        // SDL's game controller: its buttons (A to the cross, then the extra ones) and axes
        private const int ControllerButtons = 21, ControllerAxes = 6;

        /// <summary>The first gamepad (RetroArch's first player's), opened again when one is plugged or unplugged.</summary>
        private void Refresh()
        {
            pump();
            int now = count();
            if (now == devices)
                return;
            devices = now;
            Close();
            if (now == 0)
                return;
            Index = FirstController();
            if (isController(Index) != 0)
            {
                controller = openController(Index);
                Name = Marshal.PtrToStringUTF8(controllerName(controller));
            }
            else
            {
                joystick = openJoystick(Index);
                Name = joystick == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(joystickName(joystick));
            }
            Rest();
        }

        /// <summary>The first game controller SDL knows, else the first device: a PC's LED controller or KVM is seen as a joystick too.</summary>
        private int FirstController()
        {
            for (int i = 0; i < count(); i++)
                if (isController(i) != 0)
                    return i;
            return 0;
        }

        /// <summary>SDL's index of the gamepad read: RetroArch's SDL2 driver gives its ports in this order.</summary>
        public int Index { get; private set; }

        /// <summary>The index of the gamepad the first player plays with (see <see cref="Index"/>), when SDL can be read.</summary>
        public static int? FirstControllerIndex(string libraryFile)
        {
            try
            {
                using var gamepad = new RetroGamepad(libraryFile);
                gamepad.Refresh();
                return gamepad.Name != null ? gamepad.Index : null;
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        /// <summary>SDL's 16 bytes of a gamepad's id (SDL_JoystickGUID).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct JoystickGuid { public ulong Low, High; }

        /// <summary>SDL's id of the gamepad the first player plays with, as SDL writes it (xemu keeps its buttons by it), when SDL can be read.</summary>
        public static string? FirstControllerGuid(string libraryFile)
        {
            try
            {
                using var gamepad = new RetroGamepad(libraryFile);
                gamepad.Refresh();
                if (gamepad.Name == null)
                    return null;
                JoystickGuid guid = gamepad.Function<GuidOfInt>("SDL_JoystickGetDeviceGUID")(gamepad.Index);
                return Convert.ToHexString(BitConverter.GetBytes(guid.Low)).ToLowerInvariant() + Convert.ToHexString(BitConverter.GetBytes(guid.High)).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                return null;
            }
        }

        private void Close()
        {
            if (controller != IntPtr.Zero)
                closeController(controller);
            if (joystick != IntPtr.Zero)
                closeJoystick(joystick);
            controller = joystick = IntPtr.Zero;
            Name = null;
        }

        private int ButtonCount => IsStandard ? ControllerButtons : joystick == IntPtr.Zero ? 0 : buttons(joystick);
        private int AxisCount => IsStandard ? ControllerAxes : joystick == IntPtr.Zero ? 0 : axes(joystick);
        private int HatCount => IsStandard || joystick == IntPtr.Zero ? 0 : hats(joystick);
        private byte Button(int i) => IsStandard ? controllerButton(controller, i) : joystickButton(joystick, i);
        private short Axis(int i) => IsStandard ? controllerAxis(controller, i) : joystickAxis(joystick, i);

        /// <summary>What the gamepad holds now is its rest: only a change from it is a press (a trigger rests at an end).</summary>
        public void Rest()
        {
            pump();
            lastButtons = new byte[ButtonCount];
            for (int i = 0; i < lastButtons.Length; i++)
                lastButtons[i] = Button(i);
            restAxes = new short[AxisCount];
            for (int i = 0; i < restAxes.Length; i++)
                restAxes[i] = Axis(i);
            heldAxes = new bool[restAxes.Length];
            lastHats = new byte[HatCount];
            for (int i = 0; i < lastHats.Length; i++)
                lastHats[i] = joystickHat(joystick, i);
        }

        /// <summary>A button just pressed, an axis pushed from its rest or a hat's direction; null for none.</summary>
        public RetroPad.Input? Poll()
        {
            Refresh();
            for (int i = 0; i < lastButtons.Length; i++)
            {
                byte now = Button(i);
                bool down = now != 0 && lastButtons[i] == 0;
                lastButtons[i] = now;
                if (down)
                    return RetroPad.Button(i, IsStandard);
            }
            for (int i = 0; i < restAxes.Length; i++)
            {
                short now = Axis(i);
                // Pushed once until it is back at its rest: a trigger let go is not a press the other way
                if (Math.Abs(now - restAxes[i]) > Moved && Math.Abs((int)now) > Pressed && !heldAxes[i])
                {
                    heldAxes[i] = true;
                    return RetroPad.Axis(i, now > 0, IsStandard);
                }
                if (Math.Abs(now - restAxes[i]) < Moved / 2)
                    heldAxes[i] = false;
            }
            for (int i = 0; i < lastHats.Length; i++)
            {
                byte now = joystickHat(joystick, i);
                byte pressed = (byte)(now & ~lastHats[i]);
                lastHats[i] = now;
                string? direction = (pressed & 1) != 0 ? "up" : (pressed & 2) != 0 ? "right" : (pressed & 4) != 0 ? "down" : (pressed & 8) != 0 ? "left" : null;
                if (direction != null)
                    return RetroPad.Hat(i, direction);
            }
            return null;
        }

        /// <summary>The gamepad plugged now, after the window was opened.</summary>
        public void Update() => Refresh();

        public void Dispose()
        {
            try
            {
                Close();
                quit(InitGameController);
            }
            catch (Exception ex) { Log.Ignored(ex); }
        }
    }
}
