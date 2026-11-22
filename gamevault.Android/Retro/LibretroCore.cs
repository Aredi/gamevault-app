using System;
using System.IO;
using System.Runtime.InteropServices;

namespace gamevault.Android.Retro
{
    /// <summary>
    /// A libretro core (the "_libretro_android.so" of libretro's buildbot) loaded in the app: its functions, and the
    /// callbacks it calls (pictures, sound, controls, questions about the app). See libretro.h.
    /// </summary>
    internal sealed unsafe class LibretroCore : IDisposable
    {
        public const uint DeviceJoypad = 1, DeviceAnalog = 5, DevicePointer = 6;
        public const uint JoypadMask = 256;
        public const uint MemorySaveRam = 0;

        public enum PixelFormat { Rgb1555 = 0, Xrgb8888 = 1, Rgb565 = 2 }

        [StructLayout(LayoutKind.Sequential)]
        public struct SystemInfo { public IntPtr LibraryName, LibraryVersion, ValidExtensions; public byte NeedFullpath, BlockExtract; }

        [StructLayout(LayoutKind.Sequential)]
        public struct GameInfo { public IntPtr Path, Data; public nuint Size; public IntPtr Meta; }

        [StructLayout(LayoutKind.Sequential)]
        public struct GameGeometry { public uint BaseWidth, BaseHeight, MaxWidth, MaxHeight; public float AspectRatio; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SystemTiming { public double Fps, SampleRate; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SystemAvInfo { public GameGeometry Geometry; public SystemTiming Timing; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Variable { public IntPtr Key, Value; }

        /// <summary>retro_hw_render_callback: what a core drawing with the GPU asks for, and what it is given.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct HwRender
        {
            public int ContextType;
            public IntPtr ContextReset, GetCurrentFramebuffer, GetProcAddress;
            public byte Depth, Stencil, BottomLeftOrigin;
            public uint VersionMajor, VersionMinor;
            public byte CacheContext;
            public IntPtr ContextDestroy;
            public byte DebugContext;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nuint CurrentFramebufferFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ProcAddressFn([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        private readonly CurrentFramebufferFn currentFramebuffer;
        private readonly ProcAddressFn procAddress;
        private IntPtr contextReset, contextDestroy;

        /// <summary>The GPU context of a core that draws with it (GameCube, Wii), else null.</summary>
        public GlContext? Gl { get; private set; }
        /// <summary>The core's picture starts at its bottom line (OpenGL's order): turned upright when shown.</summary>
        public bool BottomLeftOrigin { get; private set; }

        /// <summary>
        /// Options the app sets itself (else the cores' defaults). Dolphin's fast memory catches the process's memory
        /// faults, which .NET's runtime uses too.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> Options = new()
        {
            ["dolphin_fastmem"] = "disabled",
            ["dolphin_fastmem_arena"] = "disabled",
            // Citra: the bottom screen touched on the phone's screen (libretro's pointer), not with a mouse
            ["citra_touch_touchscreen"] = "enabled",
            ["citra_mouse_touchscreen"] = "disabled",
        };
        /// <summary>The values given to the core, by their text: kept until the end, a core may hold one.</summary>
        private readonly System.Collections.Generic.Dictionary<string, IntPtr> optionValues = new();
        /// <summary>This device's own options (the PS2's renderer without OpenGL ES 3.2), and the player's (see SetOption).</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> overrides = new();
        /// <summary>Each option's default, as the core declared it ("Description; default|other|..."): the BIOS of LRPS2 is one.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> defaults = new();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool EnvironmentFn(uint command, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VideoRefreshFn(IntPtr data, uint width, uint height, nuint pitch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AudioSampleFn(short left, short right);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nuint AudioSampleBatchFn(IntPtr data, nuint frames);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InputPollFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate short InputStateFn(uint port, uint device, uint index, uint id);

        private readonly IntPtr library;
        // Kept: the core holds pointers to them
        private readonly EnvironmentFn environment;
        private readonly VideoRefreshFn video;
        private readonly AudioSampleFn audioSample;
        private readonly AudioSampleBatchFn audioBatch;
        private readonly InputPollFn inputPoll;
        private readonly InputStateFn inputState;
        private readonly IntPtr systemDirectory, saveDirectory;
        private readonly uint language;

        private readonly delegate* unmanaged[Cdecl]<void> run;

        /// <summary>A picture: its pixels (null: the same as before), width, height, bytes per line.</summary>
        public Action<IntPtr, uint, uint, int>? Video { get; set; }
        /// <summary>Sound: interleaved stereo 16-bit samples, their number of frames.</summary>
        public Action<IntPtr, int>? Audio { get; set; }
        public Func<uint, uint, uint, uint, short>? Input { get; set; }

        public PixelFormat Format { get; private set; } = PixelFormat.Rgb1555;
        public SystemAvInfo AvInfo { get; private set; }
        public SystemInfo Info { get; }
        public string LibraryName { get; }
        public string ValidExtensions { get; }

        /// <param name="language">libretro's language number (0 English, 2 French...).</param>
        public LibretroCore(string file, string systemDirectory, string saveDirectory, uint language)
        {
            string name = Path.GetFileName(file);
            // Without OpenGL ES 3.2: LRPS2 draws with the CPU (its GPU renderer would show nothing); Citra has no other
            // renderer and stops the whole app
            if (name.StartsWith("citra_", StringComparison.Ordinal) && !GlContext.IsModern)
                throw new InvalidOperationException(gamevault.Localization.Loc.T("The 3DS needs OpenGL ES 3.2, which this device's GPU does not have."));
            if (name.StartsWith("pcsx2_", StringComparison.Ordinal) && !GlContext.IsModern)
                overrides["pcsx2_renderer"] = "Software (SW)";
            // ARMSX2: OpenGL ES (the player has no Vulkan), the CPU without 3.2
            if (name.StartsWith("armsx2_", StringComparison.Ordinal))
                overrides["armsx2_renderer"] = GlContext.IsModern ? "OpenGL" : "Software";
            // LRPS2: free room next to it for its recompilers' maps
            library = (Path.GetFileName(file).StartsWith("pcsx2_", StringComparison.Ordinal) ? NearLoader.Load(file) : null) ?? NativeLibrary.Load(file);
            this.systemDirectory = Marshal.StringToCoTaskMemUTF8(systemDirectory);
            this.saveDirectory = Marshal.StringToCoTaskMemUTF8(saveDirectory);
            this.language = language;
            environment = Environment;
            video = (data, width, height, pitch) => Video?.Invoke(data, width, height, (int)pitch);
            audioSample = AudioSample;
            audioBatch = (data, frames) => { Audio?.Invoke(data, (int)frames); return frames; };
            inputPoll = () => { };
            inputState = (port, device, index, id) => Input?.Invoke(port, device, index, id) ?? 0;
            currentFramebuffer = () => Gl?.Framebuffer ?? 0;
            procAddress = name => Gl?.ProcAddress(name) ?? IntPtr.Zero;

            ((delegate* unmanaged[Cdecl]<IntPtr, void>)Export("retro_set_environment"))(Marshal.GetFunctionPointerForDelegate(environment));
            // As RetroArch: init right after the environment, then the callbacks (some cores keep them in objects made by init)
            ((delegate* unmanaged[Cdecl]<void>)Export("retro_init"))();
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)Export("retro_set_video_refresh"))(Marshal.GetFunctionPointerForDelegate(video));
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)Export("retro_set_audio_sample"))(Marshal.GetFunctionPointerForDelegate(audioSample));
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)Export("retro_set_audio_sample_batch"))(Marshal.GetFunctionPointerForDelegate(audioBatch));
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)Export("retro_set_input_poll"))(Marshal.GetFunctionPointerForDelegate(inputPoll));
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)Export("retro_set_input_state"))(Marshal.GetFunctionPointerForDelegate(inputState));
            run = (delegate* unmanaged[Cdecl]<void>)Export("retro_run");

            SystemInfo info;
            ((delegate* unmanaged[Cdecl]<SystemInfo*, void>)Export("retro_get_system_info"))(&info);
            Info = info;
            LibraryName = Marshal.PtrToStringUTF8(info.LibraryName) ?? "";
            ValidExtensions = Marshal.PtrToStringUTF8(info.ValidExtensions) ?? "";
        }

        private IntPtr Export(string name) => NativeLibrary.GetExport(library, name);

        public bool LoadGame(string? path, IntPtr data, long size)
        {
            IntPtr pathText = path != null ? Marshal.StringToCoTaskMemUTF8(path) : IntPtr.Zero;
            var game = new GameInfo { Path = pathText, Data = data, Size = (nuint)size };
            // Kept until the game is unloaded: some cores keep the path
            gamePath = pathText;
            bool loaded = ((delegate* unmanaged[Cdecl]<GameInfo*, byte>)Export("retro_load_game"))(&game) != 0;
            gameLoaded = loaded;
            if (loaded)
            {
                SystemAvInfo av;
                ((delegate* unmanaged[Cdecl]<SystemAvInfo*, void>)Export("retro_get_system_av_info"))(&av);
                AvInfo = av;
                // A controller on each port, as RetroArch does: Dolphin has none until told (its pads did not answer)
                var setDevice = (delegate* unmanaged[Cdecl]<uint, uint, void>)Export("retro_set_controller_port_device");
                for (uint port = 0; port < 4; port++)
                    setDevice(port, DeviceJoypad);
                if (Gl != null)
                {
                    // Its largest picture, then the core makes its GPU objects
                    Gl.CreateFramebuffer((int)Math.Max(av.Geometry.MaxWidth, av.Geometry.BaseWidth), (int)Math.Max(av.Geometry.MaxHeight, av.Geometry.BaseHeight));
                    if (contextReset != IntPtr.Zero)
                        ((delegate* unmanaged[Cdecl]<void>)contextReset)();
                }
            }
            return loaded;
        }
        private IntPtr gamePath;

        public void Run() => run();

        private bool optionsChanged;

        /// <summary>An option of the app's own, from the player's thread: before the game is loaded, or during it (the core reads it again).</summary>
        public void SetOption(string key, string value)
        {
            if (overrides.TryGetValue(key, out string? current) && current == value)
                return;
            overrides[key] = value;
            optionsChanged = gameLoaded;
        }

        public Span<byte> Memory(uint kind)
        {
            IntPtr data = ((delegate* unmanaged[Cdecl]<uint, IntPtr>)Export("retro_get_memory_data"))(kind);
            nuint size = ((delegate* unmanaged[Cdecl]<uint, nuint>)Export("retro_get_memory_size"))(kind);
            return data == IntPtr.Zero || size == 0 ? Span<byte>.Empty : new Span<byte>((void*)data, (int)size);
        }

        /// <summary>The whole state of the game (a save state, as RetroArch's), or null when the core has none.</summary>
        public byte[]? SaveState()
        {
            nuint size = ((delegate* unmanaged[Cdecl]<nuint>)Export("retro_serialize_size"))();
            if (size == 0)
                return null;
            byte[] state = new byte[(int)size];
            fixed (byte* data = state)
                return ((delegate* unmanaged[Cdecl]<IntPtr, nuint, byte>)Export("retro_serialize"))((IntPtr)data, size) != 0 ? state : null;
        }

        /// <summary>The game back where a save state was made.</summary>
        public bool LoadState(byte[] state)
        {
            fixed (byte* data = state)
                return ((delegate* unmanaged[Cdecl]<IntPtr, nuint, byte>)Export("retro_unserialize"))((IntPtr)data, (nuint)state.Length) != 0;
        }

        private bool gameLoaded;

        /// <summary>The game unloaded: only a loaded one (unloading after a failed load crashes some cores).</summary>
        public void UnloadGame()
        {
            if (!gameLoaded)
                return;
            gameLoaded = false;
            // Its GPU objects first, as RetroArch: LRPS2 unloading with them still there never returned
            if (Gl != null && contextDestroy != IntPtr.Zero)
                ((delegate* unmanaged[Cdecl]<void>)contextDestroy)();
            ((delegate* unmanaged[Cdecl]<void>)Export("retro_unload_game"))();
        }

        public void Dispose()
        {
            ((delegate* unmanaged[Cdecl]<void>)Export("retro_deinit"))();
            Gl?.Dispose();
            Gl = null;
            NativeLibrary.Free(library);
            foreach (IntPtr value in optionValues.Values)
                Marshal.FreeCoTaskMem(value);
            Marshal.FreeCoTaskMem(systemDirectory);
            Marshal.FreeCoTaskMem(saveDirectory);
            if (gamePath != IntPtr.Zero)
                Marshal.FreeCoTaskMem(gamePath);
        }

        private short[] single = new short[2];
        private void AudioSample(short left, short right)
        {
            fixed (short* frame = single)
            {
                frame[0] = left;
                frame[1] = right;
                Audio?.Invoke((IntPtr)frame, 1);
            }
        }

        /// <summary>The core's questions: only what a player of 2D consoles needs; the rest is answered "not here".</summary>
        private bool Environment(uint command, IntPtr data)
        {
            try
            {
                switch (command & ~0x10000u)// RETRO_ENVIRONMENT_EXPERIMENTAL
                {
                    case 27:// GET_LOG_INTERFACE: bionic's syslog(priority, format, ...) is libretro's log(level, format, ...), in Android's log
                        if (NativeLibrary.TryGetExport(NativeLibrary.Load("libc.so"), "syslog", out IntPtr syslog))
                        {
                            *(IntPtr*)data = syslog;
                            return true;
                        }
                        return false;
                    case 3:// GET_CAN_DUPE
                        *(byte*)data = 1;
                        return true;
                    case 6:// SET_MESSAGE
                    case 8:// SET_PERFORMANCE_LEVEL
                    case 11:// SET_INPUT_DESCRIPTORS
                    case 13:// SET_DISK_CONTROL_INTERFACE
                    case 35:// SET_CONTROLLER_INFO
                    case 44:// SET_SERIALIZATION_QUIRKS
                        return true;
                    case 9:// GET_SYSTEM_DIRECTORY
                        *(IntPtr*)data = systemDirectory;
                        return true;
                    case 31:// GET_SAVE_DIRECTORY
                        *(IntPtr*)data = saveDirectory;
                        return true;
                    case 10:// SET_PIXEL_FORMAT
                        int format = *(int*)data;
                        if (format is < 0 or > 2)
                            return false;
                        Format = (PixelFormat)format;
                        return true;
                    case 16:// SET_VARIABLES: their defaults, the first of each list
                        for (var variable = (Variable*)data; variable->Key != IntPtr.Zero; variable++)
                        {
                            string description = Marshal.PtrToStringUTF8(variable->Value) ?? "";
                            int values = description.IndexOf("; ", StringComparison.Ordinal);
                            if (values >= 0)
                                defaults[Marshal.PtrToStringUTF8(variable->Key) ?? ""] = description[(values + 2)..].Split('|')[0];
                        }
                        return true;
                    case 15:// GET_VARIABLE: the app's own options, else the core's default
                        {
                            var variable = (Variable*)data;
                            string key = Marshal.PtrToStringUTF8(variable->Key) ?? "";
                            if (!overrides.TryGetValue(key, out string? value) && !Options.TryGetValue(key, out value) && !defaults.TryGetValue(key, out value))
                            {
                                variable->Value = IntPtr.Zero;
                                return false;
                            }
                            if (!optionValues.TryGetValue(value, out IntPtr text))
                                optionValues[value] = text = Marshal.StringToCoTaskMemUTF8(value);
                            variable->Value = text;
                            return true;
                        }
                    case 14:// SET_HW_RENDER: OpenGL ES (2, 3) only, in a context of the player's own
                        {
                            var hw = (HwRender*)data;
                            bool gles = hw->ContextType is 2 or 4 || (hw->ContextType == 5 && hw->VersionMajor <= 3);
                            if (!gles)
                                return false;
                            Gl ??= new GlContext();
                            contextReset = hw->ContextReset;
                            contextDestroy = hw->ContextDestroy;
                            BottomLeftOrigin = hw->BottomLeftOrigin != 0;
                            hw->GetCurrentFramebuffer = Marshal.GetFunctionPointerForDelegate(currentFramebuffer);
                            hw->GetProcAddress = Marshal.GetFunctionPointerForDelegate(procAddress);
                            return true;
                        }
                    case 56:// GET_PREFERRED_HW_RENDER: OpenGL ES 3
                        *(uint*)data = 4;
                        return true;
                    case 17:// GET_VARIABLE_UPDATE: an option changed during the game (see SetOption)
                        *(byte*)data = optionsChanged ? (byte)1 : (byte)0;
                        optionsChanged = false;
                        return true;
                    case 32:// SET_SYSTEM_AV_INFO
                        AvInfo = *(SystemAvInfo*)data;
                        return true;
                    case 37:// SET_GEOMETRY
                        var av = AvInfo;
                        av.Geometry = *(GameGeometry*)data;
                        AvInfo = av;
                        return true;
                    case 39:// GET_LANGUAGE
                        *(uint*)data = language;
                        return true;
                    case 47:// GET_AUDIO_VIDEO_ENABLE
                        *(int*)data = 3;
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                GameVault.Core.Log.Ignored(ex);
                return false;
            }
        }
    }
}
