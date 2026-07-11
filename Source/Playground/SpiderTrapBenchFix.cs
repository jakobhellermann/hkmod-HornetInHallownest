using System;
using HornetPlayer.HornetInHallownest.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HornetPlayer.Playground;

// Deepnest_Spider_Town trap bench ("RestBench Spider"): sitting webs the hero, fades to this scene, then its `Fade` FSM
// FALLS the hero and waits for `Hero Y <= Hero Land Y` (FloatCompare, tolerance 0) to fire LAND -> Land -> Relinquish
// Control (where RegainControl/StartAnimationControl on the Hero run). `Hero Land Y` is a baked value (19.5) calibrated
// to the KNIGHT's grounded transform.y. Hornet's terrain-collider bottom sits LOWER relative to her origin than the
// Knight's (feet-depth ~1.55 vs ~1.39), so grounded she rests ~0.16 HIGHER -> her Y never drops to the Knight-frame
// land-Y -> Fall hangs -> control is never returned (she's stuck no-input on the "Airborne" frame on the floor).
//
// Fix: translate the baked land-Y into Hornet's frame by raising it by the collider feet-delta (derived LIVE from the
// two colliders, not hardcoded). One-time data correction on scene load; the FSM then runs its own Fall -> Land ->
// Relinquish Control to completion, including the real side effects (SetBenchRespawn "BEASTS_DEN", spiderCapture,
// SaveGame). Event-driven (sceneLoaded), not a per-frame poll.
//
// TODO(unverified): root cause + numbers confirmed live (Hero Land Y=19.5, Hornet rested 19.565 -> 0.065 over), but the
// softlock -> fixed round-trip is NOT verified end-to-end — the trap is a one-time event (spiderCapture) and the test
// save is already past it. Re-verify on a fresh save that reaches this bench.
internal static class SpiderTrapBenchFix {
    private const string BenchName = "RestBench Spider";
    private const string FadeFsm = "Fade";
    private const string LandYVar = "Hero Land Y";
    private static bool subscribed;

    internal static void Install() {
        if (subscribed) return;
        // Fully qualified: HK has its own global `SceneManager` type that would shadow the Unity one.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        subscribed = true;
    }

    internal static void Cleanup() {
        if (!subscribed) return;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        subscribed = false;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        try {
            var bench = GameObject.Find(BenchName);
            if (bench == null) return; // not the spider-trap scene

            PlayMakerFSM? fade = null;
            foreach (var f in bench.GetComponents<PlayMakerFSM>())
                if (f.FsmName == FadeFsm) {
                    fade = f;
                    break;
                }

            var landY = fade?.FsmVariables.FindFsmFloat(LandYVar);
            if (landY == null) return;

            var delta = FeetDelta();
            if (delta <= 0f) return; // colliders not ready, or no overshoot to correct

            var before = landY.Value;
            landY.Value = before + delta;
            Log.Debug($"[SpiderTrapBench] raised '{LandYVar}' {before} -> {landY.Value} (+{delta} collider feet-delta) so "
                     + "Hornet's higher grounded Y still trips the Fall land-check (else the trap FSM hangs, no control back)");
        } catch (Exception e) {
            Log.Error($"[SpiderTrapBench] {e.Message}");
        }
    }

    // How much higher Hornet's origin rests on a floor than the Knight's, from the live colliders (collider bottom =
    // offset.y - size.y/2; the origin sits |bottom| above the feet). delta = knightBottom - hornetBottom, e.g.
    // -1.39 - (-1.55) = 0.16. Returns 0 if either collider is missing (fail-safe: no patch).
    private static float FeetDelta() {
        var hornetCol = HornetSpawner.TerrainCollider;
        var knight = HeroController.UnsafeInstance;
        var knightCol = knight != null ? knight.GetComponent<BoxCollider2D>() : null;
        if (hornetCol == null || knightCol == null) return 0f;
        var hornetBottom = hornetCol.offset.y - hornetCol.size.y / 2f;
        var knightBottom = knightCol.offset.y - knightCol.size.y / 2f;
        return knightBottom - hornetBottom;
    }
}
