extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Minimal bootstrap of the Silksong runtime singletons that HeroController dereferences, WITHOUT running their real
// (heavy, environment-dependent) Awake. The GameManager GO stays inactive so no Awake fires; we set the public static
// _instance + the few fields HeroController reads (isPaused, playerData, an InputHandler component for
// gm.GetComponent<InputHandler>()). Grown iteratively as spawn-real reveals the next missing field.
internal static class SilksongBootstrap {
    private static GameObject? gmGo;
    private static GameObject? poolGo;
    private static bool done;

    // The HeroActions InControl set InputBridge drives each frame. Pass-through to the live Handler.inputActions (NOT a
    // cached snapshot): if anything ever re-runs InputHandler.OnAwake/Setup it reassigns inputActions, and a snapshot
    // would silently drift — InputDriver driving the old set while the hero reads the new one (move_input=0,
    // ia_same=false). Always dereferencing Handler keeps driver and hero on the same object by construction.
    internal static Silksong::HeroActions? InputActions => Handler?.inputActions;

    // The bootstrap InputHandler. Exposed so InputBridge can run the per-frame InputHandler bookkeeping that never
    // happens (its GO is inactive, so InputHandler.Update doesn't run) — e.g. clearing ForceDreamNailRePress.
    internal static Silksong::InputHandler? Handler { get; private set; }

    internal static void Ensure() {
        if (done) return;
        done = true;
        try {
            var pd = Silksong::PlayerData.instance; // create/get the PlayerData singleton
            // Fresh PlayerData has every ability locked (e.g. HeroController.CanDash() gates on playerData.hasDash).
            // Grant Hornet's full movement + combat kit so every move is usable in the playground.
            pd.hasDash = true;          // dash (Swift Step)
            pd.hasWalljump = true;      // wall jump
            pd.hasDoubleJump = true;    // double jump
            pd.hasBrolly = true;        // float / glide (umbrella)
            pd.hasSuperJump = true;     // needle super-jump
            pd.hasHarpoonDash = true;   // silk harpoon dash
            pd.hasChargeSlash = true;   // charge slash (nail art)
            pd.hasQuill = true;
            pd.hasParry = true;
            pd.hasNeedolin = true;
            pd.hasNeedleThrow = true;
            pd.hasThreadSphere = true;
            pd.hasSilkSpecial = true;   // silk special / arts
            pd.hasSilkCharge = true;
            pd.hasSilkBomb = true;
            pd.hasSilkBossNeedle = true;
            pd.hasNeedolinMemoryPowerup = true;
            // Silk resource so silk-cost abilities can actually fire.
            pd.silkMax = 9;
            pd.silk = 9;
            pd.silkSpecialLevel = 1;

            gmGo = new GameObject("Silksong_GameManager");
            gmGo.SetActive(false); // inactive => GameManager/InputHandler Awake never runs
            var gm = gmGo.AddComponent<Silksong::GameManager>();
            var ih = gmGo.AddComponent<Silksong::InputHandler>(); // so gm.GetComponent<InputHandler>() resolves
            // GO is inactive => InputHandler.Awake never runs, so inputActions stays null and every input check
            // (CanAttackAction, ListenForAttack/Dash/... FSM actions) NullRefs. Construct it like InputHandler does.
            ih.inputActions = new Silksong::HeroActions();
            Handler = ih;

            // InputHandler.OnAwake (which allocates buttonQueueTimers) never runs on our inactive GO, so
            // GetWasButtonPressedQueued NullRefs. Many of Hornet's FSMs (sprint, brolly/float, …) and HeroController
            // read queued input that way, stalling FSM-driven moves the moment they hand off to that path. Allocate the
            // array (sized by the HeroActionButton enum) so it returns "nothing queued" instead of throwing.
            var habType = typeof(Silksong::GlobalEnums.HeroActionButton);
            var habLen = 0;
            foreach (int v in Enum.GetValues(habType)) habLen = Math.Max(habLen, v + 1);
            typeof(Silksong::InputHandler).GetField("buttonQueueTimers", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(ih, new float[habLen]);

            // FSM input actions (GetWasButtonPressedQueued, …) read the InputHandler via
            // ManagerSingleton<InputHandler>.Instance, whose fallback is FindAnyObjectByType<T>() — which EXCLUDES
            // inactive objects, so it never finds our ih on the inactive bootstrap GO (-> NullRef in the action).
            // HeroController reaches ih directly (gm.GetComponent), which is why basic input worked but FSM actions
            // didn't. Register ih as the singleton so the FSM path resolves too.
            typeof(Silksong::ManagerSingleton<Silksong::InputHandler>)
                .GetProperty("UnsafeInstance", BindingFlags.Public | BindingFlags.Static)
                ?.GetSetMethod(true)?.Invoke(null, new object[] { ih });

            // gm.cameraCtrl (private-set property) is null -> SendHeroInPosition's gm.cameraCtrl.ResetStartTimer()
            // NullRefs. Provide a bare CameraController (on the inactive GO => no heavy Awake); ResetStartTimer just
            // sets a float. Assign via the property's backing field.
            var camCtrl = gmGo.AddComponent<Silksong::CameraController>();
            typeof(Silksong::GameManager).GetField("<cameraCtrl>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(gm, camCtrl);

            // gm.sm (CustomSceneManager, private-set) is null -> GetTotalFrostSpeed's gm.sm.FrostSpeed NullRefs (and
            // other per-scene env reads). Bare instance => default scene settings (FrostSpeed 0 etc.).
            var sm = gmGo.AddComponent<Silksong::CustomSceneManager>();
            typeof(Silksong::GameManager).GetField("<sm>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(gm, sm);

            Object.DontDestroyOnLoad(gmGo);

            // ObjectPool singleton: ObjectPool.CountPooled derefs ObjectPool.instance. Its getter does
            // FindObjectOfType<ObjectPool>(), so the pool must be on an ACTIVE GO (Awake sets _instance, and
            // pooledObjects is inline-initialized). Separate GO since gmGo is inactive.
            poolGo = new GameObject("Silksong_ObjectPool");
            poolGo.AddComponent<Silksong::ObjectPool>();
            Object.DontDestroyOnLoad(poolGo);

            Silksong::GameManager._instance = gm;
            gm.isPaused = false;
            gm.playerData = pd;
            // gm.GameState (LookForInput gates on == PLAYING) is maintained per-frame by HornetEnvironmentAdapter,
            // which also mirrors HK's pause onto it — so it's not set here.

            Log.Info($"[Bootstrap] GameManager.instance={(Silksong::GameManager.instance != null)}, " +
                     $"playerData={pd != null}, inputHandler={(gm.GetComponent<Silksong::InputHandler>() != null)}");
        } catch (Exception e) {
            Log.Error($"[Bootstrap] FAILED: {e}");
        }
    }

    internal static void Cleanup() {
        // DestroyImmediate (not Destroy): Unload->Initialize runs synchronously in one frame, so deferred end-of-frame
        // Destroys would leave the old GM/InputHandler alive while Initialize rebuilds — the spawned hero (and FSM
        // singleton lookups) could then bind to the stale instances. Tear down synchronously so a hot-reload state
        // equals a clean startup. Also null the static refs (InputActions/Handler) so nothing dangles into the dead GO.
        if (gmGo != null) { Object.DestroyImmediate(gmGo); gmGo = null; }
        if (poolGo != null) { Object.DestroyImmediate(poolGo); poolGo = null; }
        Silksong::GameManager._instance = null;
        Handler = null; // InputActions is computed from Handler, so this clears it too
        done = false;
    }
}
