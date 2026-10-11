extern alias Silksong;
using System;
using System.Collections;
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

    private static bool HkOnlyNoOp() {
        return false;
    }

    #endregion

    // HK-only MP methods called on Hornet -> the stand-in that runs instead. Hornet has no soul, so most no-op; the
    // hot-spring soul refill is the exception, mapped onto her silk and masks.
    private static readonly Dictionary<string, MethodInfo?> hkMethodStandIns = new() {
        { "ClearMP", noOpStandIn },
        { "ClearMPSendEvents", noOpStandIn },
        { "AddMPCharge", noOpStandIn },
        { "StartMPDrain", noOpStandIn },
        { "StopMPDrain", noOpStandIn },
        { "SetMPCharge", noOpStandIn },
        { "TryAddMPChargeSpa", spaStandIn },
        { "AddMPChargeSpa", spaStandIn }
    };

    // Soul vessel pickups `SendMessage` their max-up to "Hero", i.e. Hornet, whose HeroController has no
    // AddToMaxMPReserve. PlayerDataSyncModule mirrors HK PD onto hers, so the upgrade has to land on the Knight.
    private static readonly Dictionary<string, Action<HeroController, int>> knightUpgrades = new() {
        { "AddToMaxMPReserve", (knight, amount) => knight.AddToMaxMPReserve(amount) }
    };

    private const float MaskHealInterval = 0.15f;
    private const string RelinquishControl = "RelinquishControl";

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

        if (functionName == "AddToMaxHealth" && hornet.TryGetComponent<Silksong::HeroController>(out var hero)) {
            Runtime.StartCoroutine(AddMask(hero, self.functionCall.IntParameter.Value));
            return;
        }

        if (functionName == RelinquishControl) CancelMove(hornet);

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

    // Silksong's own mask shard fills her masks one at a time, then adds the new one full and sends "MAX HP UP": a heal
    // landing after it leaves the new mask no longer following damage. The Knight's AddToMaxHealth goes through SetInt,
    // so PlayerDataSyncModule raises her maximum and sends "MAX HP UP" from it; her health is full ahead of that.
    private static IEnumerator AddMask(Silksong::HeroController hero, int amount) {
        var pd = Silksong::PlayerData.instance;
        while (hero && pd.health < pd.CurrentMaxHealth) {
            hero.AddHealth(1);
            yield return new WaitForSeconds(MaskHealInterval);
        }

        if (hero) {
            hero.MaxHealth();
            pd.health = pd.maxHealth + amount;
        }

        if (HeroController.UnsafeInstance is { } knight) knight.AddToMaxHealth(amount);
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

        var hornet = HornetSpawner.HornetRoot;
        if (self.methodName?.Value == RelinquishControl && hornet &&
            self.Fsm.GetOwnerDefaultTarget(self.gameObject) == hornet)
            CancelMove(hornet);
        orig(self);
    }

    // Without an FSM CANCEL first, her move ends itself under HK's RelinquishControl and gives her control back.
    private static void CancelMove(GameObject hornet) {
        if (hornet.TryGetComponent<Silksong::HeroController>(out var hero) && HeroBroadcastModule.IsInMove(hero))
            HeroBroadcastModule.CancelMove(hero);
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
