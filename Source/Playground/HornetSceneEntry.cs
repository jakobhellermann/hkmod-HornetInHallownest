extern alias Silksong;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Run Hornet's REAL Silksong HeroController.EnterScene so she walks/drops in with her own animation + entry FSMs
// (superjump entry, hard-land, drop-down), instead of being teleported (which left her falling OOB and not re-triggering
// the gate). HK owns the transition and relocates its Knight; we mirror HK's entry GATE into a fabricated Silksong
// TransitionPoint and feed it to Hornet's hero.
//
// Dependencies the un-run Silksong environment would provide are seeded elsewhere: Platform.Current +
// CheatManager.SceneEntryWait in SilksongBootstrap; GameManager.FadeSceneIn no-op'd in Stub (HK owns the fade).
internal static class HornetSceneEntry {
    // On by default: Hornet runs her real EnterScene. A/B toggle (POST /scene-entry?on=true|false) flips to the plain
    // snap-to-Knight for comparison/debugging.
    internal static bool Enabled = true;

    internal static IEnumerator Run(global::HeroController knight) {
        var hc = BundleSpike.RealHero;
        var hkGate = knight.sceneEntryGate;
        if (hc == null || hkGate == null) yield break;

        var gateGo = BuildGate(hkGate);
        var tp = gateGo.GetComponent<Silksong::TransitionPoint>();
        // EnterScene's StartCoroutine(EnterHeroSub...) runs on `hc`, so it must be the runner; we just await it here.
        yield return hc.StartCoroutine(hc.EnterScene(tp, 0f));
        Object.Destroy(gateGo);
    }

    // Fabricate a Silksong TransitionPoint mirroring HK's entry gate. GetGatePosition() parses the GameObject NAME for
    // top/left/right/bottom/door, so the name must carry the side; everything else is plain public fields (identical set
    // in both games). customEntryFSM is left null -> PrepareEntry/BeforeEntry/AfterEntry are no-ops.
    private static GameObject BuildGate(global::TransitionPoint hk) {
        var go = new GameObject(hk.name);
        go.transform.position = hk.transform.position;
        var tp = go.AddComponent<Silksong::TransitionPoint>();
        tp.entryOffset = hk.entryOffset;
        tp.isADoor = hk.isADoor;
        tp.entryDelay = hk.entryDelay;
        tp.alwaysEnterRight = hk.alwaysEnterRight;
        tp.alwaysEnterLeft = hk.alwaysEnterLeft;
        tp.hardLandOnExit = hk.hardLandOnExit;
        tp.nonHazardGate = hk.nonHazardGate;
        tp.customFade = hk.customFade;
        return go;
    }
}
