using System;
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
            library = NativeLibrary.Load(file);
            this.systemDirectory = Marshal.StringToCoTaskMemUTF8(systemDirectory);
            this.saveDirectory = Marshal.StringToCoTaskMemUTF8(saveDirectory);
            this.language = language;
            environment = Environment;
            video = (data, width, height, pitch) => Video?.Invoke(data, width, height, (int)pitch);
            audioSample = AudioSample;
            audioBatch = (data, frames) => { Audio?.Invoke(data, (int)frames); return frames; };
            inputPoll = () => { };
            inputState = (port, device, index, id) => Input?.Invoke(port, device, index, id) ?? 0;

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
            if (loaded)
            {
                SystemAvInfo av;
                ((delegate* unmanaged[Cdecl]<SystemAvInfo*, void>)Export("retro_get_system_av_info"))(&av);
                AvInfo = av;
            }
            return loaded;
        }
        private IntPtr gamePath;

        public void Run() => run();

        public Span<byte> Memory(uint kind)
        {
            IntPtr data = ((delegate* unmanaged[Cdecl]<uint, IntPtr>)Export("retro_get_memory_data"))(kind);
            nuint size = ((delegate* unmanaged[Cdecl]<uint, nuint>)Export("retro_get_memory_size"))(kind);
            return data == IntPtr.Zero || size == 0 ? Span<byte>.Empty : new Span<byte>((void*)data, (int)size);
        }

        public void UnloadGame() => ((delegate* unmanaged[Cdecl]<void>)Export("retro_unload_game"))();

        public void Dispose()
        {
            ((delegate* unmanaged[Cdecl]<void>)Export("retro_deinit"))();
            NativeLibrary.Free(library);
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
                    case 3:// GET_CAN_DUPE
                        *(byte*)data = 1;
                        return true;
                    case 6:// SET_MESSAGE
                    case 8:// SET_PERFORMANCE_LEVEL
                    case 11:// SET_INPUT_DESCRIPTORS
                    case 13:// SET_DISK_CONTROL_INTERFACE
                    case 16:// SET_VARIABLES: the cores' defaults are used
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
                    case 15:// GET_VARIABLE: the default
                        ((Variable*)data)->Value = IntPtr.Zero;
                        return false;
                    case 17:// GET_VARIABLE_UPDATE
                        *(byte*)data = 0;
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
