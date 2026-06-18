extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace HornetPlayer.Playground;

// Stub out Silksong methods we don't want to run yet (environment managers, FSM actions that NullRef because the full
// game runtime isn't set up). We stub the CALLEE (the leaf method that crashes), not its callers — minimal surface,
// and callers keep whatever they do around the call. Mechanism: MonoMod RuntimeDetour ILHook (no HookGen, no Harmony)
// rewrites the method body to "log once + return default". The code lives in the prefixed Silksong binary, so we can't
// edit it as source; the ILHook is our "guard".
internal static class Stub {
    private static readonly List<ILHook> hooks = new();
    private static readonly HashSet<string> logged = new();

    // The methods that NullRef on spawn because the full game runtime isn't set up (callees, leaf methods).
    // Identified from Player.log on a real spawn — see TODO.md / docs.
    internal static void Install() {
        Skip(typeof(Silksong::HeroWaterController), "Update");                                  // per-frame
        Skip(typeof(Silksong::PersonalObjectPool), "OnStart");                                  // Start
        Skip(typeof(Silksong::HeroAnimationController), "UpdateToolEquipFlags");                // Start
        Skip(typeof(Silksong::HutongGames.PlayMaker.Actions.ListenForTauntV2), "OnUpdate");     // FSM action, per-frame
        // Tool-equipment subsystem isn't initialized -> IsToolEquipped NullRefs; stub the root (no tools equipped),
        // which should cascade-fix ToolItem.IsEquipped / CheckIfToolEquipped / ToolEquipChecker / HeroWispLantern.
        Skip(typeof(Silksong::ToolItemManager), "IsToolEquipped");
        Skip(typeof(Silksong::KeepWorldScalePositive), "OnEnable");
        Skip(typeof(Silksong::HutongGames.PlayMaker.Actions.SetPolygonCollider), "OnEnter");
        Skip(typeof(Silksong::HeroNailImbuement), "Awake");
        Skip(typeof(Silksong::FollowTransform), "OnEnable");
        // Input-listener FSM actions (ListenForAttack/Dash/QuickMap/Superdash/…) read InControl input state that
        // isn't alive in our context (InControl InputManager not initialized) -> NullRef in PlayerAction.WasPressed.
        // For a no-input bring-up we no-op them all (no input -> they'd fire no events anyway). Category stub.
        SkipAllInNamespace("HutongGames.PlayMaker.Actions", "ListenFor", "OnUpdate");
        // AddSilk -> GameCameras.instance.silkSpool (silk meter UI); GameCameras isn't bootstrapped. UI, not needed
        // for no-input bring-up. TODO: bootstrap GameCameras/silkSpool for the UI/combat phase.
        Skip(typeof(Silksong::HeroController), "AddSilk");
        Skip(typeof(Silksong::HeroController), "SetupDeliveryItems"); // delivery-quest setup entry — quests irrelevant
        Skip(typeof(Silksong::DeliveryQuestItem), "BreakAllInternal"); // also called directly from Start (BreakTimedNoEffects)
        // NOTE: SetConfigGroup's throw is FSMUtility.SendEventToGameObject -> list[i].Fsm.Event() on the hero's
        // PlayMakerFSMs, which aren't fully initialized (linked to the residual ~125 action-resolution failures).
        // NOT stubbed here (FSMUtility is broad / FSM is core) — tracked as the PlayMaker bring-up TODO.
    }

    // Stub `method` on every Silksong type in `ns` whose name starts with `prefix` (category stub).
    internal static void SkipAllInNamespace(string ns, string prefix, string method) {
        Type?[] types;
        try { types = typeof(Silksong::HeroController).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types; }
        var n = 0;
        foreach (var t in types) {
            if (t?.Namespace != ns || !t.Name.StartsWith(prefix)) continue;
            if (t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null) continue;
            Skip(t, method);
            n++;
        }
        Log.Info($"[Stub] category {ns}.{prefix}*::{method} -> {n} types");
    }

    // Stub every method named `method` on `type` (all overloads/visibilities) to log-once + return default.
    internal static void Skip(Type type, string method) {
        var found = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var any = false;
        foreach (var mi in found) {
            if (mi.Name != method || mi.IsAbstract || mi.GetMethodBody() == null) continue;
            var label = $"{type.Name}.{mi.Name}";
            try {
                hooks.Add(new ILHook(mi, il => Rewrite(il, label)));
                any = true;
            } catch (Exception e) {
                Log.Error($"[Stub] hook failed {label}: {e.Message}");
            }
        }
        if (!any) Log.Error($"[Stub] no method '{method}' on {type.FullName}");
        else Log.Info($"[Stub] installed: {type.Name}.{method}");
    }

    // Called from stubbed methods (emitted by Rewrite). Logs each distinct stub once to avoid per-frame spam.
    public static void Logged(string label) {
        if (logged.Add(label)) Log.Info($"[Stub] >> {label} (stubbed, no-op)");
    }

    private static void Rewrite(ILContext il, string label) {
        il.Body.Instructions.Clear();
        il.Body.ExceptionHandlers.Clear();
        il.Body.Variables.Clear();
        var c = new ILCursor(il);
        c.Emit(OpCodes.Ldstr, label);
        c.Emit(OpCodes.Call, typeof(Stub).GetMethod(nameof(Logged))!);
        EmitDefaultReturn(c, il);
    }

    private static void EmitDefaultReturn(ILCursor c, ILContext il) {
        var rt = il.Method.ReturnType;
        if (rt.MetadataType == MetadataType.Void) {
            c.Emit(OpCodes.Ret);
        } else if (!rt.IsValueType) {
            c.Emit(OpCodes.Ldnull);
            c.Emit(OpCodes.Ret);
        } else {
            var v = new VariableDefinition(rt);
            il.Body.Variables.Add(v);
            c.Emit(OpCodes.Ldloca, v);
            c.Emit(OpCodes.Initobj, rt);
            c.Emit(OpCodes.Ldloc, v);
            c.Emit(OpCodes.Ret);
        }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        logged.Clear();
    }
}
