using System;
using UnityEngine;

namespace HornetPlayer.Playground;

// Tiny logging shim so the framework-agnostic server code has no hard dependency on the mod loader. The mod points
// `Sink` at its own logger in Initialize; until then (and in unit-style contexts) it falls back to Debug.Log.
internal static class Log {
    internal static Action<string> Sink = msg => Debug.Log(msg);

    internal static void Info(object? msg) => Sink($"[HornetPlayer] {msg}");
    internal static void Error(object? msg) => Sink($"[HornetPlayer] [ERROR] {msg}");
}
