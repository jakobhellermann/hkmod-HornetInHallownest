extern alias SilksongPM;
using System;
using System.Reflection;
using HutongGames.PlayMaker;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SExecStack = SilksongPM::HutongGames.PlayMaker.FsmExecutionStack;

namespace HornetPlayer.Playground;

// Add context to PlayMaker warnings that fire with zero context:
//
// 1. "Could not find FSM: X" — PlayMaker logs this from MANY sites with no context (no GO, no scene, no calling FSM):
//    ActionHelpers.GetGameObjectFsm AND every Set*/Get*Fsm action's own LogWarning AND other lookup paths. We catch
//    them UNIFORMLY via Application.logMessageReceived (fires for every Unity log regardless of source) and attach the
//    calling FSM (from PlayMaker's execution stack) plus the target GO + scene when the lookup came through the
//    GetGameObjectFsm path (stashed there). Mostly cross-game mismatches: HK FSMs do SetFsmBool on the "Hero" global ->
//    Hornet's GO, but Hornet's FSMs are Silksong.PlayMaker.PlayMakerFSM (different type, same name), so HK's
//    GetComponents<HK.PlayMakerFSM> never finds them (open item #8b). The execution-stack lookup tries HK's stack first,
//    then Silksong's (for not-found warnings raised inside Hornet's own PlayMaker runtime).
//
// 2. "Fsm not initialized: X" / "Error Loading Action: X : ..." — from Silksong's PlayMaker. Effect prefabs spawned
//    from GlobalPool try to init their FSMs but the init chain hits the inactive Silksong_GameManager -> Fsm stays
//    null -> every property access logs a warning -> thousands of lines of noise. Deduplicate to one log-per-unique-
//    message with a root-cause note.
internal static class PlayMakerWarningContext {
    private static Hook? getGameObjectFsmHook;
    private static Hook? fsmUpdateHook;

    // Target GO/fsm of the in-flight GetGameObjectFsm call, so the synchronous "Could not find FSM" warning emitted
    // INSIDE it can name the GO + scene. Null for warnings from other lookup paths -> caller-only context.
    [ThreadStatic] private static GameObject? pendingGo;
    [ThreadStatic] private static string? pendingFsm;

    // Fallback calling-FSM context for init paths that run actions outside the execution stack (rare).
    [ThreadStatic] private static string? currentFsmContext;

    internal static void Install() {
        var mi = typeof(ActionHelpers).GetMethod("GetGameObjectFsm", BindingFlags.Public | BindingFlags.Static);
        if (mi != null) {
            getGameObjectFsmHook = new Hook(mi, GetGameObjectFsmHook);
            Log.Info("[PlayMakerCtx] installed: ActionHelpers.GetGameObjectFsm");
        }
        else {
            Log.Error("[PlayMakerCtx] ActionHelpers.GetGameObjectFsm not found");
        }

        // PlayMaker's FsmExecutionStack is the primary source for the calling FSM (pushed by Update/LateUpdate/
        // FixedUpdate/ProcessEvent). Hook Fsm.Update as a fallback for rare init paths that run actions before the
        // stack is pushed.
        var fsmUpdate = typeof(Fsm).GetMethod("Update", BindingFlags.Public | BindingFlags.Instance);
        if (fsmUpdate != null) {
            fsmUpdateHook = new Hook(fsmUpdate, FsmUpdateHook);
            Log.Info("[PlayMakerCtx] installed: Fsm.Update context fallback");
        }
        else {
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

    // Stash the lookup target so the "Could not find FSM" warning raised inside orig (ActionHelpers' own
    // Debug.LogWarning) can be enriched with the GO + scene.
    private static PlayMakerFSM? GetGameObjectFsmHook(
        Func<GameObject, string, PlayMakerFSM?> orig, GameObject go, string fsmName) {
        var prevGo = pendingGo;
        var prevFsm = pendingFsm;
        pendingGo = go;
        pendingFsm = fsmName;
        try {
            return orig(go, fsmName);
        } finally {
            pendingGo = prevGo;
            pendingFsm = prevFsm;
        }
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type) {
        // ALL "Could not find FSM: X" paths -> one contextual line per unique (fsm, caller).
        if (condition.StartsWith("Could not find FSM:")) {
            var fsmName = condition.Substring("Could not find FSM:".Length).Trim();
            var caller = ResolveCaller();
            var goCtx = pendingFsm == fsmName && pendingGo != null
                ? $" on GO '{pendingGo.name}' (scene={pendingGo.scene.name})"
                : "";
            Log.InfoOnce($"notfound|{fsmName}|{caller}",
                $"[PlayMakerCtx] FSM '{fsmName}' not found{goCtx} — called by FSM: {caller}");
            return;
        }

        // HK HUD FSMs (Soul Orb Control, etc.) call GetHero() -> HeroProxy redirects to Hornet -> they try HK-specific
        // methods that don't exist on Silksong's HeroController. No-ops (no crash), suppressed to keep the log clean.
        if (HeroSwitch.HornetActive && condition.StartsWith("Method Name is invalid: ClearMP"))
            return;

        // Deduplicate the Silksong PlayMaker "Fsm not initialized" / "Error Loading Action" burst (root: inactive
        // Silksong_GameManager -> Fsm object null -> every property getter logs). One line per unique message.
        if (condition.StartsWith("Fsm not initialized:") ||
            condition.StartsWith("Error Loading Action:") ||
            condition.StartsWith("get_actions: Fsm not initialized:") ||
            condition.StartsWith("get_fsm: Fsm not initialized:"))
            Log.InfoOnce($"warn|{condition}",
                $"[PlayMakerCtx] {condition} (root cause: inactive Silksong_GameManager — FSM init chain aborted)");
    }

    // The FSM whose action raised the warning: HK's execution stack first, then Silksong's (for warnings from Hornet's
    // own PlayMaker runtime), then the Fsm.Update fallback.
    private static string ResolveCaller() {
        var hk = FsmExecutionStack.ExecutingFsm;
        if (hk != null) return $"{hk.OwnerName}/{hk.Name}";
        var ss = SExecStack.ExecutingFsm;
        if (ss != null) return $"{ss.OwnerName}/{ss.Name}";
        return currentFsmContext ?? "(unknown FSM)";
    }

    internal static void Cleanup() {
        getGameObjectFsmHook?.Dispose();
        getGameObjectFsmHook = null;
        fsmUpdateHook?.Dispose();
        fsmUpdateHook = null;
        Application.logMessageReceived -= OnLogMessage;
    }
}
