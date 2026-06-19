extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

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
    private static FieldInfo? fixedUpdateCycleField;

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.EnvironmentAdapter");
        go.AddComponent<HornetEnvironmentAdapter>();
        Object.DontDestroyOnLoad(go);
    }

    internal static void Cleanup() {
        if (go != null) { Object.Destroy(go); go = null; }
    }

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
            var hero = BundleSpike.RealHero;
            if (hero != null) {
                if (!hero.enabled) hero.enabled = true;
                isGameplaySceneField ??= typeof(Silksong::HeroController)
                    .GetField("isGameplayScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                isGameplaySceneField?.SetValue(hero, true);
            }

            // TESTING: infinite silk so silk-cost abilities always fire.
            var pd = Silksong::PlayerData.instance;
            if (pd != null) pd.silk = pd.silkMax;

            // RegainControl sets InputHandler.ForceDreamNailRePress=true; it's normally cleared in InputHandler.Update
            // (never runs on the inactive GO), so ListenForDreamNail would skip forever -> clear it once unheld.
            var ih = SilksongBootstrap.Handler;
            if (ih?.inputActions != null && !ih.inputActions.DreamNail.IsPressed)
                ih.ForceDreamNailRePress = false;
        } catch (Exception e) { Log.Error($"[EnvAdapter] {e}"); }
    }

    private void FixedUpdate() {
        try {
            // Silksong's CustomPlayerLoop (which ticks FixedUpdateCycle) isn't injected into HK, so the counter is
            // frozen and cycle-gated FSM actions (e.g. CheckCollisionSide -> Sprint LAND) run once then early-return
            // forever. Advance it each FixedUpdate (any monotonic change satisfies the guards).
            fixedUpdateCycleField ??= typeof(Silksong::CustomPlayerLoop)
                .GetField("<FixedUpdateCycle>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
            if (fixedUpdateCycleField != null)
                fixedUpdateCycleField.SetValue(null, (int)fixedUpdateCycleField.GetValue(null)! + 1);
        } catch (Exception e) { Log.Error($"[EnvAdapter.FixedUpdate] {e}"); }
    }
}
