extern alias Silksong;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Minimal bootstrap of the Silksong runtime singletons that HeroController dereferences, WITHOUT running their real
// (heavy, environment-dependent) Awake. The GameManager GO stays inactive so no Awake fires; we set the public static
// _instance + the few fields HeroController reads (isPaused, playerData, an InputHandler component for
// gm.GetComponent<InputHandler>()). Grown iteratively as spawn-real reveals the next missing field.
internal static class SilksongBootstrap {
    private static GameObject? gmGo;
    private static bool done;

    internal static void Ensure() {
        if (done) return;
        done = true;
        try {
            var pd = Silksong::PlayerData.instance; // create/get the PlayerData singleton

            gmGo = new GameObject("Silksong_GameManager");
            gmGo.SetActive(false); // inactive => GameManager/InputHandler Awake never runs
            var gm = gmGo.AddComponent<Silksong::GameManager>();
            gmGo.AddComponent<Silksong::InputHandler>(); // so gm.GetComponent<InputHandler>() resolves
            Object.DontDestroyOnLoad(gmGo);

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
        Silksong::GameManager._instance = null;
        done = false;
    }
}
