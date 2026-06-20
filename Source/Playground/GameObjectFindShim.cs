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

    // Set true ONLY while Silksong code we control the entry of is executing (e.g. around the hero's SetActive(true),
    // which synchronously runs HeroController.Awake -> UpdateConfig -> FSM events -> FindGameObject). While true, name/tag
    // lookups are INTERCEPTED: a known key (e.g. "CameraTarget") resolves to the Silksong object; anything else returns
    // null + a log (fail loud — never silently hand Silksong code HK's same-named object). While false (HK's own per-frame
    // code), lookups pass through to Unity unchanged. Keep the window TIGHT (one Silksong entry point at a time) so HK's
    // finds are never intercepted. Extend to specific Update methods later as their lookups surface.
    internal static bool CalledFromSilksongContext;

    // Known Silksong TAGS -> the Silksong object they should resolve to (tag namespace, used by FindWithTag).
    private static GameObject? ResolveTag(string tag) => tag switch {
        "CameraTarget" => GameCamerasBootstrap.CameraTargetGo, // Sprint FSM's FindGameObject(tag) -> needs Silksong's CameraTarget (has SetSprint)
        _ => null,
    };

    // Known Silksong NAMES -> the Silksong object (name namespace, used by GameObject.Find). Separate from tags on purpose.
    private static GameObject? ResolveName(string name) => name switch {
        _ => null,
    };

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

    // One-off diagnostic: set to a name/tag (e.g. "CameraTarget") to dump the managed call stack ONCE when that
    // lookup fires — reveals WHO calls it (the FSM action / component). Set via /find-trace?key=CameraTarget.
    internal static string? TraceKey;

    private static GameObject FindDetour(Func<string, GameObject> orig, string name) {
        MaybeTrace(name);
        if (CalledFromSilksongContext) return Intercept("Find", name, ResolveName(name))!;
        var r = orig(name);
        LogOnce("Find", name, r);
        return r;
    }

    private static GameObject TagDetour(Func<string, GameObject> orig, string tag) {
        MaybeTrace(tag);
        if (CalledFromSilksongContext) return Intercept("FindWithTag", tag, ResolveTag(tag))!;
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

    // In Silksong context: resolve known keys to the Silksong object; block the rest to null (+log once) so Silksong
    // code never silently picks up HK's same-named object. Unknown blocks are the to-do list for the Resolve() map.
    private static GameObject? Intercept(string method, string key, GameObject? redirect) {
        if (logged.Add("ctx:" + method + "|" + key))
            Log.Info(redirect != null
                ? $"[Find] {method}('{key}') -> REDIRECT '{redirect.name}' (silksong-context)"
                : $"[Find] {method}('{key}') -> null (silksong-context, no Resolve mapping — BLOCKED; add to map if needed)");
        return redirect;
    }

    private static void MaybeTrace(string key) {
        try {
            if (TraceKey == null || key != TraceKey || !logged.Add("trace:" + key)) return;
            Log.Info($"[Find] CALLER of '{key}':\n{Environment.StackTrace}");
        } catch { }
    }

    private static void LogOnce(string method, string key, GameObject? result) {
        try {
            var k = method + "|" + key;
            if (!logged.Add(k)) return;
            // Only reached in the passthrough path (HK context); Silksong-context lookups go through Intercept.
            Log.Info($"[Find] {method}('{key}') -> {(result != null ? "'" + result.name + "'" : "null")}  [passthrough]");
        } catch { /* never break a find */ }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        logged.Clear();
        CalledFromSilksongContext = false;
    }
}
