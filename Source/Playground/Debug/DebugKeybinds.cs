extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HornetPlayer.HornetInHallownest.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// B  - toggle infinite silk
// 8  - toggle collider height
// F9 - toggle per-frame state trace to a TSV
internal static class DebugKeybinds {
    private static GameObject? go;

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.DebugKeybinds");
        go.AddComponent<DebugKeybindsBehaviour>();
        Object.DontDestroyOnLoad(go);
    }

    internal static void Cleanup() {
        if (go != null) Object.Destroy(go);
        go = null;
    }
}

internal sealed class DebugKeybindsBehaviour : MonoBehaviour {
    // F9 trace: Hornet's per-frame state (finer than the HTTP poll), written as a TSV on stop.
    private const string TracePath = "/tmp/hornet_trace_live.tsv";

    private bool infiniteSilk;
    private List<string>? traceBuf;
    private float traceT0;
    private bool tracing;

    private void Update() {
        if (Input.GetKeyDown(KeyCode.B)) {
            infiniteSilk = !infiniteSilk;
            Log.Debug($"[DebugKeybinds] infinite silk: {infiniteSilk}");
        }

        if (Input.GetKeyDown(KeyCode.Alpha8)) {
            var knightHeight = HornetSpawner.ToggleColliderHeight();
            Log.Debug($"[DebugKeybinds] collider height: {(knightHeight ? "Knight (1.28)" : "Hornet full (2.08)")}");
        }

        if (infiniteSilk) {
            var spd = Silksong::PlayerData.instance;
            if (spd.silk < spd.silkMax) spd.silk = spd.silkMax;
        }

        TraceTick();
    }

    private void TraceTick() {
        if (Input.GetKeyDown(KeyCode.F9)) {
            if (!tracing) {
                tracing = true;
                traceT0 = Time.realtimeSinceStartup;
                traceBuf = new List<string> { "t\tscene\ttransState\theroState\tonGround\tcReq\tvx\tvy\tx\ty" };
                Log.Debug("[Trace] recording started (F9 to stop)");
            }
            else {
                tracing = false;
                try {
                    File.WriteAllLines(TracePath, traceBuf!);
                    Log.Debug($"[Trace] wrote {traceBuf!.Count - 1} frames -> {TracePath}");
                } catch (Exception e) {
                    Log.Error($"[Trace] write failed: {e.Message}");
                }

                traceBuf = null;
            }
        }

        if (!tracing) return;
        var hc = BundleSpike.Hornet;
        if (!hc) return;
        var p = hc.transform.position;
        var rb = hc.GetComponent<Rigidbody2D>();
        var v = rb ? rb.linearVelocity : Vector2.zero;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        traceBuf!.Add(string.Format(CultureInfo.InvariantCulture,
            "{0:F2}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6:F1}\t{7:F1}\t{8:F1}\t{9:F1}",
            Time.realtimeSinceStartup - traceT0, scene, hc.transitionState, hc.hero_state,
            hc.cState.onGround, hc.controlReqlinquished, v.x, v.y, p.x, p.y));
    }
}
