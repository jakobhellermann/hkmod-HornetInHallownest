extern alias Silksong;
using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SGate = Silksong::GlobalEnums.GatePosition;

namespace HornetPlayer.Playground;

// THE seam between Silksong's Hornet and HK's environment.
//
// We run Silksong's HERO code on HK's environment: HK keeps its cameras, scenes and input routing, so we deliberately
// do NOT run Silksong's GameManager/InputHandler/CustomPlayerLoop. Their GameObject is kept INACTIVE because
// GameManager.Awake -> SetupGameRefs would import Silksong's whole, conflicting environment (its camera rig rendering
// over HK, an InControl device-poller fighting our InputDriver + dual-control, and a scene handler reacting to HK's
// scene loads). That's a parallel "shadow world" we explicitly don't want.
//
// The price is that the per-frame bookkeeping those Awake/Update methods would do never happens. Rather than scatter
// the fixes, this one component runs exactly the small set the hero code reads each frame. Add new per-frame needs
// HERE (and one-time setup in SilksongBootstrap) — keep InputDriver to input only.
[DefaultExecutionOrder(-9000)] // after InputDriver (-10000) commits input, before HeroController.Update (0)
internal sealed class HornetEnvironmentAdapter : MonoBehaviour {
    private static GameObject? go;
    private static FieldInfo? isGameplaySceneField;
    private static FieldInfo? gameStateField;
    private static MethodInfo? updateButtonQueueingMethod;
    private static MethodInfo? sendRefreshEventMethod;

    private static Hook? beginSceneTransitionHook;
    private static Hook? setHazardRespawnHook1; // SetHazardRespawn(Vector3, bool)
    private static Hook? setHazardRespawnHook2; // SetHazardRespawn(HazardRespawnMarker)

    private static Hook?
        finishedEnteringSceneHook; // finish cutscene (enterWithoutInput) entries HK's Dream-Return FSM would

    private static Hook? enterSceneDreamGateHook; // mirror HK's dream-gate warp-in onto Hornet

    private void Update() {
        try {
            var paused = Time.timeScale <= 0.0001f;

            // Mirror HK's pause onto Silksong's GameManager so the hero pipeline (LookForInput gates on GameState)
            // freezes with HK instead of running through the pause.
            // Read the static field directly (O(1)), NOT the `instance` getter (LogErrors every frame while null) and
            // NOT SilentInstance (does FindObjectOfType every call while null — expensive, and never finds our inactive
            // gm anyway). SilksongBootstrap.Ensure sets _instance when it creates the gm; before that it's null (skip).
            var gm = Silksong::GameManager._instance;
            if (gm != null) {
                gameStateField ??= typeof(Silksong::GameManager)
                    .GetField("<GameState>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                gameStateField?.SetValue(gm,
                    paused ? Silksong::GlobalEnums.GameState.PAUSED : Silksong::GlobalEnums.GameState.PLAYING);
            }

            if (paused) return;

            // HeroController: Start's non-gameplay path left the component disabled + isGameplayScene=false (our gm
            // isn't a "gameplay scene"), so Unity never ticks Update / LookForInput early-returns. Re-assert both.
            // Only drive Hornet while SHE is the active character; when Knight is active she stays inert (HeroSwitch
            // disabled her HeroController + Rigidbody) so we must NOT force-enable her here.
            var hero = BundleSpike.RealHero;
            if (hero != null && HeroSwitch.HornetActive) {
                if (!hero.enabled) hero.enabled = true;
                isGameplaySceneField ??= typeof(Silksong::HeroController)
                    .GetField("isGameplayScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                isGameplaySceneField?.SetValue(hero, true);

                QuakeFloorBridge.Tick(hero); // down-dash breaks HK quake floors (only iterates while quaking)
                NeedolinDreamNail
                    .Tick(hero); // Needolin acts as a Dream Nail on nearby HK dream objects (edge-triggered)
                GeoDashBridge
                    .Tick(hero); // collect geo during a dash (kinematic HeroBox tunnels past it; only runs while dashing)
            }

            // --- The bookkeeping half of InputHandler.Update ---
            // InputHandler.Update never ticks (its GO is inactive). Its body splits cleanly in two: per-frame
            // bookkeeping the hero pipeline needs, and environment coupling we deliberately reject (SetCursorVisible
            // touches the OS cursor HK owns; UpdateActiveController -> SetupGamepadUIInputActions rebinds gamepad UI;
            // inputActions.Pause.WasPressed -> gm.PauseGameToggle runs *Silksong's* pause). So we don't run Update();
            // we run its bookkeeping methods by reflection — after InputDriver (-10000) commits WasPressed, before
            // HeroController/FSMs read it (this component is -9000). Add further Update-maintained state HERE.
            var ih = SilksongBootstrap.Handler;
            if (ih != null) {
                // UpdateButtonQueueing(): maintains buttonQueueTimers[] — the ~0.1s queued-press window read by
                // GetWasButtonPressedQueued. With it frozen, every GetWasButtonPressedQueued returns false, so any
                // FSM/HeroController path that consumes queued input silently stalls (e.g. Sprint FSM Ground Sprint
                // R/L listens for JUMP this way -> jump/attack-out-of-sprint never fired; no error, because the normal
                // CanJump/CanAttack path is intentionally off during sprint with hero_state=no_input).
                updateButtonQueueingMethod ??= typeof(Silksong::InputHandler)
                    .GetMethod("UpdateButtonQueueing", BindingFlags.Instance | BindingFlags.NonPublic);
                updateButtonQueueingMethod?.Invoke(ih, null);

                // PlayingInput()'s sole effect: clear ForceDreamNailRePress once DreamNail is released (RegainControl
                // sets it; only Update clears it, so ListenForDreamNail would skip forever). Inlined (not the real
                // method) to avoid its CheatManager.IsOpen static read, which needn't be initialized here.
                if (ih.inputActions != null && !ih.inputActions.DreamNail.IsPressed)
                    ih.ForceDreamNailRePress = false;

                // Mark keyboard as the active controller. InputHandler.Update (which we don't run) maintains this from
                // InControl's device detection (UpdateActiveController, from inputActions.LastInputType); we bypass
                // InControl, so it stays None -> Platform.WasLastInputKeyboard is false. That silently breaks menus that
                // special-case keyboard: e.g. Platform.GetMenuAction only maps DreamNail(D)->MenuActions.Super (the
                // inventory's change-crest shortcut) on the keyboard branch, so D never changed the crest. We ARE keyboard.
                // Edge-only: set it once when it isn't already keyboard, then fire RefreshActiveControllerEvent (via the
                // private SendRefreshEvent) so the glyph UIs (UIButtonSkins/ActionButtonIconBase, which cache their icon
                // and only recompute on that event) switch to keyboard prompts. Skipping the refresh leaves stale glyphs
                // computed while it was still None. Firing once (not per frame) avoids recomputing every subscriber's icon.
                if (ih.lastActiveController != Silksong::InControl.BindingSourceType.KeyBindingSource) {
                    ih.lastActiveController = Silksong::InControl.BindingSourceType.KeyBindingSource;
                    sendRefreshEventMethod ??= typeof(Silksong::InputHandler)
                        .GetMethod("SendRefreshEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                    sendRefreshEventMethod?.Invoke(ih, null);
                }
            }
        } catch (Exception e) {
            Log.Error($"[EnvAdapter] {e}");
        }
    }

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.EnvironmentAdapter");
        go.AddComponent<HornetEnvironmentAdapter>();
        DontDestroyOnLoad(go);
        // Stand in for Silksong's NextSceneWillActivate -> recycle pooled tools/audio (see RecycleSilksongPooledObjects).
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
        // Hook HK's GameManager.BeginSceneTransition: it fires at the start of a scene transition, before the scene
        // unloads. Silksong's own GameManager never fires its UnloadingLevel event (GM GO inactive), and
        // HeroController.OnLevelUnload never subscribes to it (subscription is in SetupGameRefs, which we skip).
        // So Hornet stays in the scene and gets destroyed by SceneManager.UnloadScene in the transition coroutine.
        // In this hook (after orig, which kicks off the coroutine), we directly deparent her — what OnLevelUnload
        // would do, but checking scene instead of parent (SetHeroParent(null) skips DontDestroyOnLoad when
        // transform.parent is already null, even if the GO is still in a scene).
        // Hook HK's PlayerData.SetHazardRespawn (both overloads): HK calls these from FinishedEnteringScene
        // (HeroController) AND from HazardRespawnTrigger.OnTriggerEnter2D (which fires on ANY Layer-9 collider,
        // including Hornet). Mirror onto Silksong's PlayerData so Silksong's HazardRespawn coroutine respawns at
        // the right spot instead of the scene start.
        var shr1 = typeof(PlayerData).GetMethod("SetHazardRespawn",
            BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(Vector3), typeof(bool)], null);
        if (shr1 != null)
            setHazardRespawnHook1 = new Hook(shr1,
                (Action<Action<PlayerData, Vector3, bool>, PlayerData, Vector3, bool>)
                ((orig, self, pos, facingRight) => {
                    orig(self, pos, facingRight);
                    var ssPd = Silksong::PlayerData.instance;
                    if (ssPd != null) ssPd.SetHazardRespawn(pos, facingRight);
                }));
        var shr2 = typeof(PlayerData).GetMethod("SetHazardRespawn",
            BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(HazardRespawnMarker)], null);
        if (shr2 != null)
            setHazardRespawnHook2 = new Hook(shr2,
                (Action<Action<PlayerData, HazardRespawnMarker>, PlayerData, HazardRespawnMarker>)
                ((orig, self, marker) => {
                    orig(self, marker);
                    var ssPd = Silksong::PlayerData.instance;
                    if (ssPd != null && marker != null)
                        ssPd.SetHazardRespawn(marker.transform.position, marker.respawnFacingRight);
                }));
        Log.Debug(
            $"[EnvAdapter] SetHazardRespawn mirror installed ({setHazardRespawnHook1 != null}, {setHazardRespawnHook2 != null})");

        // Finish HK's "enter without input" cutscene entries (dreamer-free dream return, etc.). Those rely on an HK
        // hero FSM (e.g. "Dream Return") to close the arrival with RegainControl/StartAnimationControl/AcceptInput.
        // Hornet has no such FSM, so FinishedEnteringScene consumes enterWithoutInput WITHOUT calling AcceptInput
        // (HeroController:9292) -> she arrives frozen (acceptingInput=false, the only stuck gate — confirmed live).
        // Re-implement just HK's "Regain Control" step, right where the skip happens. NOT a clone of the FSM (its
        // Prostrate/blue-health/dreamOrbs bits don't translate to Hornet) — only the necessary close.
        var fes = typeof(Silksong::HeroController).GetMethod("FinishedEnteringScene",
            BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool), typeof(bool)], null);
        if (fes != null)
            finishedEnteringSceneHook = new Hook(fes,
                (Action<Action<Silksong::HeroController, bool, bool>, Silksong::HeroController, bool, bool>)
                OnFinishedEnteringScene);
        else
            Log.Error("[EnvAdapter] HeroController.FinishedEnteringScene(bool,bool) not found");

        // Mirror HK's dream-gate warp-in onto Hornet. GameManager drives dream entries with a DIRECT
        // `hero_ctrl.EnterSceneDreamGate()` on its Knight (GameManager.cs:1809, entryGateName=="dreamGate") — a typed
        // field call no shim can redirect (unlike the global "Hero" var / FindWithTag / GetComponent indirection).
        // So Hornet's own EnterSceneDreamGate never runs and she stays stuck in WAITING_TO_ENTER_LEVEL (no_input, the
        // white dream fade never clears -> whitescreen). Hook HK's method and flag it; HeroSwitch's entry relay then
        // positions her (snap to Knight at isHeroInPosition) and runs HER EnterSceneDreamGate (same pattern as the
        // SetHazardRespawn mirror below — both bridge direct gm-driven Knight calls the shims can't touch).
        var esdg = typeof(HeroController).GetMethod("EnterSceneDreamGate", BindingFlags.Instance | BindingFlags.Public);
        if (esdg != null)
            enterSceneDreamGateHook = new Hook(esdg,
                (Action<Action<HeroController>, HeroController>)((orig, self) => {
                    orig(self);
                    if (HeroSwitch.HornetActive) HeroSwitch.DreamGateEntryPending = true;
                }));
        else
            Log.Error("[EnvAdapter] HeroController.EnterSceneDreamGate not found");

        var mi = typeof(GameManager).GetMethod("BeginSceneTransition", [typeof(GameManager.SceneLoadInfo)]);
        if (mi != null) {
            beginSceneTransitionHook = new Hook(mi, BeginSceneTransitionHook);
            Log.Debug("[EnvAdapter] installed: GameManager.BeginSceneTransition deparent hook");
        }
        else {
            Log.Error("[EnvAdapter] GameManager.BeginSceneTransition not found");
        }
    }

    private static void BeginSceneTransitionHook(Action<GameManager, GameManager.SceneLoadInfo> orig, GameManager self,
        GameManager.SceneLoadInfo info) {
        orig(self, info);
        RelayLeaveScene(info);
        DreamReturnBridge
            .OnBeginSceneTransition(info); // fade the white blanker out on a Dream arrival (Hornet lacks the FSM)
        DeparentHero("scene transition");
    }

    // Silksong's GameManager.SetupGameRefs (GameManager.cs:3456) subscribes AutoRecycleSelf.RecycleActiveRecyclers +
    // PlayAudioAndRecycle.RecycleActiveRecyclers to its NextSceneWillActivate event — that's what despawns thrown tools
    // and one-shot audio (pooled via PersonalObjectPool + AutoRecycleSelf) on every scene change. We skip SetupGameRefs
    // (GM GO inactive) AND Silksong's GM never runs the scene-load flow that raises that event, so those pooled objects
    // linger across HK transitions: a tool thrown before a transition kept ticking ToolBreakRangeHandler.Update against a
    // now-destroyed camera transform -> a per-frame NullReferenceException until AutoRecycleSelf's own timer reclaimed it.
    // Mirror Silksong's single NextSceneWillActivate firing: hook Unity's activeSceneChanged (one fire per real room
    // change — additive boundary loads don't switch the active scene) and recycle there. Static, Silksong-scoped — no-op
    // when none are active (Knight active / Hornet despawned).
    private static void OnActiveSceneChanged(Scene from, Scene to) {
        RecycleSilksongPooledObjects();
    }

    private static void RecycleSilksongPooledObjects() {
        try {
            Silksong::AutoRecycleSelf.RecycleActiveRecyclers();
            Silksong::PlayAudioAndRecycle.RecycleActiveRecyclers();
            Silksong::ResetDynamicHierarchy.ForceReconnectAll(); // 3rd NextSceneWillActivate subscriber (same intent)
        } catch (Exception e) {
            Log.Error($"[EnvAdapter] RecycleActiveRecyclers: {e.Message}");
        }
    }

    // Re-implements HK's "Dream Return" FSM's Regain Control step for Hornet (she has no such FSM). enterWithoutInput
    // entries deliberately skip AcceptInput (HeroController:9292), expecting that FSM to close the arrival; without it
    // she's frozen. Runs after orig, at the exact skip point — no race, no polling. Excludes dash/sprint/quake
    // continuations: those also skip AcceptInput but resume via their own completion, not a fresh input grant.
    private static void OnFinishedEnteringScene(Action<Silksong::HeroController, bool, bool> orig,
        Silksong::HeroController self, bool setHazardMarker, bool preventRunBob) {
        var wasEnterWithoutInput = self.enterWithoutInput;
        var isMoveResume = self.exitedSuperDashing || self.exitedQuake || self.exitedSprinting;
        orig(self, setHazardMarker, preventRunBob);
        if (!wasEnterWithoutInput || isMoveResume || !HeroSwitch.HornetActive) return;
        self.RegainControl();
        self.StartAnimationControl();
        self.AcceptInput();
        Log.Debug("[EnvAdapter] closed enterWithoutInput entry (RegainControl+AcceptInput; Hornet has no arrival FSM)");
    }

    // HK drives the transition on its Knight: GameManager calls hk.LeaveScene(gate) THEN hk.LeavingScene(). Hornet's own
    // HeroController never gets either (HK doesn't know about her), so two things break:
    //   - LeaveScene(gate): puts the hero in no_input + NO_DAMAGE + EXITING_SCENE and replaces gravity/locomotion with a
    //     scripted transition_vel walk-out. Without it Hornet stays in WAITING_TO_TRANSITION with gravity on, full damage
    //     mode, and the InputDriver still feeding held input — she runs/falls out the gate and (e.g. through a bottom
    //     gate) lands in a hazard and TAKES damage during the fade.
    //   - LeavingScene() -> RecordLeaveSceneCState(): records exitedSprinting/exitedSuperDashing/exitedQuake from the
    //     current cState + sprint FSM. EnterScene reads exitedSprinting to resume the sprint on the far side
    //     (`if (exitedSprinting) sprintFSM.SendEventSafe("ENTER SPRINTING")`). Without it sprint (and the harpoon/quake
    //     carry-throughs) don't continue across the transition — Silksong does. We call only RecordLeaveSceneCState (the
    //     hero-state bookkeeping piece); the rest of LeavingScene() is audio/camera/FSM plumbing HK owns or that NullRefs
    //     on our env. Order matches Silksong's GameManager (LeaveScene then the record).
    // Relay onto her real Silksong methods with the same gate (GatePosition is the identical enum -> cast by value). Only
    // for a directional gate exit (HasValue); null-gate loads (death/menu) have their own paths. EnterScene side is
    // handled by HornetSceneEntry.
    private static void RelayLeaveScene(GameManager.SceneLoadInfo info) {
        if (!HeroSwitch.HornetActive || !info.HeroLeaveDirection.HasValue) return;
        var hero = BundleSpike.RealHero;
        if (hero == null) return;
        var gate = (SGate)(int)info.HeroLeaveDirection.Value;
        hero.LeaveScene(gate);
        hero.RecordLeaveSceneCState();
        Log.Debug($"[EnvAdapter] relayed LeaveScene(gate={gate}) + RecordLeaveSceneCState to Hornet " +
                  $"(no_input + NO_DAMAGE + gravity off; exitedSprinting={hero.exitedSprinting})");
    }

    // Mirrors Silksong's HeroController.OnLevelUnload → SetHeroParent(null) → DontDestroyOnLoad, but checks scene
    // instead of parent: SetHeroParent(null) skips DontDestroyOnLoad when transform.parent is already null, even if
    // the GO is still in a scene (e.g. platform destroyed, hero unparented but not in DDOL).
    private static void DeparentHero(string reason) {
        var hero = BundleSpike.RealHero;
        if (hero != null && hero.gameObject.scene.name != "DontDestroyOnLoad") {
            Log.Debug($"[EnvAdapter] deparenting hero before {reason} (was in scene={hero.gameObject.scene.name})");
            hero.SetHeroParent(null);
            if (hero.gameObject.scene.name != "DontDestroyOnLoad")
                DontDestroyOnLoad(hero.gameObject);
        }
    }

    internal static void Cleanup() {
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        beginSceneTransitionHook?.Dispose();
        beginSceneTransitionHook = null;
        setHazardRespawnHook1?.Dispose();
        setHazardRespawnHook1 = null;
        setHazardRespawnHook2?.Dispose();
        setHazardRespawnHook2 = null;
        finishedEnteringSceneHook?.Dispose();
        finishedEnteringSceneHook = null;
        enterSceneDreamGateHook?.Dispose();
        enterSceneDreamGateHook = null;
        if (go != null) {
            Destroy(go);
            go = null;
        }
    }

    // NOTE: the manual FixedUpdateCycle bump that used to live here is gone — CustomPlayerLoopBootstrap now installs
    // Silksong's REAL LateFixedUpdate phase into Unity's PlayerLoop, which advances FixedUpdateCycle itself AND ticks
    // every registered ILateFixedUpdate (DamageEnemies' EvaluateDamage/ProcessDamageBuffer, the cycle-gated FSM
    // actions, etc.). Bumping the counter alone left those handlers un-ticked (ground slashes detected enemies but
    // dealt no damage).
}
