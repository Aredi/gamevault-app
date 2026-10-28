using System;
using System.Runtime.InteropServices;

namespace gamevault.Android.Retro
{
    /// <summary>
    /// An OpenGL ES 3 context of the player's own (an off-screen surface) and the framebuffer the core draws into
    /// (libretro's hardware rendering: GameCube and Wii). Each picture is read back and shown as the other consoles'.
    /// Made, used and destroyed on the player's thread only.
    /// </summary>
    internal sealed unsafe class GlContext : IDisposable
    {
        private const string Egl = "libEGL.so", Gles = "libGLESv3.so";
        private const int EGL_NONE = 0x3038, EGL_RENDERABLE_TYPE = 0x3040, EGL_OPENGL_ES3_BIT = 0x40, EGL_SURFACE_TYPE = 0x3033, EGL_PBUFFER_BIT = 0x1,
            EGL_RED_SIZE = 0x3024, EGL_GREEN_SIZE = 0x3023, EGL_BLUE_SIZE = 0x3022, EGL_ALPHA_SIZE = 0x3021, EGL_DEPTH_SIZE = 0x3025, EGL_STENCIL_SIZE = 0x3026,
            EGL_CONTEXT_CLIENT_VERSION = 0x3098, EGL_WIDTH = 0x3057, EGL_HEIGHT = 0x3056;
        private const uint GL_FRAMEBUFFER = 0x8D40, GL_TEXTURE_2D = 0x0DE1, GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401, GL_COLOR_ATTACHMENT0 = 0x8CE0,
            GL_RENDERBUFFER = 0x8D41, GL_DEPTH24_STENCIL8 = 0x88F0, GL_DEPTH_STENCIL_ATTACHMENT = 0x821A, GL_TEXTURE_MIN_FILTER = 0x2801,
            GL_NEAREST = 0x2600, GL_PACK_ALIGNMENT = 0x0D05, GL_FRAMEBUFFER_COMPLETE = 0x8CD5;

        [DllImport(Egl)] private static extern IntPtr eglGetDisplay(IntPtr nativeDisplay);
        [DllImport(Egl)] private static extern bool eglInitialize(IntPtr display, out int major, out int minor);
        [DllImport(Egl)] private static extern bool eglChooseConfig(IntPtr display, int[] attributes, IntPtr* configs, int size, out int count);
        [DllImport(Egl)] private static extern IntPtr eglCreateContext(IntPtr display, IntPtr config, IntPtr share, int[] attributes);
        [DllImport(Egl)] private static extern IntPtr eglCreatePbufferSurface(IntPtr display, IntPtr config, int[] attributes);
        [DllImport(Egl)] private static extern bool eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);
        [DllImport(Egl)] private static extern bool eglDestroyContext(IntPtr display, IntPtr context);
        [DllImport(Egl)] private static extern bool eglDestroySurface(IntPtr display, IntPtr surface);
        [DllImport(Egl)] private static extern IntPtr eglGetProcAddress(string name);

        [DllImport(Gles)] private static extern void glGenFramebuffers(int n, uint* ids);
        [DllImport(Gles)] private static extern void glBindFramebuffer(uint target, uint id);
        [DllImport(Gles)] private static extern void glGenTextures(int n, uint* ids);
        [DllImport(Gles)] private static extern void glBindTexture(uint target, uint id);
        [DllImport(Gles)] private static extern void glTexParameteri(uint target, uint name, int value);
        [DllImport(Gles)] private static extern void glTexImage2D(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, IntPtr data);
        [DllImport(Gles)] private static extern void glFramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level);
        [DllImport(Gles)] private static extern void glGenRenderbuffers(int n, uint* ids);
        [DllImport(Gles)] private static extern void glBindRenderbuffer(uint target, uint id);
        [DllImport(Gles)] private static extern void glRenderbufferStorage(uint target, uint format, int width, int height);
        [DllImport(Gles)] private static extern void glFramebufferRenderbuffer(uint target, uint attachment, uint renderbufferTarget, uint renderbuffer);
        [DllImport(Gles)] private static extern uint glCheckFramebufferStatus(uint target);
        [DllImport(Gles)] private static extern void glPixelStorei(uint name, int value);
        [DllImport(Gles)] private static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, IntPtr data);
        [DllImport(Gles)] private static extern void glDeleteFramebuffers(int n, uint* ids);
        [DllImport(Gles)] private static extern void glDeleteTextures(int n, uint* ids);
        [DllImport(Gles)] private static extern void glDeleteRenderbuffers(int n, uint* ids);

        private readonly IntPtr display, context, surface;
        private readonly IntPtr glesLibrary;
        private uint framebuffer, texture, depth;
        private byte[] readBack = Array.Empty<byte>();

        public uint Framebuffer => framebuffer;

        public GlContext()
        {
            // The display Avalonia uses too: never terminated here
            display = eglGetDisplay(IntPtr.Zero);
            if (!eglInitialize(display, out _, out _))
                throw new InvalidOperationException("EGL could not be initialized");
            int[] wanted = { EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT, EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8,
                EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_DEPTH_SIZE, 24, EGL_STENCIL_SIZE, 8, EGL_NONE };
            IntPtr config;
            if (!eglChooseConfig(display, wanted, &config, 1, out int count) || count == 0)
                throw new InvalidOperationException("No OpenGL ES 3 configuration");
            context = eglCreateContext(display, config, IntPtr.Zero, new[] { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE });
            surface = eglCreatePbufferSurface(display, config, new[] { EGL_WIDTH, 16, EGL_HEIGHT, 16, EGL_NONE });
            if (context == IntPtr.Zero || surface == IntPtr.Zero || !eglMakeCurrent(display, surface, surface, context))
                throw new InvalidOperationException("The OpenGL ES 3 context could not be made");
            glesLibrary = NativeLibrary.Load(Gles);
        }

        /// <summary>The core's GL functions: EGL's, else the library's (core functions are not always given by EGL).</summary>
        public IntPtr ProcAddress(string name)
        {
            IntPtr address = eglGetProcAddress(name);
            if (address == IntPtr.Zero && NativeLibrary.TryGetExport(glesLibrary, name, out IntPtr exported))
                address = exported;
            return address;
        }

        /// <summary>The framebuffer at the core's largest picture (its colour as a texture, depth and stencil).</summary>
        public void CreateFramebuffer(int width, int height)
        {
            DeleteFramebuffer();
            uint id;
            glGenFramebuffers(1, &id);
            framebuffer = id;
            glBindFramebuffer(GL_FRAMEBUFFER, framebuffer);
            glGenTextures(1, &id);
            texture = id;
            glBindTexture(GL_TEXTURE_2D, texture);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
            glGenRenderbuffers(1, &id);
            depth = id;
            glBindRenderbuffer(GL_RENDERBUFFER, depth);
            glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH24_STENCIL8, width, height);
            glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_RENDERBUFFER, depth);
            if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException("The framebuffer is not complete");
        }

        /// <summary>The picture the core drew, into <see cref="Pixels"/> (RGBA, the framebuffer's bottom line first).</summary>
        public void Read(int width, int height)
        {
            if (readBack.Length < width * height * 4)
                readBack = new byte[width * height * 4];
            glBindFramebuffer(GL_FRAMEBUFFER, framebuffer);
            glPixelStorei(GL_PACK_ALIGNMENT, 1);
            fixed (byte* pixels = readBack)
                glReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, (IntPtr)pixels);
        }

        public byte[] Pixels => readBack;

        private void DeleteFramebuffer()
        {
            uint id;
            if (framebuffer != 0) { id = framebuffer; glDeleteFramebuffers(1, &id); framebuffer = 0; }
            if (texture != 0) { id = texture; glDeleteTextures(1, &id); texture = 0; }
            if (depth != 0) { id = depth; glDeleteRenderbuffers(1, &id); depth = 0; }
        }

        public void Dispose()
        {
            DeleteFramebuffer();
            eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            eglDestroySurface(display, surface);
            eglDestroyContext(display, context);
            NativeLibrary.Free(glesLibrary);
        }
    }
}
