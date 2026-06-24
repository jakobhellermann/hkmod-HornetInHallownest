using System;
using System.Collections.Generic;

namespace HornetPlayer.Playground;

// Tiny logging shim so the framework-agnostic server code has no hard dependency on the mod loader. The mod points
// `Sink` at its own logger in Initialize; until then (and in unit-style contexts) it falls back to Debug.Log.
internal static class Log {
    internal static Action<string> SinkDebug = UnityEngine.Debug.Log;
    internal static Action<string> SinkInfo = UnityEngine.Debug.Log;
    internal static Action<string> SinkError = UnityEngine.Debug.Log;

    internal static void Debug(object? msg) {
        SinkDebug($"{msg}");
    }

    internal static void Info(object? msg) {
        SinkInfo($"{msg}");
    }

    internal static void Error(object? msg) {
        SinkError($"{msg}");
    }

    // --- Log-once dedup with global toggle ---
    // When DedupOnce is true (default): each unique key logs only once. When false: logs every call.
    // Toggle via POST /log-once?dedup=false to see repeat occurrences during debugging.
    private static readonly HashSet<string> seenOnce = new();
    internal static bool DedupOnce = true;

    internal static void InfoOnce(string key, object? msg) {
        if (DedupOnce && !seenOnce.Add(key)) return;
        SinkInfo($"{msg}");
    }

    internal static void DebugOnce(string key, object? msg) {
        if (DedupOnce && !seenOnce.Add(key)) return;
        SinkDebug($"{msg}");
    }

    internal static void ErrorOnce(string key, object? msg) {
        if (DedupOnce && !seenOnce.Add(key)) return;
        SinkError($"{msg}");
    }

    internal static void ClearOnce() {
        seenOnce.Clear();
    }
}
