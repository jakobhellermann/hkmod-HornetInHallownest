extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace HornetInHallownest.Modules;

// HeroProxy repoints the playmaker "Hero" global to Hornet, and many `CallMethodProper` already work transparently.
// Remap those methods that don't match 1:1, and ignore those that aren't supported for now.
// Also disambiguate method calls that do work, but won't resolve due to ambiguous overload.
public sealed class FsmMethodCallRemapModule : ModuleBase {
    private static readonly Dictionary<string, string> methodRedirects = new() {
        { "CanTalk", "CanInspect" },
        { "CanFocus", "CanCast" }
    };

    #region Replacement methods
    private static readonly MethodInfo? spaChargeStandIn =
        typeof(FsmMethodCallRemapModule).GetMethod(nameof(SpaChargeSilk), BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo? noOpStandIn =
        typeof(FsmMethodCallRemapModule).GetMethod(nameof(HkOnlyNoOp), BindingFlags.NonPublic | BindingFlags.Static);
    
    private static bool SpaChargeSilk() {
        if (HornetSpawner.Hornet is { } hero) hero.AddSilk(1, true);
        return true;
    }

    private static bool HkOnlyNoOp() {
        return false;
    }
    #endregion

    // HK-only MP methods called on Hornet -> the stand-in that runs instead. Hornet has no soul, so most no-op; the
    // hot-spring soul refill is the exception, mapped onto a silk gain.
    private static readonly Dictionary<string, MethodInfo?> hkMethodStandIns = new() {
        { "ClearMP", noOpStandIn },
        { "ClearMPSendEvents", noOpStandIn },
        { "AddMPCharge", noOpStandIn },
        { "StartMPDrain", noOpStandIn },
        { "StopMPDrain", noOpStandIn },
        { "SetMPCharge", noOpStandIn },
        { "TryAddMPChargeSpa", spaChargeStandIn },
        { "AddMPChargeSpa", spaChargeStandIn }
    };

    public override string Id => "fsm-method-remap";

    public override void Initialize() {
        Detour(typeof(CallMethodProper), "DoCache", DoCacheHook);
        Detour(typeof(CallMethodProper), "DoMethodCall", DoMethodCallHook);
    }

    // CallMethodProper caches the resolved behaviour instance on the first call and invokes on it forever, after we
    // retarget Hero (switch or respawn) that's stale.
    private static void DoMethodCallHook(Action<CallMethodProper> orig, CallMethodProper self) {
        if (self.GetFieldValue<MethodInfo>("cachedMethodInfo") != null) {
            var target = self.Fsm.GetOwnerDefaultTarget(self.gameObject);
            var cached = self.GetFieldValue<UnityEngine.Object>("cachedBehaviour") as Component;
            if (target && (!cached || cached.gameObject != target))
                self.SetFieldValue("cachedMethodInfo", null);
        }

        orig(self);
    }

    private bool DoCacheHook(Func<CallMethodProper, bool> orig, CallMethodProper self) {
        try {
            return orig(self) || TryRecoverMiss(self);
        } catch (AmbiguousMatchException) {
            return TryDisambiguate(self);
        }
    }

    private bool TryRecoverMiss(CallMethodProper self) {
        var type = self.GetFieldValue<Type>("cachedType");
        var methodName = self.methodName?.Value;

        if (type != null && methodName != null &&
            methodRedirects.TryGetValue(methodName, out var redirect) &&
            type.GetMethod(redirect, Type.EmptyTypes) is { } altMethod) {
            SetCached(self, altMethod);
            LogDebugOnce($"redirect|{methodName}|{type.Name}",
                $"redirected '{methodName}' -> '{redirect}' on {type.Name}");
            return true;
        }

        if (HeroSwitch.HornetActive && methodName != null &&
            hkMethodStandIns.TryGetValue(methodName, out var standIn)) {
            if (standIn == null) return false;
            SetCached(self, standIn);
            LogDebugOnce($"hkonly|{methodName}", $"HK-only '{methodName}' -> {standIn.Name}");
            return true;
        }

        LogError($"method '{methodName}' not found on {type?.Name ?? "?"}");
        return false;
    }

    // orig set cachedType before GetMethod threw; fall back to the resolved component's type if not.
    private bool TryDisambiguate(CallMethodProper self) {
        var type = self.GetFieldValue<Type>("cachedType");
        if (type == null) {
            var comp = self.GetFieldValue<MonoBehaviour>("component");
            if (comp != null) type = comp.GetType();
        }

        var methodName = self.methodName?.Value;
        var parameters = self.parameters;

        if (type == null || methodName == null) {
            LogError("AmbiguousMatch but missing type/method");
            return false;
        }

        var resolved = parameters is { Length: > 0 }
            ? type.GetMethod(methodName, parameters.Select(p => p.RealType ?? typeof(object)).ToArray())
            : type.GetMethod(methodName, Type.EmptyTypes);

        if (resolved != null) {
            SetCached(self, resolved);
            LogDebugOnce($"disambig|{methodName}|{type.Name}",
                $"disambiguated '{methodName}' on {type.Name} -> {resolved}");
            return true;
        }

        self.SetFieldValue("errorString", $"ambiguous '{methodName}' on {type.Name} - no overload matched\n");
        LogError($"ambiguous '{methodName}' on {type.Name} - could not resolve, skipping");
        return false;
    }

    private static void SetCached(CallMethodProper self, MethodInfo method) {
        self.SetFieldValue("cachedMethodInfo", method);
        self.SetFieldValue("cachedParameterInfo", method.GetParameters());
    }
}
