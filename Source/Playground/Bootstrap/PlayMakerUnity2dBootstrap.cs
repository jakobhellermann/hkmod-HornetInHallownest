extern alias Silksong;
extern alias SilksongPM;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Silksong's PlayMaker 2D integration needs a global "PlayMaker Unity 2D" manager, else PlayMakerUnity2DProxy.Start
// disables itself and Hornet's collision/trigger-driven FSM logic (landing, wall bumps, hazards) silently never fires.
// It's a scene-placed prefab in Silksong (no addressable key), absent in HK, so we create it: a GO with a PlayMakerFSM
// (fsmProxy) + PlayMakerUnity2d. Hot-reload safe (isAvailable() reads a surviving Silksong static; GO is DDOL).
internal static class PlayMakerUnity2dBootstrap {
    internal static void Ensure() {
        try {
            if (Silksong::PlayMakerUnity2d.isAvailable()) return; // already up (survives hot-reload)
            var go = new GameObject("PlayMaker Unity 2D");
            go.AddComponent<SilksongPM::PlayMakerFSM>(); // must exist before PlayMakerUnity2d.Awake reads GetComponent<PlayMakerFSM>()
            go.AddComponent<Silksong::PlayMakerUnity2d>(); // Awake: fsmProxy = that FSM -> isAvailable() == true
            Object.DontDestroyOnLoad(go);
            var ok = Silksong::PlayMakerUnity2d.isAvailable();
            Log.Debug($"[PMUnity2d] manager created, isAvailable={ok}");
        } catch (Exception e) {
            Log.Error($"[PMUnity2d] failed: {e.InnerException ?? e}");
        }
    }
}
