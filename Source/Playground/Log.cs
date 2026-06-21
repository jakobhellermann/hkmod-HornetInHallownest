using System;

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
}
