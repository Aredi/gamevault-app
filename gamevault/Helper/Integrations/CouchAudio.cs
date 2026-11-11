using GameVault.Core;
using GameVault.Core.Couch;
using gamevault.Helper.Platform;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace gamevault.Helper
{
    /// <summary>
    /// Plays the sounds of the living-room mode (<see cref="CouchSounds"/>): with Windows' own player there, with SDL2
    /// (RetroArch's or the system's) elsewhere. Silent where neither can play.
    /// </summary>
    internal sealed class CouchAudio : IDisposable
    {
        private const uint InitAudio = 0x10;
        private const ushort Signed16 = 0x8010;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntOfUInt(uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidOfUInt(uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint OpenDevice(IntPtr device, int capture, ref AudioSpec desired, IntPtr obtained, int changes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PauseDevice(uint device, int pause);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueAudio(uint device, short[] data, uint length);

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioSpec
        {
            public int Frequency;
            public ushort Format;
            public byte Channels, Silence;
            public ushort Samples, Padding;
            public uint Size;
            public IntPtr Callback, UserData;
        }

        private readonly Dictionary<CouchSound, short[]> samples = new();
        // Windows plays them from memory after the call: they stay where they are
        private readonly Dictionary<CouchSound, GCHandle> waves = new();
        private IntPtr library;
        private uint device;
        private VoidOfUInt? quit, clear, close;
        private QueueAudio? queue;
        private bool tried, failed;

        [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
        private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
        private const uint Async = 0x0001, NoDefault = 0x0002, Memory = 0x0004;

        public void Play(CouchSound sound)
        {
            if (failed)
                return;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!waves.TryGetValue(sound, out GCHandle wave))
                        waves[sound] = wave = GCHandle.Alloc(CouchSounds.Wave(sound), GCHandleType.Pinned);
                    PlaySound(wave.AddrOfPinnedObject(), IntPtr.Zero, Async | NoDefault | Memory);
                    return;
                }
                if (!tried)
                {
                    tried = true;
                    OpenSdl();
                }
                if (device == 0)
                    return;
                if (!samples.TryGetValue(sound, out short[]? data))
                    samples[sound] = data = CouchSounds.Samples(sound);
                // A held direction: the sound of the game passed is cut by the next one's
                clear!(device);
                queue!(device, data, (uint)(data.Length * 2));
            }
            catch (Exception ex)
            {
                Log.Ignored(ex);
                failed = true;
            }
        }

        private void OpenSdl()
        {
            if (!(RetroArchManager.Sdl2Library is string known && NativeLibrary.TryLoad(known, out library))
                && !(!PlatformInfo.IsMac && NativeLibrary.TryLoad("libSDL2-2.0.so.0", out library)))
                return;
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            if (Function<IntOfUInt>("SDL_InitSubSystem")(InitAudio) != 0)
                return;
            quit = Function<VoidOfUInt>("SDL_QuitSubSystem");
            var desired = new AudioSpec { Frequency = CouchSounds.Rate, Format = Signed16, Channels = 1, Samples = 1024 };
            device = Function<OpenDevice>("SDL_OpenAudioDevice")(IntPtr.Zero, 0, ref desired, IntPtr.Zero, 0);
            if (device == 0)
                return;
            clear = Function<VoidOfUInt>("SDL_ClearQueuedAudio");
            close = Function<VoidOfUInt>("SDL_CloseAudioDevice");
            queue = Function<QueueAudio>("SDL_QueueAudio");
            Function<PauseDevice>("SDL_PauseAudioDevice")(device, 0);
        }

        public void Dispose()
        {
            try
            {
                if (OperatingSystem.IsWindows() && waves.Count > 0)
                {
                    // Stopped before its memory is given back
                    PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
                    foreach (GCHandle wave in waves.Values)
                        wave.Free();
                    waves.Clear();
                }
                if (device != 0)
                    close!(device);
                if (quit != null)
                    quit(InitAudio);
            }
            catch (Exception ex) { Log.Ignored(ex); }
            device = 0;
            quit = null;
        }
    }
}
