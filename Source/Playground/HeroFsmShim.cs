extern alias Silksong;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker; // HK's shared PlayMaker (Fsm / PlayMakerFSM / ActionHelpers)
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// General fallback for HK FSM actions that look up a named PlayMakerFSM ON THE HERO and expect it to exist.
//
// HK FSMs do SetFsmBool/GetFsmBool(gameObject=var "Hero", fsmName="Dream Return" | "ProxyFSM", ...). With the global
// "Hero" var pointed at Hornet (HeroProxy), the lookup runs against HER GameObject — but her FSMs are the isolated
// Silksong.PlayMaker type, so HK's ActionHelpers.GetGameObjectFsm (which does go.GetComponents<HK PlayMakerFSM>()) finds
// none and logs "Could not find FSM: <name>" (Dream Return: 1 per Godhome return; ProxyFSM: x4 per transition — the
// old open item #8b). These are genuine HERO-OWNED FSMs the Knight has and Hornet simply doesn't.
//
// We deliberately DON'T port those FSMs: they act on Self=the Knight body (SetPosition/RegainControl/Tk2dPlayAnimation
// on Knight-only clips), so "letting the real FSM run" would drive the inert Knight, not Hornet — and the global "Hero"
// remap doesn't help because 42/43 of their actions target Self (the FSM's owner), which isn't remappable. The behavior
// a real hero FSM would perform on Hornet's side (e.g. Dream Return's blanker fade + control restore on arrival) lives
// in its own bridge (DreamReturnBridge), triggered by the scene transition. A dummy has no state machine to run.
//
// So this shim only needs HK's *lookup* to resolve, so the write/read lands somewhere harmless and the warning stops.
// Both SetFsmBool and GetFsmBool null-check the FsmVariables result (verified in decomp) — a missing variable is
// tolerated silently (the set is dropped, the read keeps its default) — so the dummy needs NO variables: an empty FSM
// carrying just the name is enough to suppress the warning for the whole 70-set / 25-get census across these names.
//
// Same pattern as DamagesEnemyFsmShim (return a dummy HK PlayMakerFSM from a lookup hook, built on an inactive GO so
// PlayMakerFSM.Awake never runs). That shim hooks FindFsmOnGameObject/LocateFSM for a READ; this hooks GetGameObjectFsm
// for the SetFsm*/GetFsm* actions. The two are candidates to merge onto this general mechanism later.
internal static class HeroFsmShim {
    // FSM names HK looks up on the hero that Hornet lacks. Adding a name here hands back an inert dummy instead of
    // logging "Could not find FSM". These are hero-owned FSMs, only ever looked up on the hero.
    private static readonly HashSet<string> MockedNames = new() { "Dream Return", "ProxyFSM" };

    private static Hook? getFsmHook;
    private static GameObject? holder;
    private static readonly Dictionary<string, PlayMakerFSM> dummies = new();

    internal static void Install() {
        var mi = typeof(ActionHelpers).GetMethod("GetGameObjectFsm", BindingFlags.Public | BindingFlags.Static,
            null, [typeof(GameObject), typeof(string)], null);
        if (mi == null) {
            Log.Error("[HeroFsmShim] ActionHelpers.GetGameObjectFsm not found");
            return;
        }

        getFsmHook = new Hook(mi, (Hooked)OnGetGameObjectFsm);
        Log.Debug("[HeroFsmShim] installed: ActionHelpers.GetGameObjectFsm hero-FSM dummy fallback");
    }

    private static PlayMakerFSM OnGetGameObjectFsm(Orig orig, GameObject go, string fsmName) {
        if (go != null && !string.IsNullOrEmpty(fsmName) && MockedNames.Contains(fsmName) && IsHornetHero(go)) {
            // Be exact: if a real HK FSM of that name is somehow present, prefer it (never true on Hornet, but cheap).
            foreach (var f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == fsmName)
                    return f;
            return GetOrCreateDummy(fsmName); // suppress "Could not find FSM"; absorb the set / default the read
        }

        // HK objects (incl. the Knight's real FSMs, found by orig's own loop) + genuine misses: unchanged, warning kept.
        // go! — orig (HK's GetGameObjectFsm) itself doesn't null-check go before go.GetComponents; forward as-is.
        return orig(go!, fsmName);
    }

    // The "Hero" global resolves to Hornet's hero root, which carries the Silksong HeroController. HK's Knight has the
    // real HK FSMs, so gating on "is a Silksong hero" only ever diverts Hornet — never a legitimately-warning HK object.
    private static bool IsHornetHero(GameObject go) => go.GetComponent<Silksong::HeroController>() != null;

    private static PlayMakerFSM GetOrCreateDummy(string fsmName) {
        if (dummies.TryGetValue(fsmName, out var existing) && existing != null) return existing;
        if (holder == null) {
            holder = new GameObject("hp_hero_fsm_shim");
            holder.SetActive(false); // inactive -> PlayMakerFSM.Awake never runs (no empty-FSM PlayMaker log noise)
            Object.DontDestroyOnLoad(holder);
        }

        var dummy = holder.AddComponent<PlayMakerFSM>();
        // new Fsm field-initialises Variables (non-null); no states -> nothing ever runs even if it activated. Assign to
        // the private field directly (there's no public setter), exactly as DamagesEnemyFsmShim does.
        var fsm = new Fsm { Name = fsmName };
        typeof(PlayMakerFSM).GetField("fsm", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dummy, fsm);
        dummies[fsmName] = dummy;
        Log.Debug($"[HeroFsmShim] created dummy hero FSM '{fsmName}'");
        return dummy;
    }

    internal static void Cleanup() {
        getFsmHook?.Dispose();
        getFsmHook = null;
        dummies.Clear();
        if (holder != null) {
            Object.Destroy(holder);
            holder = null;
        }
    }

    private delegate PlayMakerFSM Orig(GameObject go, string fsmName);

    private delegate PlayMakerFSM Hooked(Orig orig, GameObject go, string fsmName);
}
