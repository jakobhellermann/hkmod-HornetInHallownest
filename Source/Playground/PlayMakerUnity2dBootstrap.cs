extern alias Silksong;
extern alias SilksongPM;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Silksong's PlayMaker 2D integration needs a global "PlayMaker Unity 2D" manager in the scene. Without it,
// PlayMakerUnity2DProxy.Start() logs "requires the 'PlayMaker Unity 2D' Prefab" and **disables itself** — which kills
// BOTH the per-object collision/trigger delegate path (CheckCollisionSideEnter/DetectCollisonDown/Trigger2dEvent
// register AddOnCollision2dDelegate) AND the global-event forwarding. So Hornet's collision/trigger-driven FSM logic
// (landing, wall bumps, hazards) silently never fires. The manager is a scene-placed prefab in Silksong (no addressable
// key, not created in code), so it doesn't exist in HK — we create it.
//
// isAvailable() == (fsmProxy != null), and fsmProxy = GetComponent<PlayMakerFSM>() in PlayMakerUnity2d.Awake(). So the
// manager is just a GameObject carrying a PlayMakerFSM + PlayMakerUnity2d. Hot-reload safe: isAvailable() reads a
// Silksong-assembly static that survives our DLL reload, and the manager is DontDestroyOnLoad — so a reload reuses it.
internal static class PlayMakerUnity2dBootstrap {
    internal static void Ensure() {
        try {
            if (Silksong::PlayMakerUnity2d.isAvailable()) return; // already up (survives hot-reload)
            var go = new GameObject("PlayMaker Unity 2D");
            go.AddComponent<SilksongPM::PlayMakerFSM>(); // must exist BEFORE PlayMakerUnity2d.Awake reads GetComponent<PlayMakerFSM>()
            go.AddComponent<Silksong::PlayMakerUnity2d>(); // Awake: fsmProxy = that FSM -> isAvailable() == true
            Object.DontDestroyOnLoad(go);
            var ok = Silksong::PlayMakerUnity2d.isAvailable();
            Log.Info($"[PMUnity2d] manager created, isAvailable={ok}");
        } catch (Exception e) {
            Log.Error($"[PMUnity2d] failed: {e.InnerException ?? e}");
        }
    }
}
