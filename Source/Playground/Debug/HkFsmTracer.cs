using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.RuntimeDetour;
using Fsm = HutongGames.PlayMaker.Fsm;
using FsmState = HutongGames.PlayMaker.FsmState;
using FsmEvent = HutongGames.PlayMaker.FsmEvent;

namespace HornetPlayer.Playground;

// HK-side twin of FsmTracer. FsmTracer hooks the ISOLATED Silksong.PlayMaker runtime (Hornet's FSMs); this hooks HK's
// SHARED PlayMaker (unaliased HutongGames.PlayMaker.Fsm) — the runtime that HK's SCENE FSMs run on (npc_control,
// Conversation Control, the Dreamer cutscene, scene-transition FSMs, …). Use it to pin exactly where an HK cutscene
// hangs (e.g. the dreamer "get Dream Nail" whitescreen) instead of theorising from the pseudocode dumps.
//
// Same shape as FsmTracer: hooks Fsm.SwitchState (definitive state change) + both Fsm.Event overloads (triggers),
// filtered to a settable set of FSM names. POST /hk-fsm-trace?names=Dreamer Scene 2,Conversation Control  (empty = off).
// Every detour try/catches its logging and ALWAYS calls orig last — observing must never break the FSM.
internal static class HkFsmTracer {
    private static readonly List<Hook> hooks = new();
    private static readonly HashSet<string> targets = new(StringComparer.Ordinal);

    internal static object SetTargets(string? csv) {
        targets.Clear();
        if (!string.IsNullOrWhiteSpace(csv))
            foreach (var n in csv.Split(',')) {
                var t = n.Trim();
                if (t.Length > 0) targets.Add(t);
            }

        Log.Info($"[HkFsmTrace] tracing: {(targets.Count == 0 ? "(off)" : string.Join(", ", targets))}");
        return new { tracing = targets.ToArray() };
    }

    // A target is either a bare FSM name ("Conversation Control") or "Name@GameObject" ("Control@Dreamer Scene 2") to
    // disambiguate a generic FSM name (many GOs have a "Control" FSM) down to one GO.
    private static bool Traced(Fsm fsm) {
        if (fsm == null || targets.Count == 0) return false;
        if (targets.Contains(fsm.Name)) return true;
        return targets.Contains($"{fsm.Name}@{fsm.GameObjectName}");
    }

    internal static void Install() {
        if (hooks.Count > 0) return;
        var t = typeof(Fsm);
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
                        Log.Info($"[HkFsmTrace] {fsm.Name}@{fsm.GameObjectName}: '{from}' --> '{to?.Name}'  [{acts}]");
                    }
                } catch (Exception e) {
                    Log.Error($"[HkFsmTrace] SwitchState log: {e.Message}");
                }

                orig(fsm, to!);
            }));
        Add(t.GetMethod("Event", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(FsmEvent) }, null),
            (Action<Action<Fsm, FsmEvent>, Fsm, FsmEvent>)((orig, fsm, ev) => {
                try {
                    if (Traced(fsm) && !string.IsNullOrEmpty(ev?.Name))
                        Log.Info(
                            $"[HkFsmTrace] {fsm.Name}: EVENT '{ev.Name}' <- {ActiveAction(fsm)} (state '{fsm.ActiveStateName}')");
                } catch (Exception e) {
                    Log.Error($"[HkFsmTrace] Event log: {e.Message}");
                }

                orig(fsm, ev!);
            }));
        Add(t.GetMethod("Event", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null),
            (Action<Action<Fsm, string>, Fsm, string>)((orig, fsm, name) => {
                try {
                    if (Traced(fsm) && !string.IsNullOrEmpty(name))
                        Log.Info(
                            $"[HkFsmTrace] {fsm.Name}: EVENT(str) '{name}' <- {ActiveAction(fsm)} (state '{fsm.ActiveStateName}')");
                } catch (Exception e) {
                    Log.Error($"[HkFsmTrace] Event(str) log: {e.Message}");
                }

                orig(fsm, name);
            }));

        Log.Debug($"[HkFsmTrace] installed ({hooks.Count} hooks; POST /hk-fsm-trace?names=... to arm)");
    }

    private static string ActiveAction(Fsm fsm) {
        var st = fsm.ActiveState;
        var a = st?.ActiveAction;
        return a != null ? $"[{st!.ActiveActionIndex}]{a.GetType().Name}" : "(no action)";
    }

    private static void Add(MethodInfo? mi, Delegate detour) {
        if (mi == null) {
            Log.Error("[HkFsmTrace] method not found");
            return;
        }

        try {
            hooks.Add(new Hook(mi, detour));
        } catch (Exception e) {
            Log.Error($"[HkFsmTrace] hook failed: {e.Message}");
        }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        targets.Clear();
    }
}
