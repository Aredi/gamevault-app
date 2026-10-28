using Android.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Android.Retro
{
    /// <summary>
    /// A game played in the app: the core runs on its own thread at the console's speed (the sound's pace), its pictures
    /// are kept for the screen, the controls are read from the touch pad and the controllers, the save is written on
    /// pause, every half minute and at the end.
    /// </summary>
    internal sealed unsafe class RetroPlayer
    {
        // RetroPad buttons (libretro's ids)
        public const int B = 0, Y = 1, Select = 2, Start = 3, Up = 4, Down = 5, Left = 6, Right = 7, A = 8, X = 9, L = 10, R = 11, L2 = 12, R2 = 13, L3 = 14, R3 = 15;

        private readonly Helper.Platform.BuiltInGame game;
        private readonly string coreFile;
        private readonly TaskCompletionSource done = new();
        private LibretroCore? core;
        private Thread? thread;
        private volatile bool running, paused;
        private readonly ManualResetEventSlim resume = new(true);
        private AudioTrack? audio;
        private short[] audioBuffer = new short[8192];
        private IntPtr romData;
        private string? extracted;
        private byte[]? lastSave;

        /// <summary>The buttons held on each of the 4 ports (bit = RetroPad id): controllers, and the touch pad on port 0.</summary>
        public readonly int[] Buttons = new int[4];
        private readonly int[] tapped = new int[4], tappedThisFrame = new int[4];

        /// <summary>A controller's buttons on its port; a press shorter than a frame is still seen once.</summary>
        public void SetButtons(int port, int held)
        {
            Buttons[port] = held;
            Interlocked.Or(ref tapped[port], held);
        }
        public int TouchButtons => touchHeld;
        private volatile int touchHeld;
        // A tap shorter than a frame is still seen once by the game
        private int touchTapped, touchThisFrame;

        public void SetTouchButtons(int held)
        {
            touchHeld = held;
            Interlocked.Or(ref touchTapped, held);
        }
        /// <summary>Analog sticks: [port, stick * 2 + axis], -32767..32767.</summary>
        public readonly short[,] Sticks = new short[4, 4];
        /// <summary>A finger on the game's picture (the DS's touch screen): -32767..32767, or none.</summary>
        public volatile int PointerX, PointerY, PointerDown;

        public Task Completion => done.Task;
        public FrameBuffer Frame { get; } = new();
        public float AspectRatio { get; private set; } = 4f / 3f;
        /// <summary>Select+Start (or the menu's Quit): the game ends.</summary>
        public event Action? QuitRequested;
        public string Error { get; private set; } = "";

        public RetroPlayer(Helper.Platform.BuiltInGame game, string coreFile)
        {
            this.game = game;
            this.coreFile = coreFile;
        }

        /// <summary>Loads the core and the game on the player's thread, then runs it.</summary>
        public Task<bool> StartAsync()
        {
            var started = new TaskCompletionSource<bool>();
            // The emulators' own threads need room (melonDS)
            thread = new Thread(() => Loop(started), 16 * 1024 * 1024) { Name = "retro-player", IsBackground = true };
            running = true;
            thread.Start();
            return started.Task;
        }

        private void Loop(TaskCompletionSource<bool> started)
        {
            try
            {
                if (!Load())
                {
                    started.TrySetResult(false);
                    Finish();
                    return;
                }
                started.TrySetResult(true);
                var av = core!.AvInfo;
                double fps = av.Timing.Fps > 1 ? av.Timing.Fps : 60;
                OpenAudio(av.Timing.SampleRate);
                var clock = Stopwatch.StartNew();
                long frame = 0;
                var autosave = Stopwatch.StartNew();
                while (running)
                {
                    if (paused)
                    {
                        resume.Wait();
                        clock.Restart();
                        frame = 0;
                        continue;
                    }
                    touchThisFrame = Interlocked.Exchange(ref touchTapped, 0);
                    for (int port = 0; port < 4; port++)
                        tappedThisFrame[port] = Interlocked.Exchange(ref tapped[port], 0);
                    core.Run();
                    UpdateGeometry();
                    if ((Buttons.Any(b => (b & (1 << Select)) != 0 && (b & (1 << Start)) != 0)))
                    {
                        QuitRequested?.Invoke();
                        // Once: the buttons are still held while the game ends
                        Array.Clear(Buttons);
                    }
                    if (autosave.Elapsed > TimeSpan.FromSeconds(30))
                    {
                        WriteSave();
                        autosave.Restart();
                    }
                    // The sound sets the pace (its writes wait while its buffer is full): a clock as well would leave it
                    // without samples now and then, heard as cuts. Without sound, the clock
                    if (audio != null)
                        continue;
                    frame++;
                    double ahead = frame / fps - clock.Elapsed.TotalSeconds;
                    if (ahead > 0.002)
                        Thread.Sleep(TimeSpan.FromSeconds(Math.Min(ahead, 0.1)));
                    else if (ahead < -0.25)
                    {
                        // Far behind (the app was slowed down): starts again from now instead of running fast
                        clock.Restart();
                        frame = 0;
                    }
                }
                WriteSave();
            }
            catch (Exception ex)
            {
                GameVault.Core.Log.Error(ex, "Built-in player");
                Error = ex.Message;
                started.TrySetResult(false);
            }
            Finish();
        }

        private bool Load()
        {
            string saves = Path.GetDirectoryName(game.SaveFile)!;
            Directory.CreateDirectory(saves);
            Directory.CreateDirectory(game.SystemDirectory);
            core = new LibretroCore(coreFile, game.SystemDirectory, saves, Language(game.Language));
            core.Video = (data, width, height, pitch) =>
            {
                // RETRO_HW_FRAME_BUFFER_VALID: drawn in the GPU's framebuffer, read back
                if (data == new IntPtr(-1) && core.Gl is GlContext gl)
                {
                    gl.Read((int)width, (int)height);
                    Frame.WriteRgba(gl.Pixels, (int)width, (int)height, core.BottomLeftOrigin);
                }
                else if (data != IntPtr.Zero)
                    Frame.Write(data, (int)width, (int)height, pitch, core.Format);
            };
            core.Audio = WriteAudio;
            core.Input = InputState;

            string rom = PlayableRom(game.Rom, core.ValidExtensions);
            long size = 0;
            if (core.Info.NeedFullpath == 0)
            {
                byte[] content = File.ReadAllBytes(rom);
                size = content.Length;
                romData = Marshal.AllocHGlobal(content.Length);
                Marshal.Copy(content, 0, romData, content.Length);
            }
            if (!core.LoadGame(rom, romData, size))
            {
                Error = "The core could not load the game";
                return false;
            }
            UpdateGeometry();
            ReadSave();
            return true;
        }

        /// <summary>A zipped game for a core that reads no zip: its game file, unpacked once in the cache.</summary>
        private string PlayableRom(string rom, string extensions)
        {
            var valid = extensions.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(e => e.ToLowerInvariant()).ToHashSet();
            if (!string.Equals(Path.GetExtension(rom), ".zip", StringComparison.OrdinalIgnoreCase) || valid.Contains("zip"))
                return rom;
            using ZipArchive zip = ZipFile.OpenRead(rom);
            ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => valid.Contains(Path.GetExtension(e.Name).TrimStart('.').ToLowerInvariant()))
                ?? throw new InvalidDataException("No game file in the archive");
            extracted = Path.Combine(Path.GetTempPath(), "retro-game", Path.GetFileName(entry.Name));
            Directory.CreateDirectory(Path.GetDirectoryName(extracted)!);
            entry.ExtractToFile(extracted, true);
            return extracted;
        }

        private static uint Language(string language) => language switch
        {
            "Japanese" => 1,
            "French" => 2,
            "Spanish" => 3,
            "German" => 4,
            "Italian" => 5,
            _ => 0,
        };

        private void UpdateGeometry()
        {
            var geometry = core!.AvInfo.Geometry;
            float ratio = geometry.AspectRatio > 0 ? geometry.AspectRatio : geometry.BaseHeight > 0 ? (float)geometry.BaseWidth / geometry.BaseHeight : 4f / 3f;
            AspectRatio = ratio;
        }

        private short InputState(uint port, uint device, uint index, uint id)
        {
            if (port >= 4)
                return 0;
            switch (device & 0xff)
            {
                case LibretroCore.DeviceJoypad:
                    int held = Buttons[port] | tappedThisFrame[port] | (port == 0 ? touchHeld | touchThisFrame : 0);
                    return id == LibretroCore.JoypadMask ? (short)held : (short)((held >> (int)id) & 1);
                case LibretroCore.DeviceAnalog:
                    if (index >= 2 || id >= 2)
                        return 0;
                    short stick = Sticks[port, index * 2 + id];
                    // The touch pad's cross also moves the left stick (3D games walk with it)
                    if (stick == 0 && port == 0 && index == 0)
                    {
                        int cross = touchHeld | touchThisFrame;
                        int low = id == 0 ? Left : Up, high = id == 0 ? Right : Down;
                        if ((cross & (1 << low)) != 0) return -32767;
                        if ((cross & (1 << high)) != 0) return 32767;
                    }
                    return stick;
                case LibretroCore.DevicePointer:
                    if (port != 0)
                        return 0;
                    return id switch { 0 => (short)PointerX, 1 => (short)PointerY, 2 => (short)PointerDown, _ => 0 };
                default:
                    return 0;
            }
        }

        private void OpenAudio(double sampleRate)
        {
            try
            {
                int rate = (int)Math.Round(sampleRate > 0 ? sampleRate : 44100);
                int minimum = AudioTrack.GetMinBufferSize(rate, ChannelOut.Stereo, Encoding.Pcm16bit);
                // About 80 ms: small enough for the controls to feel right; twice for the GPU's consoles, whose pictures
                // come less evenly (GameCube, Wii)
                int bytes = Math.Max(minimum * 2, rate / (core?.Gl != null ? 6 : 12) * 4);
                audio = new AudioTrack.Builder()
                    .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game)!.SetContentType(AudioContentType.Music)!.Build()!)!
                    .SetAudioFormat(new AudioFormat.Builder().SetSampleRate(rate)!.SetEncoding(Encoding.Pcm16bit)!.SetChannelMask(ChannelOut.Stereo)!.Build()!)!
                    .SetBufferSizeInBytes(bytes)!
                    .SetTransferMode(AudioTrackMode.Stream)!
                    .Build();
                audio.Play();
            }
            catch (Exception ex)
            {
                GameVault.Core.Log.Error(ex, "No sound for the built-in player");
                audio = null;
            }
        }

        private void WriteAudio(IntPtr data, int frames)
        {
            if (audio == null || frames <= 0)
                return;
            int samples = frames * 2;
            if (audioBuffer.Length < samples)
                audioBuffer = new short[samples];
            Marshal.Copy(data, audioBuffer, 0, samples);
            try { audio.Write(audioBuffer, 0, samples); }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        private void ReadSave()
        {
            try
            {
                Span<byte> memory = core!.Memory(LibretroCore.MemorySaveRam);
                if (memory.IsEmpty || !File.Exists(game.SaveFile))
                    return;
                byte[] saved = File.ReadAllBytes(game.SaveFile);
                saved.AsSpan(0, Math.Min(saved.Length, memory.Length)).CopyTo(memory);
                lastSave = memory.ToArray();
            }
            catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
        }

        /// <summary>The cartridge's memory, written when it changed (from the player's thread: the core is not running).</summary>
        private void WriteSave()
        {
            try
            {
                if (core == null)
                    return;
                Span<byte> memory = core.Memory(LibretroCore.MemorySaveRam);
                if (memory.IsEmpty || (lastSave != null && memory.SequenceEqual(lastSave)))
                    return;
                // A game that never wrote its save has none yet: no empty file
                if (lastSave == null && !File.Exists(game.SaveFile) && !memory.ContainsAnyExcept((byte)0))
                    return;
                byte[] content = memory.ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(game.SaveFile)!);
                string part = game.SaveFile + ".part";
                File.WriteAllBytes(part, content);
                File.Move(part, game.SaveFile, true);
                lastSave = content;
            }
            catch (Exception ex) { GameVault.Core.Log.Error(ex, "The built-in player's save was not written"); }
        }

        /// <summary>The app left the screen: the game waits (and its save is written), the sound stops.</summary>
        public void Pause(bool pause)
        {
            if (pause == paused)
                return;
            paused = pause;
            if (pause)
            {
                resume.Reset();
                try { audio?.Pause(); } catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                // Written by the player's thread once it waits; also from here, as the app may be closed
                Task.Delay(100).ContinueWith(_ => WriteSaveWhilePaused(), TaskScheduler.Default);
            }
            else
            {
                try { audio?.Play(); } catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                resume.Set();
            }
        }
        private void WriteSaveWhilePaused()
        {
            if (paused && running)
                WriteSave();
        }

        public void Stop()
        {
            running = false;
            resume.Set();
        }

        private void Finish()
        {
            try
            {
                running = false;
                try { audio?.Stop(); audio?.Release(); } catch (Exception ex) { GameVault.Core.Log.Ignored(ex); }
                if (core != null)
                {
                    core.UnloadGame();
                    core.Dispose();
                }
                if (romData != IntPtr.Zero)
                    Marshal.FreeHGlobal(romData);
                if (extracted != null)
                    File.Delete(extracted);
            }
            catch (Exception ex) { GameVault.Core.Log.Error(ex, "Built-in player end"); }
            done.TrySetResult();
        }
    }

    /// <summary>
    /// The last picture of the core, as RGBA pixels for the screen (Skia's own order on Android: BGRA pictures stay black
    /// there); written by the player's thread.
    /// </summary>
    internal sealed unsafe class FrameBuffer
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int[] Pixels { get; private set; } = Array.Empty<int>();
        /// <summary>Counts the pictures: the screen copies a new one only.</summary>
        public long Version { get; private set; }

        /// <summary>A picture read from the GPU: RGBA already, its lines turned upright when it starts at the bottom.</summary>
        public void WriteRgba(byte[] rgba, int width, int height, bool bottomFirst)
        {
            lock (this)
            {
                if (Pixels.Length < width * height)
                    Pixels = new int[width * height];
                Width = width;
                Height = height;
                fixed (byte* source = rgba)
                fixed (int* target = Pixels)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int* from = (int*)source + (bottomFirst ? height - 1 - y : y) * width;
                        Buffer.MemoryCopy(from, target + y * width, width * 4, width * 4);
                    }
                    // Opaque: the GPU's alpha is the game's, not the screen's
                    for (int i = 0, n = width * height; i < n; i++)
                        target[i] |= unchecked((int)0xFF000000);
                }
                Version++;
            }
        }

        public void Write(IntPtr data, int width, int height, int pitch, LibretroCore.PixelFormat format)
        {
            lock (this)
            {
                if (Pixels.Length < width * height)
                    Pixels = new int[width * height];
                Width = width;
                Height = height;
                byte* source = (byte*)data;
                fixed (int* target = Pixels)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int* line = target + y * width;
                        switch (format)
                        {
                            case LibretroCore.PixelFormat.Xrgb8888:
                                {
                                    uint* row = (uint*)(source + y * pitch);
                                    for (int x = 0; x < width; x++)
                                    {
                                        uint p = row[x];
                                        line[x] = (int)(0xFF000000 | (p & 0xFF) << 16 | (p & 0xFF00) | (p >> 16) & 0xFF);
                                    }
                                    break;
                                }
                            case LibretroCore.PixelFormat.Rgb565:
                                {
                                    ushort* row = (ushort*)(source + y * pitch);
                                    for (int x = 0; x < width; x++)
                                    {
                                        int p = row[x];
                                        int r = (p >> 11) & 31, g = (p >> 5) & 63, b = p & 31;
                                        line[x] = unchecked((int)0xFF000000) | (b << 3 | b >> 2) << 16 | (g << 2 | g >> 4) << 8 | (r << 3 | r >> 2);
                                    }
                                    break;
                                }
                            default:
                                {
                                    ushort* row = (ushort*)(source + y * pitch);
                                    for (int x = 0; x < width; x++)
                                    {
                                        int p = row[x];
                                        int r = (p >> 10) & 31, g = (p >> 5) & 31, b = p & 31;
                                        line[x] = unchecked((int)0xFF000000) | (b << 3 | b >> 2) << 16 | (g << 3 | g >> 2) << 8 | (r << 3 | r >> 2);
                                    }
                                    break;
                                }
                        }
                    }
                }
                Version++;
            }
        }
    }
}
