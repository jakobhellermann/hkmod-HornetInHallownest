extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker; // HK's shared PlayMaker (Fsm / PlayMakerFSM / ActionHelpers / FsmFloat / FsmInt)
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// One place for "HK looks up a named PlayMakerFSM on a CROSS-GAME object and finds none".
//
// HK code resolves an FSM on a GameObject through three static lookups, all (GameObject, string) -> PlayMakerFSM:
//   - ActionHelpers.GetGameObjectFsm  — Set/GetFsm* actions; the ONLY one that logs "Could not find FSM: <name>"
//   - PlayMakerFSM.FindFsmOnGameObject — e.g. BreakableObject/BreakablePoleSimple reading a nail hit
//   - FSMUtility.LocateFSM             — e.g. the ReceivedDamage action (bells, levers)
// All do go.GetComponents<HK PlayMakerFSM>() — HK's HutongGames type. Hornet's FSMs (and her slash's) are the isolated
// Silksong.PlayMaker type, so every lookup on one of HER objects misses. Two consequences we handle here:
//   1. hero-owned FSMs (Dream Return / ProxyFSM) the Knight has and Hornet doesn't -> "Could not find FSM" noise
//      (ProxyFSM x4/transition = old #8b; Dream Return on Godhome return). Nothing needs to run: an inert dummy
//      absorbs every Set (Set/GetFsm* null-check the variable, so a missing var is dropped/defaulted silently).
//   2. Hornet's slash has no HK "damages_enemy" FSM, so BreakableObject / ReceivedDamage never react (geo rocks,
//      stag bells). Here the dummy must CARRY data HK reads: "direction" (topple way) + "damageDealt".
//
// We deliberately don't port the real FSMs: hero-owned ones act on Self=the Knight body (Knight-only tk2d clips), so
// "run the real FSM" would drive the inert Knight, not Hornet. Behavior a real hero FSM performs on Hornet's side
// (e.g. Dream Return's blanker fade + control restore) lives in its own bridge (DreamReturnBridge). This shim is only
// the LOOKUP resolver — a dummy has no state machine to run.
//
// Design: a small handler list. On any of the three lookups, the first handler whose Matches(go, name) is true
// Provides a dummy; otherwise fall through to orig (so HK objects — incl. the Knight's real FSMs — and genuine misses
// are untouched, warning preserved). Handlers gate on Silksong objects that by construction never carry the HK FSM, so
// there's no ambiguity with a real one. (Supersedes the old HeroFsmShim + DamagesEnemyFsmShim, now folded in here.)
internal static class FsmLookupShim {
    private sealed class Handler {
        public readonly Func<GameObject, string, bool> Matches;
        public readonly Func<GameObject, string, PlayMakerFSM> Provide;

        public Handler(Func<GameObject, string, bool> matches, Func<GameObject, string, PlayMakerFSM> provide) {
            Matches = matches;
            Provide = provide;
        }
    }

    private static readonly List<Handler> Handlers = new();
    private static readonly List<Hook> Hooks = new();
    private static GameObject? holder;

    // Handler 1 (hero-owned, inert): FSM names HK looks up on the hero that Hornet lacks -> empty dummy, cached per name.
    private static readonly HashSet<string> HeroInertNames = new() { "Dream Return", "ProxyFSM" };
    private static readonly Dictionary<string, PlayMakerFSM> HeroDummies = new();

    // Handler 2 (slash "damages_enemy", dynamic): one dummy carrying "direction"/"damageDealt", re-populated per lookup.
    private static PlayMakerFSM? damagesDummy;
    private static FsmFloat? dirVar;
    private static FsmInt? dmgVar;

    internal static void Install() {
        Handlers.Clear();
        Handlers.Add(new Handler(HeroInertMatches, HeroInertProvide));
        Handlers.Add(new Handler(DamagesMatches, DamagesProvide));

        HookLookup(typeof(ActionHelpers), "GetGameObjectFsm");
        HookLookup(typeof(PlayMakerFSM), "FindFsmOnGameObject");
        HookLookup(typeof(FSMUtility), "LocateFSM");
        Log.Debug($"[FsmLookupShim] installed on {Hooks.Count} lookup(s)");
    }

    private static void HookLookup(Type type, string method) {
        var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static,
            null, [typeof(GameObject), typeof(string)], null);
        if (mi == null) {
            Log.Error($"[FsmLookupShim] {type.Name}.{method}(GameObject,string) not found");
            return;
        }

        Hooks.Add(new Hook(mi, (Hooked)OnLookup));
    }

    private static PlayMakerFSM OnLookup(Orig orig, GameObject go, string fsmName) {
        if (go != null && !string.IsNullOrEmpty(fsmName))
            foreach (var h in Handlers)
                if (h.Matches(go, fsmName))
                    return h.Provide(go, fsmName);
        // HK objects (incl. the Knight's real FSMs) + genuine misses: unchanged; orig keeps its own warning.
        // go! — the origs don't null-check go before GetComponents; forward exactly as HK would.
        return orig(go!, fsmName);
    }

    // ---- Handler 1: hero-owned inert -------------------------------------------------------------------------------
    // The remapped "Hero" resolves to Hornet's hero root (Silksong HeroController). HK's Knight has the real HK FSMs,
    // so gating on "is a Silksong hero" only ever diverts Hornet — never a legitimately-warning HK object.
    private static bool HeroInertMatches(GameObject go, string fsmName) =>
        HeroInertNames.Contains(fsmName) && go.GetComponent<Silksong::HeroController>() != null;

    private static PlayMakerFSM HeroInertProvide(GameObject go, string fsmName) {
        if (!HeroDummies.TryGetValue(fsmName, out var d) || d == null) {
            d = NewDummy(fsmName);
            HeroDummies[fsmName] = d;
        }

        return d;
    }

    // ---- Handler 2: slash "damages_enemy" (dynamic) ----------------------------------------------------------------
    private static bool DamagesMatches(GameObject go, string fsmName) =>
        fsmName == "damages_enemy" && go.GetComponentInParent<Silksong::DamageEnemies>() != null;

    private static PlayMakerFSM DamagesProvide(GameObject go, string fsmName) {
        if (damagesDummy == null) {
            damagesDummy = NewDummy("damages_enemy");
            dirVar = new FsmFloat("direction");
            dmgVar = new FsmInt("damageDealt");
            damagesDummy.Fsm.Variables.FloatVariables = [dirVar];
            damagesDummy.Fsm.Variables.IntVariables = [dmgVar];
        }

        dirVar!.Value = SlashDirection(go);
        dmgVar!.Value = NailDamage();
        return damagesDummy;
    }

    // TC angle convention the readers use: 0=right, 90=up, 180=left, 270=down. Vertical slashes are named Up/DownSlash;
    // horizontal slashes (Slash/AltSlash/WallSlash) topple toward Hornet's facing.
    private static float SlashDirection(GameObject go) {
        var de = go.GetComponentInParent<Silksong::DamageEnemies>();
        var name = de != null ? de.gameObject.name : go.name;
        if (name.IndexOf("Up", StringComparison.OrdinalIgnoreCase) >= 0) return 90f;
        if (name.IndexOf("Down", StringComparison.OrdinalIgnoreCase) >= 0) return 270f;
        var hc = Silksong::HeroController.UnsafeInstance;
        var right = hc != null && hc.cState != null && hc.cState.facingRight;
        return right ? 0f : 180f;
    }

    private static int NailDamage() {
        try {
            var pd = Silksong::PlayerData.instance;
            if (pd != null && pd.nailDamage > 0) return pd.nailDamage;
        } catch {
        }

        return 5;
    }

    // ---- Dummy factory ---------------------------------------------------------------------------------------------
    // Build on an INACTIVE, DontDestroyOnLoad holder so PlayMakerFSM.Awake never runs (no empty-FSM PlayMaker log
    // noise) and it never gets a per-frame Update. `new Fsm` field-initialises Variables (non-null); no states -> it
    // never runs even if activated. Assign to the private `fsm` field directly (no public setter).
    private static PlayMakerFSM NewDummy(string fsmName) {
        if (holder == null) {
            holder = new GameObject("hp_fsm_lookup_shim");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
        }

        var dummy = holder.AddComponent<PlayMakerFSM>();
        typeof(PlayMakerFSM).GetField("fsm", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(dummy, new Fsm { Name = fsmName });
        return dummy;
    }

    internal static void Cleanup() {
        foreach (var h in Hooks) h.Dispose();
        Hooks.Clear();
        Handlers.Clear();
        HeroDummies.Clear();
        damagesDummy = null;
        dirVar = null;
        dmgVar = null;
        if (holder != null) {
            Object.Destroy(holder);
            holder = null;
        }
    }

    private delegate PlayMakerFSM Orig(GameObject go, string fsmName);

    private delegate PlayMakerFSM Hooked(Orig orig, GameObject go, string fsmName);
}
