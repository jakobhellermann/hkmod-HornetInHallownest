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
// the fixes, this one component runs exactly the small set the hero code reads each frame, and pumps the module list
// (InputModule first — it commits input from HK's bound actions — then the migrated gameplay modules). Add new
// per-frame needs HERE (and one-time setup in SilksongBootstrap).
[DefaultExecutionOrder(-9000)] // pump input + modules here, before HeroController.Update (0) reads inputActions
internal sealed class HornetEnvironmentAdapter : MonoBehaviour {
    private static GameObject? go;
    private static FieldInfo? isGameplaySceneField;
    private static FieldInfo? gameStateField;
    private static MethodInfo? updateButtonQueueingMethod;
    private static MethodInfo? sendRefreshEventMethod;

    private bool wasHornetActive;

    private void Update() {
        try {
            var paused = Time.timeScale <= 0.0001f;
            var pd = Silksong::PlayerData.instance;
            var inventoryOpen = pd != null && pd.isInventoryOpen;

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

            // Only drive Hornet while SHE is the active character; when Knight is active she stays inert (HeroSwitch
            // disabled her HeroController + Rigidbody) so we must NOT force-enable her here.
            var hero = BundleSpike.RealHero;
            var active = hero != null && HeroSwitch.HornetActive;
            if (active != wasHornetActive) {
                wasHornetActive = active;
                HornetPlayerMod.LoadedInstance?.Modules.HornetToggled(active);
            }

            // HeroController: Start's non-gameplay path left the component disabled + isGameplayScene=false (our gm
            // isn't a "gameplay scene"), so Unity never ticks Update / LookForInput early-returns. Re-assert both — but
            // only during actual gameplay, not while the inventory freezes the world (we don't force-drive her there).
            if (active && !paused) {
                if (!hero!.enabled) hero.enabled = true;
                isGameplaySceneField ??= typeof(Silksong::HeroController)
                    .GetField("isGameplayScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                isGameplaySceneField?.SetValue(hero, true);
            }

            // Also pump while the inventory is open — the world is frozen at timeScale=0 but input must still reach the
            // inventory. A full pause menu (paused && !inventoryOpen) skips it.
            if (active && (!paused || inventoryOpen))
                HornetPlayerMod.LoadedInstance?.Modules.HornetActiveUpdate(hero!);

            if (paused) return;

            // --- The bookkeeping half of InputHandler.Update ---
            // InputHandler.Update never ticks (its GO is inactive). Its body splits cleanly in two: per-frame
            // bookkeeping the hero pipeline needs, and environment coupling we deliberately reject (SetCursorVisible
            // touches the OS cursor HK owns; UpdateActiveController -> SetupGamepadUIInputActions rebinds gamepad UI;
            // inputActions.Pause.WasPressed -> gm.PauseGameToggle runs *Silksong's* pause). So we don't run Update();
            // we run its bookkeeping methods by reflection — after the module pump above committed input this frame
            // (InputModule ran first), before HeroController/FSMs read it (this component is -9000, they are @0).
            // Add further Update-maintained state HERE.
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

                // Mirror HK's active controller (keyboard vs gamepad) onto Silksong's InputHandler. InputHandler.Update
                // (which we don't run) maintains this from InControl's device detection (UpdateActiveController, from
                // inputActions.LastInputType); we bypass Silksong's InControl, so its own stays None ->
                // Platform.WasLastInputKeyboard is false. That silently breaks menus that special-case keyboard: e.g.
                // Platform.GetMenuAction only maps DreamNail(D)->MenuActions.Super (the inventory change-crest shortcut)
                // on the keyboard branch. Since InputModule now feeds Hornet from HK's own bound input (keyboard AND
                // gamepad), take the device from HK's InputHandler.lastActiveController — the BindingSourceType enums are
                // identical in both games, so cast by value. Edge-only (on change), then fire RefreshActiveControllerEvent
                // (via the private SendRefreshEvent) so the glyph UIs (UIButtonSkins/ActionButtonIconBase, which cache
                // their icon and only recompute on that event) switch prompts. Firing per frame would recompute every
                // subscriber's icon needlessly.
                var hkController = InputHandler.Instance != null
                    ? (Silksong::InControl.BindingSourceType)(int)InputHandler.Instance.lastActiveController
                    : Silksong::InControl.BindingSourceType.KeyBindingSource;
                if (ih.lastActiveController != hkController) {
                    ih.lastActiveController = hkController;
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
    }

    internal static void Cleanup() {
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
