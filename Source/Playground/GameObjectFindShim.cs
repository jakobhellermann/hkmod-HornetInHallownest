using System;
using System.Collections.Generic;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// Observability shim for name/tag-based GameObject lookups — the cross-game hazard class: Silksong code/FSMs resolve
// objects by name/tag at runtime (GameObject.Find / FindWithTag) and in HK hit HK's same-named/tagged objects (e.g.
// FindWithTag("Player") -> HK's Knight; FindGameObject("Camera Target") -> HK's camera target with no SetSprint) or
// throw on tags HK doesn't define. A prefab/remap fix is impossible (no serialized ref — pure runtime resolution), so
// we need visibility first. Both GameObject.Find(string) and FindGameObjectWithTag(string) are `cil managed` (hookable),
// and the PlayMaker FindGameObject ACTION funnels through them — so this single shim captures BOTH code- and FSM-driven
// finds.
//
// LOG-ONLY by default (no behavior change): we can't tell per call whether the caller is Silksong or HK without a
// stacktrace (unreliable via the MonoMod trampoline), and a coarse "Silksong context" flag would also catch HK's own
// finds — so blanket-redirecting/null-ing returns would break HK. Real corrections are per-name/tag, done elsewhere.
// The one safe behavior change: a FindWithTag for a tag HK doesn't define throws UnityException — we swallow that to
// null (that's what "not found" means) so it can't crash the frame.
internal static class GameObjectFindShim {
    private static readonly List<Hook> hooks = new();
    private static readonly HashSet<string> logged = new();

    // Set true during Silksong-driven windows (e.g. around spawn) to TAG those lookups in the log — not to change returns.
    internal static bool Active;

    internal static void Install() {
        if (hooks.Count > 0) return;
        AddHook("Find", typeof(GameObject).GetMethod(nameof(GameObject.Find), new[] { typeof(string) }),
            (Func<Func<string, GameObject>, string, GameObject>)FindDetour);
        AddHook("FindGameObjectWithTag", typeof(GameObject).GetMethod(nameof(GameObject.FindGameObjectWithTag), new[] { typeof(string) }),
            (Func<Func<string, GameObject>, string, GameObject>)TagDetour);
        Log.Info($"[Find] shim installed on {hooks.Count} lookup methods");
    }

    private static void AddHook(string label, System.Reflection.MethodInfo? mi, Delegate detour) {
        if (mi == null) { Log.Error($"[Find] method not found: {label}"); return; }
        try { hooks.Add(new Hook(mi, detour)); }
        catch (Exception e) { Log.Error($"[Find] hook failed {label}: {e.Message}"); }
    }

    private static GameObject FindDetour(Func<string, GameObject> orig, string name) {
        var r = orig(name);
        LogOnce("Find", name, r);
        return r;
    }

    private static GameObject TagDetour(Func<string, GameObject> orig, string tag) {
        GameObject r;
        try { r = orig(tag); }
        catch (Exception e) {
            // Tag not defined in HK's tag manager -> UnityException. "Not found" == null; don't crash the frame.
            if (logged.Add("tagthrow:" + tag)) Log.Error($"[Find] FindWithTag('{tag}') threw {e.GetType().Name} (tag not defined in HK) -> null");
            return null!;
        }
        LogOnce("FindWithTag", tag, r);
        return r;
    }

    private static void LogOnce(string method, string key, GameObject? result) {
        try {
            var k = method + "|" + key;
            if (!logged.Add(k)) return;
            var ctx = Active ? "silksong-window" : "boot/hk";
            Log.Info($"[Find] {method}('{key}') -> {(result != null ? "'" + result.name + "'" : "null")}  [{ctx}]");
        } catch { /* never break a find */ }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        logged.Clear();
        Active = false;
    }
}
