using System;

namespace HornetInHallownest;

// A time-scoped flag: Currently executing code inside the silksong assembly.
// Used in shims that can't differentiate between hk/hkss context without a full stacktrace.
// Use via `using (SilksongContext.Enter()) { <silksong call> }`. Depth-counted so nested/re-entrant windows are safe.
internal static class SilksongContext {
    private static int depth;

    internal static bool Active => depth > 0;

    internal static Scope Enter() {
        depth++;
        return default;
    }

    internal readonly struct Scope : IDisposable {
        public void Dispose() {
            depth--;
        }
    }
}
