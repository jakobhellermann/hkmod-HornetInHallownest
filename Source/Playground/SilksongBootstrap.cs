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

    internal static void Ensure() {
        if (done) return;
        done = true;
        try {
            var pd = Silksong::PlayerData.instance; // create/get the PlayerData singleton

            gmGo = new GameObject("Silksong_GameManager");
            gmGo.SetActive(false); // inactive => GameManager/InputHandler Awake never runs
            var gm = gmGo.AddComponent<Silksong::GameManager>();
            var ih = gmGo.AddComponent<Silksong::InputHandler>(); // so gm.GetComponent<InputHandler>() resolves
            // GO is inactive => InputHandler.Awake never runs, so inputActions stays null and every input check
            // (CanAttackAction, ListenForAttack/Dash/... FSM actions) NullRefs. Construct it like InputHandler does.
            ih.inputActions = new Silksong::HeroActions();

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

            Log.Info($"[Bootstrap] GameManager.instance={(Silksong::GameManager.instance != null)}, " +
                     $"playerData={pd != null}, inputHandler={(gm.GetComponent<Silksong::InputHandler>() != null)}");
        } catch (Exception e) {
            Log.Error($"[Bootstrap] FAILED: {e}");
        }
    }

    internal static void Cleanup() {
        if (gmGo != null) { Object.Destroy(gmGo); gmGo = null; }
        if (poolGo != null) { Object.Destroy(poolGo); poolGo = null; }
        Silksong::GameManager._instance = null;
        done = false;
    }
}
