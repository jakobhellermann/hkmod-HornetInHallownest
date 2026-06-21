extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine;

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
    private float armedWindow; // seconds left in the post-transition "watch for stuck control" window
    private string? lastScene;
    private float stuckControlTimer;
    private bool stuckEntryLogged;

    // Log-only watchdog for the bottom-gate (vertical / "up") entry hang. EnterScene's bottom branch ends at
    // transitionState=DROPPING_DOWN and relies on OnCollisionEnter2D's no_input branch to call FinishedEnteringScene on
    // landing. If hero_state has left no_input by the time she lands she takes the NORMAL landing path instead, so the
    // entry never completes: cState.transitioning stays true (Update movement returns early -> frozen) and acceptingInput
    // stays false, with NO error logged. Surface it so the next occurrence is caught in the log. (Observed 2026-06-21;
    // root cause = hero_state leaving no_input mid-drop, NOT yet fixed -> restart clears it.) Log-only on purpose: a
    // forced FinishedEnteringScene here would be the kind of patch-over-root-cause we're avoiding.
    private float stuckEntryTimer;

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

                StuckControlNet(hero);
                StuckEntryWatch(hero);
            }

            // TESTING: infinite silk so silk-cost abilities always fire.
            var pd = Silksong::PlayerData.instance;
            if (pd != null) pd.silk = pd.silkMax;

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
    }

    internal static void Cleanup() {
        if (go != null) {
            Destroy(go);
            go = null;
        }
    }

    // Safety net for stuck controlReqlinquished. Her scene-entry (EnterScene) clears the flag via its closing
    // RegainControl; on HK transitions where EnterScene doesn't run/complete (no sceneEntryGate, faded coroutine) the
    // flag sticks true and silently gates double-jump/attack/sprint (all check !controlReqlinquished) — abilities die
    // with NO error log. We can't just watch "controlReqlinquished true for a while": SPRINT and DASH set it true BY
    // DESIGN (the Sprint FSM runs with controlReqlinquished + hero_state=no_input), so a continuous watch clobbers a
    // long dash/sprint. So scope it tightly:
    //   - only within a short window AFTER a scene change (the only time the stick happens), and
    //   - never while dashing/sprinting (legit controlReqlinquished), and
    //   - only when settled (transitionState idle, not mid walk-in), with a debounce.
    private void StuckControlNet(Silksong::HeroController hero) {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != lastScene) {
            lastScene = scene;
            armedWindow = 5f;
            stuckControlTimer = 0f;
        }

        if (armedWindow <= 0f) return;
        armedWindow -= Time.deltaTime;

        // Don't disarm on the first !controlReqlinquished frame: at scene-change detection the flag may not be set yet
        // (EnterScene sets it a few frames later). Just run the window; the exclusions below keep it from false-firing.
        var cs = hero.cState;
        var legit = cs.dashing || cs.isSprinting || cs.shadowDashing ||
                    cs.superDashing; // these use controlReqlinquished by design
        var stuck = hero.controlReqlinquished && !cs.dead && !legit
                    && hero.transitionState == Silksong::GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION;
        if (!stuck) {
            stuckControlTimer = 0f;
            return;
        }

        stuckControlTimer += Time.deltaTime;
        if (stuckControlTimer > 0.5f) {
            hero.RegainControl();
            stuckControlTimer = 0f;
            armedWindow = 0f;
            Log.Info("[EnvAdapter] cleared stuck controlReqlinquished (RegainControl after settled transition)");
        }
    }

    private void StuckEntryWatch(Silksong::HeroController hero) {
        var cs = hero.cState;
        var stuck = cs.onGround && cs.transitioning && !hero.acceptingInput
                    && hero.transitionState == Silksong::GlobalEnums.HeroTransitionState.DROPPING_DOWN;
        if (!stuck) {
            stuckEntryTimer = 0f;
            stuckEntryLogged = false;
            return;
        }

        stuckEntryTimer += Time.deltaTime;
        if (stuckEntryTimer > 0.75f && !stuckEntryLogged) {
            stuckEntryLogged = true;
            Log.Error(
                $"[EnvAdapter] STUCK ENTRY: grounded+transitioning+!acceptingInput, transitionState=DROPPING_DOWN, "
                + $"hero_state={hero.hero_state} (needs no_input for OnCollisionEnter2D to finish the entry), "
                + $"gate={hero.gatePosition}, pos={(Vector2)hero.transform.position}. "
                + "FinishedEnteringScene never ran -> frozen; restart to clear. (bottom-gate entry hang)");
        }
    }

    // NOTE: the manual FixedUpdateCycle bump that used to live here is gone — CustomPlayerLoopBootstrap now installs
    // Silksong's REAL LateFixedUpdate phase into Unity's PlayerLoop, which advances FixedUpdateCycle itself AND ticks
    // every registered ILateFixedUpdate (DamageEnemies' EvaluateDamage/ProcessDamageBuffer, the cycle-gated FSM
    // actions, etc.). Bumping the counter alone left those handlers un-ticked (ground slashes detected enemies but
    // dealt no damage).
}
