using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// HK's CallMethodProper.DoCache() does `Type.GetMethod(name)` with NO AmbiguousMatchException fallback (Silksong's
// version has one). When HeroProxy repoints the PlayMaker "Hero" global to Hornet's GO, HK FSMs resolve
// CallMethodProper's behaviour on Silksong's HeroController (via GetComponentShim). Some method names have multiple
// overloads on Silksong's HeroController that don't exist on HK's → AmbiguousMatchException → the FSM state's OnEnter
// throws → the transition aborts → controlReqlinquished sticks, abilities die silently.
//
// Fix: hook DoCache to catch AmbiguousMatchException, disambiguate using the action's parameter RealTypes (the same
// approach Silksong's DoCache uses), and log the scene + GO + FSM + method so we can see what's being resolved
// cross-game. If disambiguation fails, return false (→ DoMethodCall logs errorString + Finish — the FSM skips the
// call gracefully instead of crashing the state).
internal static class CallMethodProperFix {
    private static Hook? hook;

    private static readonly BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo? methodNameField =
        typeof(CallMethodProper).GetField("methodName", AllInstance);

    private static readonly FieldInfo? parametersField =
        typeof(CallMethodProper).GetField("parameters", AllInstance);

    private static readonly FieldInfo? cachedTypeField =
        typeof(CallMethodProper).GetField("cachedType", AllInstance);

    private static readonly FieldInfo? cachedMethodInfoField =
        typeof(CallMethodProper).GetField("cachedMethodInfo", AllInstance);

    private static readonly FieldInfo? cachedParameterInfoField =
        typeof(CallMethodProper).GetField("cachedParameterInfo", AllInstance);

    // HK HeroController methods that Silksong doesn't have, mapped to their closest Silksong equivalent.
    // When CallMethodProper resolves behaviour="HeroController" on Hornet (via HeroProxy+GetComponentShim),
    // HK-specific method names fail with "Method Name is invalid". The map re-routes them to the Silksong equivalent
    // so the calling HK FSM gets a sensible result instead of silently canceling.
    // Methods not in this map and not on Silksong's HeroController will still fail — they show up in the
    // "method not found" log so we can add them as needed.
    private static readonly Dictionary<string, string> MethodRedirects = new() {
        { "CanTalk", "CanInspect" }, // HK NPC interaction gate -> Silksong's interact gate
        { "CanFocus", "CanCast" } // HK focus/heal gate -> Silksong's bind/cast gate
    };

    // HK-only methods with no Silksong equivalent. When Hornet is active and HK HUD FSMs call these on
    // Silksong's HeroController (via HeroProxy), they no-op instead of logging an error.
    private static readonly HashSet<string> HkOnlyMethods = new() {
        "ClearMP", "ClearMPSendEvents", "AddMPCharge", "StartMPDrain", "StopMPDrain",
        "TryAddMPChargeSpa", "AddMPChargeSpa", "SetMPCharge"
    };

    // The subset of HkOnlyMethods that should still have an effect on Hornet: HK's hot spring passively
    // refills SOUL while the hero rests in it (HeroController.TryAddMPChargeSpa). Hornet has no soul — her
    // equivalent resource is silk — so these map onto a silk gain, the same soul->silk seam as SoulOrbBridge.
    private static readonly HashSet<string> SpaChargeMethods = new() {
        "TryAddMPChargeSpa", "AddMPChargeSpa"
    };

    // Parameterless stand-ins invoked in place of the HK-only methods when Hornet is active. Pointing
    // cachedMethodInfo at a parameterless method makes DoMethodCall take its `cachedParameterInfo.Length == 0`
    // branch (Invoke(cachedBehaviour, null)), so the FSM's argument list is ignored entirely — no signature/
    // count matching — and DoCache returns true, so HK never logs "Method Name is invalid".
    private static readonly MethodInfo? spaChargeStandIn =
        typeof(CallMethodProperFix).GetMethod(nameof(SpaChargeSilk), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo? noOpStandIn =
        typeof(CallMethodProperFix).GetMethod(nameof(HkOnlyNoOp), BindingFlags.NonPublic | BindingFlags.Static);

    // Grant Hornet one silk (like SoulOrbBridge's per-orb grant); return true so the spa FSM's "Did Add"
    // result / "MP GAIN SPA" success path proceeds. AddSilk clamps to the spool max, so repeated spa ticks
    // just top her off.
    private static bool SpaChargeSilk() {
        if (BundleSpike.RealHero is { } hero)
            hero.AddSilk(1, true);
        return true;
    }

    // The remaining HK-only MP methods (ClearMP/SetMPCharge/drain/...) have no Hornet equivalent — no-op.
    private static bool HkOnlyNoOp() => false;

    private static readonly FieldInfo? errorStringField =
        typeof(CallMethodProper).GetField("errorString", AllInstance);

    private static readonly FieldInfo? componentField =
        typeof(CallMethodProper).GetField("component", AllInstance);

    private static readonly FieldInfo? gameObjectField =
        typeof(CallMethodProper).GetField("gameObject", AllInstance);

    private static bool IsHkOnlyMethod(string? methodName) {
        return methodName != null && HkOnlyMethods.Contains(methodName);
    }

    internal static void Install() {
        var mi = typeof(CallMethodProper).GetMethod("DoCache", BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi == null) {
            Log.Error("[CallMethodProperFix] DoCache not found");
            return;
        }

        hook = new Hook(mi, DoCacheHook);
        Log.Debug("[CallMethodProperFix] installed");
    }

    // For an instance method `bool DoCache()`, MonoMod's orig delegate includes the instance: Func<CallMethodProper, bool>.
    private static bool DoCacheHook(Func<CallMethodProper, bool> orig, CallMethodProper self) {
        try {
            var result = orig(self);
            // orig returned false → method not found on the resolved type. HK's DoCache sets errorString
            // ("Method Name is invalid: X") which DoMethodCall logs with zero context. Add ours.
            if (!result) {
                var type = (Type?)cachedTypeField?.GetValue(self);
                var methodName = (methodNameField?.GetValue(self) as FsmString)?.Value;
                var fsm = self.Fsm;
                var scene = fsm?.Owner != null ? fsm.Owner.gameObject.scene.name : "?";
                var goName = fsm?.OwnerName ?? "?";
                var fsmName = fsm?.Name ?? "?";
                var stateName = self.State?.Name ?? "?";

                // Try method redirect (HK method name -> Silksong equivalent)
                if (type != null && methodName != null && MethodRedirects.TryGetValue(methodName, out var redirect)) {
                    var altMethod = type.GetMethod(redirect, Type.EmptyTypes);
                    if (altMethod != null) {
                        cachedMethodInfoField?.SetValue(self, altMethod);
                        cachedParameterInfoField?.SetValue(self, altMethod.GetParameters());
                        Log.DebugOnce($"redirect|{methodName}|{type.Name}|{goName}|{fsmName}|{stateName}",
                            $"[CallMethodProperFix] redirected '{methodName}' -> '{redirect}' on {type.Name} (scene={scene} go={goName} fsm={fsmName} state={stateName})");
                        return true;
                    }
                }

                // Known HK-only methods called on Silksong's HeroController while Hornet is active. Returning
                // false here does NOT suppress the error — HK's DoMethodCall logs errorString ("Method Name is
                // invalid: X") for any DoCache that returns false. Instead redirect to a parameterless stand-in
                // and return true, so the call resolves cleanly with no error. The spa soul-charge maps to a
                // silk gain; the rest no-op.
                if (HeroSwitch.HornetActive && IsHkOnlyMethod(methodName)) {
                    var standIn = SpaChargeMethods.Contains(methodName!) ? spaChargeStandIn : noOpStandIn;
                    if (standIn != null) {
                        cachedMethodInfoField?.SetValue(self, standIn);
                        cachedParameterInfoField?.SetValue(self, standIn.GetParameters());
                        Log.DebugOnce($"hkonly|{methodName}|{goName}|{fsmName}|{stateName}",
                            $"[CallMethodProperFix] HK-only '{methodName}' -> {standIn.Name} (scene={scene} go={goName} fsm={fsmName} state={stateName})");
                        return true;
                    }

                    return false;
                }
                Log.Error(
                    $"[CallMethodProperFix] method '{methodName}' not found on {type?.Name ?? "?"} (scene={scene} go={goName} fsm={fsmName} state={stateName})");
            }

            return result;
        } catch (AmbiguousMatchException) {
            // orig already set cachedType + cachedBehaviour before GetMethod threw — but fall back to component if not.
            var type = (Type?)cachedTypeField?.GetValue(self);
            if (type == null) {
                var comp = (MonoBehaviour?)componentField?.GetValue(self);
                type = comp?.GetType();
            }

            var methodName = (methodNameField?.GetValue(self) as FsmString)?.Value;
            var parameters = (FsmVar[]?)parametersField?.GetValue(self);
            var fsm = self.Fsm;

            // Build a rich label: scene + owning GO + FSM name + state
            var scene = fsm?.Owner != null ? fsm.Owner.gameObject.scene.name : "?";
            var goName = fsm?.OwnerName ?? "?";
            var fsmName = fsm?.Name ?? "?";
            var stateName = self.State?.Name ?? "?";
            var label = $"scene={scene} go={goName} fsm={fsmName} state={stateName}";

            if (type == null || methodName == null) {
                Log.Error($"[CallMethodProperFix] AmbiguousMatch but missing fields ({label})");
                return false;
            }

            // Disambiguate using parameter types (same approach as Silksong's DoCache)
            MethodInfo? resolved;
            if (parameters is { Length: > 0 }) {
                var types = parameters.Select(p => p.RealType ?? typeof(object)).ToArray();
                resolved = type.GetMethod(methodName, types);
            }
            else {
                resolved = type.GetMethod(methodName, Type.EmptyTypes);
            }

            if (resolved != null) {
                cachedMethodInfoField?.SetValue(self, resolved);
                cachedParameterInfoField?.SetValue(self, resolved.GetParameters());
                Log.DebugOnce($"disambig|{methodName}|{type.Name}|{label}",
                    $"[CallMethodProperFix] disambiguated '{methodName}' on {type.Name} ({label}) -> {resolved}");
                return true;
            }

            errorStringField?.SetValue(self,
                $"[CallMethodProperFix] ambiguous '{methodName}' on {type.Name} — no overload matched parameter types ({label})\n");
            Log.Error(
                $"[CallMethodProperFix] ambiguous '{methodName}' on {type.Name} ({label}) — could not resolve, skipping");
            return false;
        }
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }
}
