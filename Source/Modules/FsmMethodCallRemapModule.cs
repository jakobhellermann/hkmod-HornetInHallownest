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

    private const float SpaMaskInterval = 1f;

    private static float nextSpaMaskTime;

    private static readonly MethodInfo? spaStandIn =
        typeof(FsmMethodCallRemapModule).GetMethod(nameof(SpaHeal), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo? noOpStandIn =
        typeof(FsmMethodCallRemapModule).GetMethod(nameof(HkOnlyNoOp), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo? clearStandIn =
        typeof(FsmMethodCallRemapModule).GetMethod(nameof(ClearSilk), BindingFlags.NonPublic | BindingFlags.Static);

    // HK's hot springs refill only the Knight's soul; Hornet's also heal her masks.
    private static bool SpaHeal() {
        if (HornetSpawner.Hornet is not { } hero) return true;
        hero.AddSilk(1, true);
        var pd = Silksong::PlayerData.instance;
        if (Time.time < nextSpaMaskTime || pd.health >= pd.CurrentMaxHealth) return true;
        hero.AddHealth(1);
        nextSpaMaskTime = Time.time + SpaMaskInterval;
        return true;
    }

    private static bool ClearSilk() {
        if (HornetSpawner.Hornet is { } hero) hero.TakeSilk(Silksong::PlayerData.instance.silk);
        return true;
    }

    private static bool HkOnlyNoOp() {
        return false;
    }

    #endregion

    // HK-only MP methods called on Hornet -> the stand-in that runs instead. Hornet has no soul, so most no-op; the
    // hot-spring soul refill and the Hall of Gods' soul drain are the exceptions, mapped onto her silk and masks.
    private static readonly Dictionary<string, MethodInfo?> hkMethodStandIns = new() {
        { "ClearMP", clearStandIn },
        { "ClearMPSendEvents", clearStandIn },
        { "AddMPCharge", noOpStandIn },
        { "StartMPDrain", noOpStandIn },
        { "StopMPDrain", noOpStandIn },
        { "SetMPCharge", noOpStandIn },
        { "TryAddMPChargeSpa", spaStandIn },
        { "AddMPChargeSpa", spaStandIn }
    };

    // Mask and soul vessel pickups `SendMessage` their max-up to "Hero", i.e. Hornet, whose HeroController has no
    // AddToMaxMPReserve and whose AddToMaxHealth writes only her PD. PlayerDataSyncModule mirrors HK PD onto hers, so the
    // upgrade has to land on the Knight as well.
    private static readonly Dictionary<string, Action<HeroController, int>> knightUpgrades = new() {
        { "AddToMaxHealth", (knight, amount) => knight.AddToMaxHealth(amount) },
        { "AddToMaxMPReserve", (knight, amount) => knight.AddToMaxMPReserve(amount) }
    };

    // HK's SpriteFlash runs one flash at a time, so each flash its FSMs send ends the repeating one before it, such as a
    // dream warrior's absorb flashing. Silksong's layers them.
    private static int? hkRepeatingFlashId;

    public override string Id => "fsm-method-remap";

    public override void Initialize() {
        Detour(typeof(CallMethodProper), "DoCache", DoCacheHook);
        Detour(typeof(CallMethodProper), "DoMethodCall", DoMethodCallHook);
        Detour(typeof(SendMessage), "DoSendMessage", DoSendMessageHook);
    }

    protected override void OnDeinitialize() => hkRepeatingFlashId = null;

    private static void DoSendMessageHook(Action<SendMessage> orig, SendMessage self) {
        var hornet = HornetSpawner.HornetRoot;
        var functionName = self.functionCall.FunctionName;
        if (!hornet || self.Fsm.GetOwnerDefaultTarget(self.gameObject) != hornet) {
            orig(self);
            return;
        }

        var flash = functionName.StartsWith("flash", StringComparison.OrdinalIgnoreCase)
            ? hornet.GetComponent<Silksong::SpriteFlash>()
            : null;
        if (flash && hkRepeatingFlashId is { } id) {
            flash.CancelRepeatingFlash(id);
            hkRepeatingFlashId = null;
        }

        orig(self);
        if (flash && functionName.StartsWith("Flashing", StringComparison.Ordinal))
            hkRepeatingFlashId = flash.GetFieldValue<int>("lastFlashId");

        var knight = HeroController.UnsafeInstance;
        if (knight && knightUpgrades.TryGetValue(functionName, out var deliver))
            deliver(knight, self.functionCall.IntParameter.Value);
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
