extern alias Silksong;
extern alias SilksongLoc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HornetInHallownest.Util;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace HornetInHallownest.Core;

internal static class Stub {
    private static readonly List<ILHook> hooks = [];
    private static readonly List<Hook> detours = [];

    internal static void Install() {
        // Trigger Language's cctor inside a SilksongContext window so its localization sheets come from our bundle, not
        // HK's colliding Languages/*. Stubbing SendMonoMessage silences the cross-game ChangedLanguage broadcast, which
        // can't bind a Silksong.LanguageCode arg to HK's receivers. One MissingMethodException fires unavoidably at
        // startup: hooking SendMonoMessage forces the cctor, which runs its one broadcast before the detour is wired.
        var lang = typeof(SilksongLoc::TeamCherry.Localization.Language);
        using (SilksongContext.Enter()) {
            Skip(lang, "SendMonoMessage");
            RuntimeHelpers.RunClassConstructor(lang.TypeHandle);
        }

        Skip(typeof(Silksong::PersonalObjectPool), "OnStart");
        Skip(typeof(Silksong::HeroAnimationController), "UpdateToolEquipFlags");

        // ControlReminder button-prompt UI: its GO is deactivated in BringUpHud, so Instance/Owner are null here.
        Skip(typeof(Silksong::ControlReminder), "get_Instance", true);
        Skip(typeof(Silksong::ControlReminder.ConfigBase), "Disappear", true);
        Skip(typeof(Silksong::ControlReminder), "PushSingle", true);
        Skip(typeof(Silksong::ControlReminder), "ShowPushed", true);
        Skip(typeof(Silksong::ControlReminder.SingleConfig), "Appear", true);
        Skip(typeof(Silksong::ControlReminder.DoubleConfig), "Appear", true);

        Skip(typeof(Silksong::HeroNailImbuement), "Awake");
        Skip(typeof(Silksong::FollowTransform), "OnEnable");
        Skip(typeof(Silksong::MappableControllerButton), "ShowCurrentBinding"); // SetupRef NullRef

        Skip(typeof(Silksong::HeroController), "SetupDeliveryItems"); // delivery quests out of scope
        Skip(typeof(Silksong::DeliveryHudIcon), "OnPreUpdateDisplay"); // GetActiveItems NullRef; the icon hides itself
        Skip(typeof(Silksong::HeroController), "StartDashEffect"); // cosmetic effect prefabs unresolved, NullRef aborts HeroDash
        Skip(typeof(Silksong::SetParticleScale), "OnUpdate", true); // per-frame, null parentBody
        Skip(typeof(Silksong::DeliveryQuestItem), "BreakAllInternal");
        Skip(typeof(Silksong::DeliveryQuestItem), "TakeHit"); // Superjump ceiling-slam, GetActiveItems NullRef hangs "Hit Roof" FSM
        Skip(typeof(Silksong::GameManager), "FadeSceneIn"); // Silksong scene fade fights HK's; HK owns the fade
        Skip(typeof(Silksong::GameManager), "AwardAchievement", true); // achievementHandler null; achievements irrelevant
        Skip(typeof(Silksong::GameManager), "UpdateAchievementProgress", true);

        // HeroController.Start's initial AddSilk NullRefs in SpawnNewChunk (null animator); suppress so Start completes.
        var addSilk = typeof(Silksong::HeroController).GetMethod("AddSilk",
            BindingFlags.Instance | BindingFlags.Public, null,
            [typeof(int), typeof(bool), typeof(Silksong::SilkSpool.SilkAddSource), typeof(bool)], null);
        if (addSilk != null)
            detours.Add(new Hook(addSilk,
                (Action<Action<Silksong::HeroController, int, bool, Silksong::SilkSpool.SilkAddSource, bool>,
                    Silksong::HeroController, int, bool, Silksong::SilkSpool.SilkAddSource, bool>)
                ((orig, self, amount, heroEffect, source, force) => {
                    try {
                        orig(self, amount, heroEffect, source, force);
                    } catch (Exception e) {
                        Log.InfoOnce("addsilk-guard", $"[AddSilk] caught addsilk NullRef {e.Message}");
                    }
                })));
        else
            Log.Error("[Stub] HeroController.AddSilk(int,bool,SilkAddSource,bool) not found");

        Skip(typeof(Silksong::CameraController), "ScreenFlash"); // SimpleFadeOut.SetColor NullRef (Awake never ran)
        Skip(typeof(Silksong::GameCameras), "Start"); // gs null (SetupGameRefs skipped); overscan cosmetic, HK owns camera
        Skip(typeof(Silksong::HUDCamera), "OnEnable"); // GM.inputHandler null + menu plumbing; HUD renders regardless
        Skip(typeof(Silksong::GameCameras), "Awake"); // non-root DDOL warning; GameCamerasBootstrap sets _instance itself
        Skip(typeof(Silksong::InventoryItemWideMapZone), "get_IsUnlocked"); // gameMap null; no map, correctly locked
        Skip(typeof(Silksong::QuestItemManager), "Awake"); // GetItems over null quest source, ArgumentNullException
        Skip(typeof(Silksong::QuestManager), "MaybeShowQuestUpdated", true); // no QuestManager; quest-update UI moot
        Skip(typeof(Silksong::InventoryMapManager), "OnPaneStart"); // GameMap uninitialized; map pane inert

        // GetActiveQuests/GetAcceptedQuests return null without a QuestManager, so callers foreach over null; return Empty.
        var qm = typeof(Silksong::QuestManager);
        var getActive = qm.GetMethod("GetActiveQuests", BindingFlags.Public | BindingFlags.Static);
        var getAccepted = qm.GetMethod("GetAcceptedQuests", BindingFlags.Public | BindingFlags.Static);
        if (getActive != null)
            hooks.Add(new ILHook(getActive, il => ReturnEmpty(il, typeof(Silksong::FullQuestBase))));
        if (getAccepted != null)
            hooks.Add(new ILHook(getAccepted, il => ReturnEmpty(il, typeof(Silksong::BasicQuestBase))));

        // Inventory opens as an overlay, not via Silksong's pause: SetPausedState freezes HK's world and our InputDriver.
        Skip(typeof(Silksong::GameManager), "SetPausedState");

        // Death-FSM actions that deref the un-run GM's audio/fader (null) before their Finish(), so they hang regardless.
        Skip(typeof(Silksong::HutongGames.PlayMaker.Actions.ApplyMusicCue), "OnEnter");
        Skip(typeof(Silksong::HutongGames.PlayMaker.Actions.ScreenFader), "OnEnter");
    }

    // Stub every method named `method` on `type` (all overloads/visibilities) to log-once + return default.
    private static void Skip(Type type, string method, bool silent = false) {
        var found = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                    BindingFlags.NonPublic);
        var any = false;
        foreach (var mi in found) {
            if (mi.Name != method || mi.IsAbstract || mi.GetMethodBody() == null) continue;
            var label = $"{type.Name}.{mi.Name}";
            try {
                hooks.Add(new ILHook(mi, il => Rewrite(il, label, silent)));
                any = true;
            } catch (Exception e) {
                Log.Error($"[Stub] hook failed {label}: {e.Message}");
            }
        }

        if (!any) Log.Error($"[Stub] no method '{method}' on {type.FullName}");
    }

    // Called from stubbed methods (emitted by Rewrite). Logged dedups per label; Silent never logs.
    public static void Logged(string label) => Log.DebugOnce($"stub|{label}", $"[Stub] >> {label} (stubbed, no-op)");
    public static void Silent(string label) { }

    private static void Rewrite(ILContext il, string label, bool silent = false) {
        il.Body.Instructions.Clear();
        il.Body.ExceptionHandlers.Clear();
        il.Body.Variables.Clear();
        var c = new ILCursor(il);
        c.Emit(OpCodes.Ldstr, label);
        c.Emit(OpCodes.Call, typeof(Stub).GetMethod(silent ? nameof(Silent) : nameof(Logged))!);
        EmitDefaultReturn(c, il);
    }

    private static void ReturnEmpty(ILContext il, Type elementType) {
        il.Body.Instructions.Clear();
        var c = new ILCursor(il);
        c.Emit(OpCodes.Call, typeof(Enumerable).GetMethod(nameof(Enumerable.Empty))!.MakeGenericMethod(elementType));
        c.Emit(OpCodes.Ret);
    }

    private static void EmitDefaultReturn(ILCursor c, ILContext il) {
        var rt = il.Method.ReturnType;
        if (rt.MetadataType == MetadataType.Void) {
            c.Emit(OpCodes.Ret);
        }
        else if (!rt.IsValueType) {
            c.Emit(OpCodes.Ldnull);
            c.Emit(OpCodes.Ret);
        }
        else {
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
        foreach (var h in detours) h.Dispose();
        hooks.Clear();
        detours.Clear();
    }
}
