using System;
using System.Collections.Generic;
using System.Reflection;
using HornetPlayer.HornetInHallownest;
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

    // While SilksongContext.Active (Silksong code we control the entry of — see SilksongContext), name/tag lookups are
    // INTERCEPTED: a known key (e.g. "CameraTarget") resolves to the Silksong object; anything else returns null + a log
    // (fail loud — never silently hand Silksong code HK's same-named object). Otherwise (HK's own code) lookups pass
    // through to Unity unchanged.

    // Known Silksong TAGS -> the Silksong object they should resolve to (tag namespace, used by FindWithTag).
    private static GameObject? ResolveTag(string tag) {
        return tag switch {
            "CameraTarget" => GameCamerasBootstrap
                .CameraTargetGo, // Sprint FSM's FindGameObject(tag) -> needs Silksong's CameraTarget (has SetSprint)
            _ => null
        };
    }

    // Known Silksong NAMES -> the Silksong object (name namespace, used by GameObject.Find). Separate from tags on purpose.
    private static GameObject? ResolveName(string name) {
        return name switch {
            _ => null
        };
    }

    internal static void Install() {
        if (hooks.Count > 0) return;
        AddHook("Find", typeof(GameObject).GetMethod(nameof(GameObject.Find), [typeof(string)]),
            (Func<Func<string, GameObject>, string, GameObject>)FindDetour);
        AddHook("FindGameObjectWithTag",
            typeof(GameObject).GetMethod(nameof(GameObject.FindGameObjectWithTag), [typeof(string)]),
            (Func<Func<string, GameObject>, string, GameObject>)TagDetour);
        Log.Info($"[Find] shim installed on {hooks.Count} lookup methods");
    }

    private static void AddHook(string label, MethodInfo? mi, Delegate detour) {
        if (mi == null) {
            Log.Error($"[Find] method not found: {label}");
            return;
        }

        try {
            hooks.Add(new Hook(mi, detour));
        } catch (Exception e) {
            Log.Error($"[Find] hook failed {label}: {e.Message}");
        }
    }

    private static GameObject FindDetour(Func<string, GameObject> orig, string name) {
        if (SilksongContext.Active) return Intercept("Find", name, ResolveName(name))!;
        var r = orig(name);
        LogOnce("Find", name, r);
        return r;
    }

    private static GameObject TagDetour(Func<string, GameObject> orig, string tag) {
        // While Hornet is the active hero, "Player" must resolve to HER. Both the Knight and Hornet_Real carry the
        // "Player" tag, so Unity's FindGameObjectWithTag returns whichever it finds first (nondeterministic). HK enemy
        // aggro/chase FSMs do FindWithTag("Player") ONCE, then cache + track that transform — if they grabbed the inert
        // Knight they fly to it (this is the chase consumer the HeroControllerProbe can't see: it reads the tag, not a
        // HeroController method). Redirect the tag to the active hero. HK systems that specifically need the Knight use
        // HeroController.instance / UnsafeInstance directly (not the tag), so this only steers the tag-based "who is the
        // player" consumers. Only "Player", only while HornetActive (when the Knight is active we leave native behavior).
        if (tag == "Player" && HeroSwitch.HornetActive && HeroSwitch.ActiveHeroGameObject is { } hero) {
            Log.Info("[Find] FindWithTag('Player') -> REDIRECT to Hornet");
            return hero;
        }

        if (SilksongContext.Active) return Intercept("FindWithTag", tag, ResolveTag(tag))!;

        GameObject r;
        try {
            r = orig(tag);
        } catch (Exception e) {
            // Tag not defined in HK's tag manager -> UnityException. "Not found" == null; don't crash the frame.
            Log.Error($"[Find] FindWithTag('{tag}') threw {e.GetType().Name} (tag not defined in HK) -> null");
            return null!;
        }

        LogOnce("FindWithTag", tag, r);
        return r;
    }

    // In Silksong context: resolve known keys to the Silksong object; block the rest to null (+log once) so Silksong
    // code never silently picks up HK's same-named object. Unknown blocks are the to-do list for the Resolve() map.
    private static GameObject? Intercept(string method, string key, GameObject? redirect) {
        Log.Info(redirect != null
            ? $"[Find] {method}('{key}') -> REDIRECT '{redirect.name}' (silksong-context)"
            : $"[Find] {method}('{key}') -> null (silksong-context, no Resolve mapping — BLOCKED; add to map if needed)");
        return redirect;
    }

    private static void LogOnce(string method, string key, GameObject? result) {
        try {
            var k = method + "|" + key;
            if (!logged.Add(k)) return;
            // Only reached in the passthrough path (HK context); Silksong-context lookups go through Intercept.
            Log.Debug(
                $"[Find] {method}('{key}') -> {(result != null ? "'" + result.name + "'" : "null")}  [passthrough]");
        } catch {
            /* never break a find */
        }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        logged.Clear();
    }
}
