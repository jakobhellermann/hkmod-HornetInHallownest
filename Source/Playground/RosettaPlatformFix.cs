using System;
using System.Runtime.InteropServices;
using MonoMod.Utils;

namespace HornetPlayer.Playground;

// On Apple Silicon we run the x86_64 slice under Rosetta (native arm64 can't get MonoMod's RWX mprotect — EACCES).
// But MonoMod's DeterminePlatform() shells out to `uname -m`, which reports "arm64" (the child process runs native
// even though we're translated), so it picks DetourNativeARMPlatform whose instruction-cache flush emits ARM ops that
// are illegal under Rosetta -> SIGILL. Forcing PlatformHelper.Current to the non-ARM x86 platform selects
// DetourNativeX86Platform, whose FlushICache is a no-op (x86 caches are coherent). Must run before the first Hook,
// which locks PlatformHelper.Current. Only fires when actually translated by Rosetta.
internal static class RosettaPlatformFix {
    internal static void Apply() {
        if (!IsTranslatedByRosetta()) return;
        try {
            PlatformHelper.Current = MonoMod.Utils.Platform.MacOS | MonoMod.Utils.Platform.Bits64;
            Log.Info(
                $"[RosettaFix] under Rosetta — forced MonoMod platform to {PlatformHelper.Current} (x86 detour backend)");
        } catch (Exception e) {
            Log.Error($"[RosettaFix] could not override PlatformHelper.Current (already locked): {e.Message}");
        }
    }

    // `sysctl.proc_translated` == 1 iff this process runs under Rosetta 2. Absent (ENOENT) on Intel/older macOS and on
    // non-macOS -> treated as not translated. RuntimeInformation.ProcessArchitecture is unreliable here under Mono.
    private static bool IsTranslatedByRosetta() {
        try {
            var size = (IntPtr)sizeof(int);
            return sysctlbyname("sysctl.proc_translated", out var translated, ref size, IntPtr.Zero, IntPtr.Zero) == 0
                   && translated == 1;
        } catch {
            return false;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int sysctlbyname(string name, out int oldp, ref IntPtr oldlenp, IntPtr newp, IntPtr newlen);
}
