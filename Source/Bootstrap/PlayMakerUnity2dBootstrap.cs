extern alias Silksong;
extern alias SilksongPM;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Bootstrap;

// Without this scene-placed Silksong manager, PlayMakerUnity2DProxy disables itself and Hornet's collision-driven FSMs
// (landing, wall bumps, hazards) never fire.
internal static class PlayMakerUnity2dBootstrap {
    internal static void Ensure() {
        if (Silksong::PlayMakerUnity2d.isAvailable()) return;

        var go = new GameObject("PlayMaker Unity 2D");
        // PlayMakerUnity2d.Awake reads it.
        go.AddComponent<SilksongPM::PlayMakerFSM>();
        go.AddComponent<Silksong::PlayMakerUnity2d>();
        Object.DontDestroyOnLoad(go);
    }
}
