using System.Runtime.Intrinsics.X86;

namespace GameVault.Core.Retro
{
    public enum EmulatorFailure { None, OlderProcessor, MissingRuntime, Other }

    /// <summary>How an emulator ended: one that dies as it starts shows no window of its own, the client tells why.</summary>
    public static class EmulatorExit
    {
        /// <summary>Longer than this it was played: a crash in a game is not a failure to start.</summary>
        public static readonly TimeSpan AtItsStart = TimeSpan.FromSeconds(20);

        private const int IllegalInstruction = unchecked((int)0xC000001D), DllNotFound = unchecked((int)0xC0000135), EntryPointNotFound = unchecked((int)0xC0000139);
        /// <summary>128 + SIGILL.</summary>
        private const int LinuxIllegalInstruction = 132;

        public static EmulatorFailure Of(int exitCode, TimeSpan ran) =>
            exitCode == 0 || ran >= AtItsStart ? EmulatorFailure.None
            : exitCode is IllegalInstruction or LinuxIllegalInstruction ? EmulatorFailure.OlderProcessor
            : exitCode is DllNotFound or EntryPointNotFound ? EmulatorFailure.MissingRuntime
            : EmulatorFailure.Other;

        /// <summary>Windows' codes as they are documented (0xC000001D).</summary>
        public static string CodeText(int exitCode) => exitCode < 0 ? $"0x{exitCode:X8}" : exitCode.ToString();

        /// <summary>What Eden's main builds need (x86-64-v3): AVX2 with BMI, FMA and LZCNT, from Intel's 4th generation and the Ryzen.</summary>
        public static bool ProcessorIsX86V3 => Avx2.IsSupported && Bmi1.IsSupported && Bmi2.IsSupported && Fma.IsSupported && Lzcnt.IsSupported;
    }
}
