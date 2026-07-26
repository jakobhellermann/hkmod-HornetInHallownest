extern alias SilksongPM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.RuntimeDetour;
using Fsm = SilksongPM::HutongGames.PlayMaker.Fsm;
using FsmState = SilksongPM::HutongGames.PlayMaker.FsmState;
using FsmEvent = SilksongPM::HutongGames.PlayMaker.FsmEvent;

namespace HornetInHallownest.Playground;

extern alias Silksong;

// Live FSM execution tracer for the isolated Silksong.PlayMaker runtime (Hornet's FSMs). The /fsm-dump* routes give a
// STATIC snapshot of an FSM's graph; this shows what actually RUNS at runtime: every state transition and every event,
// for a chosen FSM, as it happens. Use it to see exactly where an ability FSM goes wrong (e.g. which state it cancels
// from) instead of theorising from the structure.
//
// Hooks Fsm.SwitchState (the definitive state change) + both Fsm.Event overloads (the triggers). Filtered to a settable
// set of FSM names so it doesn't spam every FSM. Toggle via POST /fsm-trace?names=Bind,Spell Control  (empty/clear =
// off). State transitions also log the destination state's action TYPES, so the path reads as "what should run here".
internal static class FsmTracer {
    private static readonly List<Hook> hooks = new();
    private static readonly HashSet<string> targets = new(StringComparer.Ordinal);

    internal static object SetTargets(string? csv) {
        targets.Clear();
        if (!string.IsNullOrWhiteSpace(csv))
            foreach (var n in csv.Split(',')) {
                var t = n.Trim();
                if (t.Length > 0) targets.Add(t);
            }

        Log.Info($"[FsmTrace] tracing: {(targets.Count == 0 ? "(off)" : string.Join(", ", targets))}");
        return new { tracing = targets.ToArray() };
    }

    private static bool Traced(Fsm fsm) {
        return fsm != null && targets.Count > 0 && targets.Contains(fsm.Name);
    }

    internal static void Install() {
        if (hooks.Count > 0) return;
        var t = typeof(Fsm);
        // NB: every detour wraps its logging in try/catch and ALWAYS calls orig last — a logging error (e.g. a null
        // action in a state's Actions array) must never throw before orig and break the FSM we're only observing.
        Add(
            t.GetMethod("SwitchState", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(FsmState) },
                null),
            (Action<Action<Fsm, FsmState>, Fsm, FsmState>)((orig, fsm, to) => {
                try {
                    if (Traced(fsm)) {
                        var from = fsm.ActiveStateName ?? "(none)";
                        var acts = to?.Actions != null
                            ? string.Join(",", to.Actions.Select(a => a?.GetType().Name ?? "null"))
                            : "";
                        Log.Info($"[FsmTrace] {fsm.Name}@{fsm.GameObjectName}: '{from}' --> '{to?.Name}'  [{acts}]");
                    }
                } catch (Exception e) {
                    Log.Error($"[FsmTrace] SwitchState log: {e.Message}");
                }

                orig(fsm, to!);
            }));
        Add(t.GetMethod("Event", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(FsmEvent) }, null),
            (Action<Action<Fsm, FsmEvent>, Fsm, FsmEvent>)((orig, fsm, ev) => {
                try {
                    if (Traced(fsm) && !string.IsNullOrEmpty(ev?.Name))
                        Log.Info(
                            $"[FsmTrace] {fsm.Name}: EVENT '{ev.Name}' <- {ActiveAction(fsm)} (state '{fsm.ActiveStateName}')");
                } catch (Exception e) {
                    Log.Error($"[FsmTrace] Event log: {e.Message}");
                }

                orig(fsm, ev!);
            }));
        Add(t.GetMethod("Event", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null),
            (Action<Action<Fsm, string>, Fsm, string>)((orig, fsm, name) => {
                try {
                    if (Traced(fsm) && !string.IsNullOrEmpty(name))
                        Log.Info(
                            $"[FsmTrace] {fsm.Name}: EVENT(str) '{name}' <- {ActiveAction(fsm)} (state '{fsm.ActiveStateName}')");
                } catch (Exception e) {
                    Log.Error($"[FsmTrace] Event(str) log: {e.Message}");
                }

                orig(fsm, name);
            }));

        Log.Debug($"[FsmTrace] installed ({hooks.Count} hooks; POST /fsm-trace?names=... to arm)");
    }

    // Which action is currently executing — attributes an event to the action that sent it (e.g. which BoolTest fired
    // CANCEL). PlayMaker sets ActiveAction before each action's OnEnter/OnUpdate, so it's valid inside the Event hook.
    private static string ActiveAction(Fsm fsm) {
        var st = fsm.ActiveState;
        var a = st?.ActiveAction;
        return a != null ? $"[{st!.ActiveActionIndex}]{a.GetType().Name}" : "(no action)";
    }

    private static void Add(MethodInfo? mi, Delegate detour) {
        if (mi == null) {
            Log.Error("[FsmTrace] method not found");
            return;
        }

        try {
            hooks.Add(new Hook(mi, detour));
        } catch (Exception e) {
            Log.Error($"[FsmTrace] hook failed: {e.Message}");
        }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        targets.Clear();
    }
}
