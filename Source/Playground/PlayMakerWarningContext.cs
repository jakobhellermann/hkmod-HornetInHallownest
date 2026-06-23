extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// Add context to PlayMaker warnings that fire with zero context:
//
// 1. "Could not find FSM: ProxyFSM" — from ActionHelpers.GetGameObjectFsm (HK's PlayMaker). HK FSMs (e.g. SetFsmBool
//    on the "Hero" global → Hornet's GO) look for an FSM by name, but GetComponents<HK's PlayMakerFSM> doesn't find
//    Silksong's PlayMakerFSM (different type, same name — the cross-game GetComponents<T> collision, open item #8b).
//    The warning has zero context (no GO, no scene, no calling FSM). Hook adds GO name + scene + calling FSM context.
//
// 2. "Fsm not initialized: X" / "Error Loading Action: X : ..." — from Silksong's PlayMaker. Effect prefabs spawned
//    from GlobalPool try to init their FSMs but the init chain hits the inactive Silksong_GameManager → Fsm stays
//    null → every property access logs a warning → thousands of lines of noise. Deduplicate via
//    Application.logMessageReceived to a single log-per-unique-message, with a root-cause note.
internal static class PlayMakerWarningContext {
    private static Hook? getGameObjectFsmHook;
    private static Hook? fsmUpdateHook;
    private static readonly HashSet<string> logged = new();

    // Fallback for init paths that run actions outside FsmExecutionStack (rare).
    [ThreadStatic] private static string? currentFsmContext;

    internal static void Install() {
        // Hook HK's ActionHelpers.GetGameObjectFsm — this is where "Could not find FSM: X" comes from
        var mi = typeof(ActionHelpers).GetMethod("GetGameObjectFsm",
            BindingFlags.Public | BindingFlags.Static);
        if (mi != null) {
            getGameObjectFsmHook = new Hook(mi, GetGameObjectFsmHook);
            Log.Info("[PlayMakerCtx] installed: ActionHelpers.GetGameObjectFsm");
        } else {
            Log.Error("[PlayMakerCtx] ActionHelpers.GetGameObjectFsm not found");
        }

        // Use PlayMaker's own FsmExecutionStack as the primary source of truth for the calling FSM.
        // It's pushed by Fsm.Update, LateUpdate, FixedUpdate, AND ProcessEvent (cross-FSM events, init) —
        // so it captures all action execution paths. We hook Fsm.Update as a fallback for rare init paths
        // that run actions before the stack is pushed.
        var fsmUpdate = typeof(Fsm).GetMethod("Update", BindingFlags.Public | BindingFlags.Instance);
        if (fsmUpdate != null) {
            fsmUpdateHook = new Hook(fsmUpdate, FsmUpdateHook);
            Log.Info("[PlayMakerCtx] installed: Fsm.Update context fallback");
        } else {
            Log.Error("[PlayMakerCtx] Fsm.Update not found");
        }

        Application.logMessageReceived += OnLogMessage;
    }

    // Stash the current FSM context before actions run, clear after.
    private static void FsmUpdateHook(Action<Fsm> orig, Fsm self) {
        var prev = currentFsmContext;
        currentFsmContext = $"{self.OwnerName}/{self.Name}";
        try {
            orig(self);
        } finally {
            currentFsmContext = prev;
        }
    }

    private static PlayMakerFSM? GetGameObjectFsmHook(
        Func<GameObject, string, PlayMakerFSM?> orig, GameObject go, string fsmName) {
        var result = orig(go, fsmName);
        if (result == null && go != null && !string.IsNullOrEmpty(fsmName)) {
            var scene = go.scene.name;
            // Primary: PlayMaker's own execution stack (covers Update/LateUpdate/FixedUpdate/ProcessEvent).
            // Fallback: our ThreadStatic (covers rare init paths outside the stack).
            var execFsm = FsmExecutionStack.ExecutingFsm;
            var caller = execFsm != null
                ? $"{execFsm.OwnerName}/{execFsm.Name}"
                : currentFsmContext ?? "(unknown FSM)";
            var key = $"notfound|{fsmName}|{go.name}|{scene}|{caller}";
            if (logged.Add(key)) {
                Log.Info($"[PlayMakerCtx] FSM '{fsmName}' not found on GO '{go.name}' (scene={scene}) — called by FSM: {caller}");
            }
        }
        return result;
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type) {
        // Deduplicate the Silksong PlayMaker "Fsm not initialized" / "Error Loading Action" burst.
        // These fire from Fsm property getters and ActionData.CreateAction when the Fsm object is null
        // (init failed because Silksong_GameManager is inactive). Each unique message logged once.
        if (condition.StartsWith("Fsm not initialized:") ||
            condition.StartsWith("Error Loading Action:") ||
            condition.StartsWith("get_actions: Fsm not initialized:") ||
            condition.StartsWith("get_fsm: Fsm not initialized:")) {
            if (logged.Add(condition)) {
                Log.Info($"[PlayMakerCtx] {condition} (root cause: inactive Silksong_GameManager — FSM init chain aborted)");
            }
        }
    }

    internal static void Cleanup() {
        getGameObjectFsmHook?.Dispose();
        getGameObjectFsmHook = null;
        fsmUpdateHook?.Dispose();
        fsmUpdateHook = null;
        Application.logMessageReceived -= OnLogMessage;
        logged.Clear();
    }

    // One-shot diagnostic: log the call stack when Hornet's HeroController.OnDestroy fires,
    // so we can see WHO destroys her during scene transitions (open item #1 — silent destruction).
    private static Hook? onDestroyHook;

    internal static void InstallOnDestroyTrace() {
        var mi = typeof(Silksong::HeroController)
            .GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi == null) { Log.Error("[PlayMakerCtx] HeroController.OnDestroy not found"); return; }
        onDestroyHook = new Hook(mi, (Action<Action<Silksong::HeroController>, Silksong::HeroController>)((orig, self) => {
            orig(self);
            Log.Error($"[PlayMakerCtx] HeroController.OnDestroy on '{self.gameObject.name}' (scene={self.gameObject.scene.name})\n{System.Environment.StackTrace}");
        }));
        Log.Info("[PlayMakerCtx] installed: HeroController.OnDestroy trace");
    }

    internal static void CleanupOnDestroyTrace() {
        onDestroyHook?.Dispose();
        onDestroyHook = null;
    }
}
