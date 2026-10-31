using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace gamevault.Android.Retro
{
    /// <summary>
    /// Loads a core with free address space right after it. LRPS2's recompilers reach their 625 MB of maps from the
    /// core's code (rel32 on x86-64, ADRP on ARM64), so the maps must lie within 1.9 GB of it; in the app's process the
    /// runtime's reservations leave no such room next to a library loaded anywhere. The core is loaded at the start of a
    /// region reserved beforehand (android_dlopen_ext), and the rest of the region is given back next to it.
    /// </summary>
    internal static unsafe class NearLoader
    {
        private const int PROT_NONE = 0, MAP_PRIVATE = 0x02, MAP_ANONYMOUS = 0x20, MAP_NORESERVE = 0x4000, RTLD_NOW = 2;
        private const ulong ANDROID_DLEXT_RESERVED_ADDRESS = 0x1;
        private const long Region = 3L << 30;

        [StructLayout(LayoutKind.Sequential)]
        private struct DlExtInfo
        {
            public ulong Flags;
            public IntPtr ReservedAddress;
            public nuint ReservedSize;
            public int RelroFd, LibraryFd;
            public long LibraryFdOffset;
            public IntPtr LibraryNamespace;
        }

        [DllImport("libc.so", SetLastError = true)] private static extern IntPtr mmap(IntPtr address, nuint length, int prot, int flags, int fd, long offset);
        [DllImport("libc.so", SetLastError = true)] private static extern int munmap(IntPtr address, nuint length);
        [DllImport("libdl.so")] private static extern IntPtr android_dlopen_ext(string filename, int flags, DlExtInfo* info);

        /// <summary>The library's handle (as dlopen's), or null when it could not be loaded so (then loaded as usual).</summary>
        public static IntPtr? Load(string file)
        {
            IntPtr region = mmap(IntPtr.Zero, (nuint)Region, PROT_NONE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_NORESERVE, -1, 0);
            if (region == new IntPtr(-1))
                return null;
            var info = new DlExtInfo { Flags = ANDROID_DLEXT_RESERVED_ADDRESS, ReservedAddress = region, ReservedSize = (nuint)Region, RelroFd = -1, LibraryFd = -1 };
            IntPtr handle = android_dlopen_ext(file, RTLD_NOW, &info);
            if (handle == IntPtr.Zero)
            {
                munmap(region, (nuint)Region);
                return null;
            }
            // The image's end: its last mapping in the region that is not the reservation itself (its segments, then its
            // .bss without the file's name), then the rest of the region back
            long start = region.ToInt64(), end = start + Region, imageEnd = start;
            foreach (string line in File.ReadLines("/proc/self/maps"))
            {
                string[] parts = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[1].StartsWith("---", StringComparison.Ordinal))
                    continue;
                string[] range = parts[0].Split('-');
                long from = long.Parse(range[0], NumberStyles.HexNumber), to = long.Parse(range[1], NumberStyles.HexNumber);
                if (from >= start && to <= end)
                    imageEnd = Math.Max(imageEnd, to);
            }
            imageEnd = (imageEnd + 0xFFFF) & ~0xFFFFL;
            if (imageEnd > start && imageEnd < end)
                munmap(new IntPtr(imageEnd), (nuint)(end - imageEnd));
            return handle;
        }
    }
}
