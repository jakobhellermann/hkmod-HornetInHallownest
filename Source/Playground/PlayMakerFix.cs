extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MonoMod.RuntimeDetour;

namespace HornetPlayer.Playground;

// HK and Silksong share ONE PlayMaker.dll, so PlayMaker's action-type resolution is global: ActionData.GetActionType
// maps an action name -> a single Type, cached in the shared ActionTypeLookup. Both games define ~the same action
// names in their own Assembly-CSharp (HK) / Silksong.AssemblyCSharp. A single name->Type map cannot serve both:
//   - leave a colliding name to HK  -> Hornet's FSM gets HK's action (wrong field layout) -> NullRef
//   - seed a colliding name to Silksong -> HK's FSM (bench/stag/scene-transition) gets Silksong's action -> NullRef
// (The old global-seed approach hit exactly this and broke HK benches/stag/darkness-on-transition.)
//
// Clean separation: resolve actions PER-FSM by ownership. LoadActions(FsmState) is the funnel that creates a state's
// actions and carries state.Fsm (-> owning GameObject). We hook it to flag "this FSM belongs to the spawned Hornet"
// (its GameObject is under BundleSpike.HornetRoot), and hook GetActionType to honor that flag: Hornet FSMs resolve
// from Silksong.AssemblyCSharp (our own map/cache, never touching the shared one); every other FSM resolves exactly
// as vanilla HK. Hashcodes then match on both sides (each FSM gets its own game's action type).
//
// NOTE: only ACTION types are separated here. Enum/FsmObject types referenced inside Silksong actions still go through
// ReflectionUtils.GetGlobalType (shared) — a follow-up if those collide.
internal static class PlayMakerFix {
    private static readonly List<Hook> hooks = new();

    // Set true (saved/restored) for the duration of a LoadActions call whose FSM belongs to Hornet.
    [ThreadStatic] private static bool resolvingHornetFsm;

    // Lazily-built name -> Silksong action Type, plus a resolve cache. Our own; the shared ActionTypeLookup stays
    // HK-only.
    private static Dictionary<string, Type>? silksongActions;
    private static readonly Dictionary<string, Type?> silksongResolveCache = new();

    internal static void Apply() {
        try {
            PurgeSilksongEntries();
            InstallHooks();
            Log.Info("[PlayMakerFix] per-FSM action resolution installed (Hornet -> Silksong, everything else -> HK)");
        } catch (Exception e) {
            Log.Error($"[PlayMakerFix] Apply failed: {e}");
        }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        resolvingHornetFsm = false;
    }

    // The shared caches live in PlayMaker.dll (not reloaded on hot-reload), so an earlier global-seed run can leave
    // Silksong action types cached against names HK FSMs use. Remove any Silksong-typed entries so HK re-resolves to
    // its own — without this, a hot-reload wouldn't undo the old pollution (and a restart would still hit it once the
    // mod re-ran the old seed).
    private static void PurgeSilksongEntries() {
        var silksongAsm = typeof(Silksong::HeroController).Assembly;
        PurgeFrom(typeof(ActionData), "ActionTypeLookup", silksongAsm);
        PurgeFrom(typeof(ReflectionUtils), "typeLookup", silksongAsm);
    }

    private static void PurgeFrom(Type owner, string field, Assembly silksongAsm) {
        var dict = (IDictionary?)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (dict == null) return;
        var stale = new List<object>();
        foreach (DictionaryEntry e in dict)
            if (e.Value is Type t && t.Assembly == silksongAsm) stale.Add(e.Key);
        foreach (var k in stale) dict.Remove(k);
        if (stale.Count > 0) Log.Info($"[PlayMakerFix] purged {stale.Count} stale Silksong entries from {owner.Name}.{field}");
    }

    private static void InstallHooks() {
        var loadActions = typeof(ActionData).GetMethod("LoadActions",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(FsmState) }, null);
        var getActionType = typeof(ActionData).GetMethod("GetActionType",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (loadActions == null || getActionType == null)
            throw new InvalidOperationException($"PlayMaker methods not found (LoadActions={loadActions != null}, GetActionType={getActionType != null})");

        hooks.Add(new Hook(loadActions,
            new Func<Func<ActionData, FsmState, FsmStateAction[]>, ActionData, FsmState, FsmStateAction[]>(LoadActionsHook)));
        hooks.Add(new Hook(getActionType,
            new Func<Func<string, Type>, string, Type>(GetActionTypeHook)));
    }

    // Flag whether the state's FSM is Hornet's, for the duration of action loading (save/restore handles nested loads).
    private static FsmStateAction[] LoadActionsHook(
        Func<ActionData, FsmState, FsmStateAction[]> orig, ActionData self, FsmState state) {
        var prev = resolvingHornetFsm;
        resolvingHornetFsm = IsHornetFsm(state?.Fsm);
        try { return orig(self, state); }
        finally { resolvingHornetFsm = prev; }
    }

    // For Hornet FSMs, resolve the action name from Silksong.AssemblyCSharp; otherwise vanilla (HK) resolution.
    private static Type GetActionTypeHook(Func<string, Type> orig, string actionName) {
        if (resolvingHornetFsm) {
            var t = ResolveSilksongAction(actionName);
            if (t != null) return t;
        }
        return orig(actionName);
    }

    private static bool IsHornetFsm(Fsm? fsm) {
        var root = BundleSpike.HornetRoot;
        if (fsm == null || root == null) return false;
        var go = fsm.GameObject;
        return go != null && go.transform.IsChildOf(root.transform);
    }

    private static Type? ResolveSilksongAction(string actionName) {
        if (silksongResolveCache.TryGetValue(actionName, out var cached)) return cached;
        silksongActions ??= BuildSilksongActionMap();
        silksongActions.TryGetValue(actionName, out var t);
        silksongResolveCache[actionName] = t;
        return t;
    }

    private static Dictionary<string, Type> BuildSilksongActionMap() {
        var map = new Dictionary<string, Type>();
        var asm = typeof(Silksong::HeroController).Assembly;
        Type?[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types; }
        var actionBase = typeof(FsmStateAction);
        foreach (var t in types) {
            if (t == null || t.IsAbstract || !actionBase.IsAssignableFrom(t)) continue;
            map[t.FullName!] = t;
        }
        Log.Info($"[PlayMakerFix] built Silksong action map: {map.Count} types");
        return map;
    }
}
