extern alias Silksong;
extern alias SilksongLoc;
extern alias SilksongPM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HornetInHallownest.HornetInHallownest;
using HutongGames.PlayMaker;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace HornetInHallownest.Playground;

// Stub out Silksong methods we don't want to run yet (environment managers, FSM actions that NullRef because the full
// game runtime isn't set up). We stub the CALLEE (the leaf method that crashes), not its callers — minimal surface,
// and callers keep whatever they do around the call. Mechanism: MonoMod RuntimeDetour ILHook (no HookGen, no Harmony)
// rewrites the method body to "log once + return default". The code lives in the prefixed Silksong binary, so we can't
// edit it as source; the ILHook is our "guard".
internal static class Stub {
    private static readonly List<ILHook> hooks = new();
    private static readonly List<Hook> detours = new();

    // The methods that NullRef on spawn because the full game runtime isn't set up (callees, leaf methods).
    // Identified from Player.log on a real spawn — see TODO.md / docs.
    internal static void Install() {
        // Silksong's REMAPPED TeamCherry.Localization.Language is a SEPARATE type from HK's shared one. Its static ctor
        // runs LoadAvailableLanguages() + LoadLanguage() -> DoSwitch, which loads the localization sheets (the data we
        // WANT) and ends with SendMonoMessage("ChangedLanguage", Silksong.LanguageCode): a scene-wide
        // FindObjectsOfType<GameObject>() + BroadcastMessage that hits HK's own SetTextMeshProGameText/FontManager,
        // whose ChangedLanguage(HK.LanguageCode) can't bind a Silksong.LanguageCode arg -> MissingMethodException
        // (present-but-mismatched receiver; DontRequireReceiver doesn't save it). Stubbing SendMonoMessage routes all
        // FUTURE broadcasts (live language switches) into the no-op. The catch: installing this hook makes MonoMod's
        // GetFunctionPointer force Language's type-init, so the cctor runs (loads the sheets — good) and fires its ONE
        // broadcast BEFORE our detour is wired -> exactly ONE MissingMethodException at startup, unavoidably (we proved
        // hooking the .cctor itself also self-triggers it). Accepted as a single, understood startup error.
        //
        // We also use this controlled cctor trigger as a SilksongContext window (see SilksongContext / SilksongResources):
        // Silksong's Languages/*_General sheets exist in BOTH HK's Resources and our bundle, and by default HK wins ->
        // Silksong would read HK's strings. The window makes the cctor's sheet loads come from the Silksong bundle;
        // HK's localization already initialized at HK boot (outside this window) and is unaffected.
        var lang = typeof(SilksongLoc::TeamCherry.Localization.Language);
        using (SilksongContext.Enter()) {
            Skip(lang, "SendMonoMessage"); // installs stub; its GetFunctionPointer also forces type-init
            RuntimeHelpers
                .RunClassConstructor(lang.TypeHandle); // ensure the cctor ran inside the window (no-op if already)
        }

        Skip(typeof(Silksong::PersonalObjectPool), "OnStart"); // Start
        Skip(typeof(Silksong::HeroAnimationController), "UpdateToolEquipFlags"); // Start
        // ControlReminder lives under HudCamera/In-game/Prompts, which we deactivate in BringUpHud (its other deps
        // NullRef). Without a ControlReminder MonoBehaviour in the scene, ControlReminder.Instance (FindObjectOfType)
        // returns null. Stub the edges where the reminder system touches the missing environment:
        // - get_Instance: silently return null (prevents "Couldn't find ControlReminder instance" log ×3 from
        //   ToolItemManager.SceneInit's AddReminder calls; SubscribeEvents(null) is a graceful no-op).
        // - Disappear: derefs Owner.Hide(…) — Owner is null. Called by ReportBoundAttackToolUsed / SetEquippedTools.
        // - PushSingle/ShowPushed: derefs Instance.pushedSingles / Instance.ShowSingle — Instance is null. Called by
        //   ShowControlReminderSingleGroup FSM action (Bench/Death FSMs). Purely cosmetic button-prompt UI.
        // - Appear (SingleConfig/DoubleConfig): derefs Owner.ShowSingle/ShowDouble — Owner is null. Called by DoAppear
        //   after setting SeenToolEquipPrompt; the PlayerData side-effect is preserved, only the visual Show is skipped.
        Skip(typeof(Silksong::ControlReminder), "get_Instance", true);
        Skip(typeof(Silksong::ControlReminder.ConfigBase), "Disappear", true);
        Skip(typeof(Silksong::ControlReminder), "PushSingle", true);
        Skip(typeof(Silksong::ControlReminder), "ShowPushed", true);
        Skip(typeof(Silksong::ControlReminder.SingleConfig), "Appear", true);
        Skip(typeof(Silksong::ControlReminder.DoubleConfig), "Appear", true);
        Skip(typeof(Silksong::HeroNailImbuement), "Awake");
        Skip(typeof(Silksong::FollowTransform), "OnEnable");
        // NullReferenceException in MappableControllerButton.SetupRef
        Skip(typeof(Silksong::MappableControllerButton), "ShowCurrentBinding");
        // NOTE: the PlayMaker-ACTION stubs (SetPolygonCollider.OnEnter, ListenForTauntV2/ListenFor* OnUpdate) were
        // removed. They were workarounds for the pre-B era when those actions MIS-RESOLVED to HK's same-named versions
        // (wrong field layout -> NullRef). With Silksong.PlayMaker isolation they resolve to the correct Silksong
        // versions, so they must RUN: a stubbed action never calls Finish(), hanging its FSM state forever (e.g. Sprint
        // stuck in "Cancel All", never returning to Idle to accept DASHED), and stubbed ListenFor* suppress the very
        // input->FSM events the moves need. Input is alive now (InputDriver + buttonQueueTimers).
        Skip(typeof(Silksong::HeroController), "SetupDeliveryItems"); // delivery-quest setup entry — quests irrelevant
        // The HUD's DeliveryHudIcon.OnPreUpdateDisplay calls DeliveryQuestItem.GetActiveItems() -> NullRef (delivery-quest
        // system deliberately off, see above). Skip it: currentItem stays null -> RadialHudIcon.GetIsActive() is false ->
        // the icon hides itself. (No deliveries to show anyway.)
        Skip(typeof(Silksong::DeliveryHudIcon), "OnPreUpdateDisplay");
        // StartDashEffect activates dashBurstPrefab/airDashEffect (unresolved external effect prefabs) -> NullRef,
        // aborting HeroDash after cState.dashing is set but before cooldown/FSM. Purely cosmetic -> stub so dash runs.
        Skip(typeof(Silksong::HeroController), "StartDashEffect");
        // SetParticleScale.OnUpdate (ticked every frame via SetParticleScaleCallbackHooks) derefs a null parentBody
        // (Rigidbody2D.IsAwake) -> per-frame NullRef. Cosmetic particle scaling -> stub.
        Skip(typeof(Silksong::SetParticleScale), "OnUpdate", true); // per-frame
        Skip(typeof(Silksong::DeliveryQuestItem),
            "BreakAllInternal"); // also called directly from Start (BreakTimedNoEffects)
        // Superjump's "Hit Roof Hard" state calls DeliveryQuestItem.TakeHit() via CallStaticMethod (slamming the ceiling
        // damages carried delivery items). TakeHit() -> TakeHit(int) -> GetActiveItems() -> QuestManager/CollectableItemManager
        // (deliveries/quest subsystem off, see DeliveryHudIcon above) -> NullRef. It throws at action index 0, aborting the
        // rest of "Hit Roof Hard" (incl. the Tk2dPlayAnimationWithEvents) so the follow-up "Hit Roof" state's
        // Tk2dWatchAnimationEvents waits forever for an animation event that never fires -> Hornet stuck in soar pose at the
        // ceiling, no fall. Both overloads are void -> clean no-op (Skip stubs all overloads). GetActiveItems itself can't be
        // stubbed here (returns IEnumerable -> default null -> callers foreach over null -> NullRef again).
        Skip(typeof(Silksong::DeliveryQuestItem), "TakeHit");
        // Hornet's EnterScene (HornetSceneEntry) -> EnterHeroSubFadeUp calls gm.FadeSceneIn(): Silksong's own scene fade,
        // which fights HK's fade (HK owns the camera/fade) and needs Silksong's fade FSM/camera context we don't run.
        // No-op it; HK fades the scene.
        Skip(typeof(Silksong::GameManager), "FadeSceneIn");
        // AwardAchievement -> achievementHandler.AwardAchievementToPlayer(key); achievementHandler is null on our
        // bootstrap GM (Silksong achievements are irrelevant in HK).
        Skip(typeof(Silksong::GameManager), "AwardAchievement", true);
        Skip(typeof(Silksong::GameManager), "UpdateAchievementProgress", true);
        // Debug: catch SetEquippedTools NullRef to find the exact inner cause
        var setEq = typeof(Silksong::ToolItemManager).GetMethod("SetEquippedTools",
            BindingFlags.Static | BindingFlags.Public);
        if (setEq != null)
            detours.Add(new Hook(setEq,
                (Action<Action<string, List<string>>, string, List<string>>)
                ((orig, crestId, tools) => {
                    try {
                        orig(crestId, tools);
                    } catch (Exception e) {
                        Log.Error($"[SetEquippedTools] crestId={crestId} -> {e}");
                        throw;
                    }
                })));
        // HeroController.Start runs an initial AddSilk(CurrentSilkMax) which nullref'd in SpawnNewChunk (null animator?)
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
                        Log.InfoOnce("addsilk-guard",
                            $"[AddSilk] caught addsilk NullRef {e.Message}");
                    }
                })));
        else
            Log.Error("[Stub] HeroController.AddSilk(int,bool,SilkAddSource,bool) not found");

        // SimpleFadeOut::SetColor throws nullref, because Awake is never ran
        Skip(typeof(Silksong::CameraController), "ScreenFlash");
        // GameCameras.Start does only `gs.LoadOverscanSettings(); SetOverscan(gs.overScanAdjustment)` — gs is
        // gm.gameSettings, assigned only in SetupGameRefs (which we never run) -> null -> NullRef on activating the rig.
        // Overscan is cosmetic and HK owns the camera; skip Start so the rig can come up ACTIVE (HUD FSMs self-init).
        Skip(typeof(Silksong::GameCameras), "Start");
        // HUDCamera.OnEnable does `ih = GameManager.instance.inputHandler; ih.PauseAllowed` -> NullRef (our bootstrap GM's
        // inputHandler field isn't wired) + Invoke("MoveMenuToHudCamera") (menu plumbing we don't need). Skip — none of
        // it matters for the in-game HUD (health/silk render regardless).
        Skip(typeof(Silksong::HUDCamera), "OnEnable");
        // GameCameras.Awake is just `_instance = this; DontDestroyOnLoad(this)` — but our rig lives under an inactive
        // holder (so we can neuter before activating), so `this` is non-root -> "DontDestroyOnLoad only works for root
        // GameObjects" warning. Skip Awake; GameCamerasBootstrap sets _instance itself (before activation, so child HUD
        // FSMs resolve GameCameras.instance) and DDOLs the holder. Without skipping, Awake would also Destroy our rig if
        // _instance was pre-set to a different object — skipping removes that hazard too.
        Skip(typeof(Silksong::GameCameras), "Awake");
        // Inventory map-zone items: InventoryItemWideMapZone.IsUnlocked derefs GameManager.instance.gameMap throughout
        // (IsLostInAbyssPreMap/PostMap + HasAnyMapForZone) -> NullRef (gameMap.gm null; map subsystem out of scope).
        // These are MAP unlock indicators in the inventory; with no map they're correctly "locked" -> get_IsUnlocked
        // returns false (default), which also makes IsAutoNavSelectable false so OnEnable's EvaluateAutoNav skips them.
        Skip(typeof(Silksong::InventoryItemWideMapZone), "get_IsUnlocked");
        // Inventory QUEST pane: InventoryPaneList.Start re-activates every pane (incl. the out-of-scope quest pane), and
        // InventoryItemQuestManager.GetItems does `<quests>.Where(...)` on a null source (quest subsystem not brought up)
        // -> ArgumentNullException, thrown from QuestItemManager.Awake -> base InventoryItemListManager.Awake ->
        // UpdateList -> GetItems. Skip the quest manager's Awake so base.Awake (UpdateList) never runs -> null source
        // never touched. No quests -> empty quest pane (out of scope; sibling to the map-zone skip above).
        Skip(typeof(Silksong::QuestItemManager), "Awake");
        // QuestManager.MaybeShowQuestUpdated: called by ToolItem.Unlock when a tool is newly unlocked with an item-get
        // popup. Without a QuestManager singleton, GetActiveQuests returns Empty — but the method still NullRefs at
        // [0x0003d] (likely a static cache stale from a previous hot-reload). The quest subsystem isn't brought up, so
        // quest-update UI is moot. Silent (per-tool, could be many).
        Skip(typeof(Silksong::QuestManager), "MaybeShowQuestUpdated", true);
        // QuestManager.GetActiveQuests / GetAcceptedQuests: ObjectCache<T>.cache defaults to null, and
        // ShouldUpdate(Version) returns false when both sides are 0 (never incremented without a QuestManager).
        // So GetActiveQuests returns null -> foreach over null -> NullRef in InventoryItemCollectable.Description
        // (which iterates GetActiveQuests to append quest-desc text to item tooltips). Return Enumerable.Empty
        // instead — quest subsystem not brought up, so no active quests is correct.
        var qm = typeof(Silksong::QuestManager);
        var getActive = qm.GetMethod("GetActiveQuests", BindingFlags.Public | BindingFlags.Static);
        var getAccepted = qm.GetMethod("GetAcceptedQuests", BindingFlags.Public | BindingFlags.Static);
        if (getActive != null)
            hooks.Add(new ILHook(getActive, il => {
                il.Body.Instructions.Clear();
                var c = new ILCursor(il);
                c.Emit(OpCodes.Call, typeof(Enumerable).GetMethod(nameof(Enumerable.Empty))!
                    .MakeGenericMethod(typeof(Silksong::FullQuestBase)));
                c.Emit(OpCodes.Ret);
            }));
        if (getAccepted != null)
            hooks.Add(new ILHook(getAccepted, il => {
                il.Body.Instructions.Clear();
                var c = new ILCursor(il);
                c.Emit(OpCodes.Call, typeof(Enumerable).GetMethod(nameof(Enumerable.Empty))!
                    .MakeGenericMethod(typeof(Silksong::BasicQuestBase)));
                c.Emit(OpCodes.Ret);
            }));
        // Inventory MAP pane: InventoryMapManager.OnPaneStart -> InventoryWideMap.UpdatePositions -> get_PositionOffset
        // -> GameMap.IsLostInAbyssPostMap -> IsLostInAbyssBase NullRef (GameMap uninitialized; map subsystem out of
        // scope). OnPaneStart is called directly from Awake AND subscribed to pane.OnPaneStart; skipping it covers both.
        // The map pane stays inert (nothing to position).
        Skip(typeof(Silksong::InventoryMapManager), "OnPaneStart");
        // Inventory open is decoupled from Silksong's global pause (chosen "overlay" approach): GameManager.SetPausedState
        // does SetTimeScale(0) — which would freeze HK's world AND our InputDriver (it bails at timeScale<=0, so the
        // inventory couldn't be navigated) — plus gameCams.StopCameraShake()/ui.AudioGoToPauseMenu() (HK owns time/pause/
        // audio). No-op it: the inventory still opens (SetIsInventoryOpen sets isInventoryOpen + AddInputBlocker around
        // this), just as an overlay over the live world instead of pausing. Vanilla pause semantics can be added later.
        Skip(typeof(Silksong::GameManager), "SetPausedState");
        // ApplyMusicCue.OnEnter (death-music FSM, fired by Die's EventRegister HeroDeath broadcast): does
        // `instance.AudioManager.ApplyMusicCue(...)` guarded ONLY on musicCue/instance null, NOT instance.AudioManager —
        // which is null on our skeleton bootstrap GM -> NullRef. The deref is BEFORE the trailing Finish(), so the action
        // already can't Finish: it hangs its music FSM today regardless. This is the documented "OK to stub" case (the
        // general rule against stubbing PlayMaker actions is because they skip Finish() and hang — here the action hangs
        // on its own, and a no-op at least drops the per-death NullRef). Silksong music is shadow-world here anyway (HK
        // owns audio; AddSilk + the audio tables are already stubbed), so applying its cues is moot.
        Skip(typeof(Silksong::HutongGames.PlayMaker.Actions.ApplyMusicCue), "OnEnter");
        // ScreenFader.OnEnter -> ScreenFaderUtils.Fade derefs gm.screenFader_fsm (null on the bootstrap GM — that fader
        // FSM lives on the un-run Silksong camera rig) -> NullRef, again before the trailing Finish() (so it hangs). HK
        // owns the camera + fades (our death uses HK's FadeOut(HERO_DEATH); GameManager.FadeSceneIn is already stubbed for
        // the same reason), and gm.screenFader_fsm is ALWAYS null here so this action never works regardless -> stubbing
        // can't regress a working fade. Same documented "already can't Finish" exception as ApplyMusicCue above.
        Skip(typeof(Silksong::HutongGames.PlayMaker.Actions.ScreenFader), "OnEnter");
        // NOTE: SetConfigGroup's throw is FSMUtility.SendEventToGameObject -> list[i].Fsm.Event() on the hero's
        // PlayMakerFSMs, which aren't fully initialized (linked to the residual ~125 action-resolution failures).
        // NOT stubbed here (FSMUtility is broad / FSM is core) — tracked as the PlayMaker bring-up TODO.

        // Debug instrumentation: wrap Fsm.InitData in try/catch to log which FSM/GO NullRefs during initialization.
        // InitData iterates states/events/transitions — a null array (bad deserialization) crashes with no FSM context.
        // Hook both Silksong's and HK's PlayMaker (same namespace, different assemblies).
        InstrumentInitData(typeof(SilksongPM::HutongGames.PlayMaker.Fsm));
        InstrumentInitData(typeof(Fsm));
    }

    private static void InstrumentInitData(Type fsmType) {
        var mi = fsmType.GetMethod("InitData", BindingFlags.Public | BindingFlags.Instance);
        if (mi == null) {
            Log.Error($"[Stub] Fsm.InitData not found on {fsmType.Assembly.GetName().Name}");
            return;
        }

        var nameProp = fsmType.GetProperty("Name");
        var goNameProp = fsmType.GetProperty("GameObjectName");
        detours.Add(new Hook(mi, (Action<Action<object>, object>)((orig, self) => {
            try {
                orig(self);
            } catch (Exception e) {
                var fsmName = "?";
                var goName = "?";
                try {
                    fsmName = (string?)nameProp?.GetValue(self) ?? "?";
                } catch {
                }

                try {
                    goName = (string?)goNameProp?.GetValue(self) ?? "?";
                } catch {
                }

                Log.Error(
                    $"[Fsm.InitData] NullRef in FSM='{fsmName}' GO='{goName}' ({fsmType.Assembly.GetName().Name}): {e}");
            }
        })));
        Log.Debug($"[Stub] instrumented Fsm.InitData on {fsmType.Assembly.GetName().Name}");
    }

    // Stub `method` on every Silksong type in `ns` whose name starts with `prefix` (category stub).
    internal static void SkipAllInNamespace(string ns, string prefix, string method) {
        Type?[] types;
        try {
            types = typeof(Silksong::HeroController).Assembly.GetTypes();
        } catch (ReflectionTypeLoadException e) {
            types = e.Types;
        }

        var n = 0;
        foreach (var t in types) {
            if (t?.Namespace != ns || !t.Name.StartsWith(prefix)) continue;
            if (t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ==
                null) continue;
            Skip(t, method);
            n++;
        }

        Log.Debug($"[Stub] category {ns}.{prefix}*::{method} -> {n} types");
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

    // Called from stubbed methods (emitted by Rewrite). Two variants:
    //   Logged — logs each distinct stub once, then silent (InfoOnce dedup)
    //   Silent — never logs
    public static void Logged(string label) {
        Log.DebugOnce($"stub|{label}", $"[Stub] >> {label} (stubbed, no-op)");
    }

    public static void Silent(string label) {
    }

    private static void Rewrite(ILContext il, string label, bool silent = false) {
        il.Body.Instructions.Clear();
        il.Body.ExceptionHandlers.Clear();
        il.Body.Variables.Clear();
        var c = new ILCursor(il);
        c.Emit(OpCodes.Ldstr, label);
        c.Emit(OpCodes.Call, typeof(Stub).GetMethod(silent ? nameof(Silent) : nameof(Logged))!);
        EmitDefaultReturn(c, il);
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
