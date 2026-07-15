using System;
using HornetPlayer.HornetInHallownest.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HornetPlayer.HornetInHallownest.Modules;

public sealed class SceneFixesModule : ModuleBase {
    public override void Initialize() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override string Id => "scene-fixes";

    protected override void OnDeinitialize() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        switch (scene.name) {
            case "Deepnest_Spider_Town":
                FixSpiderTrapBench();
                break;
            case "White_Palace_12":
                DisableWhitePalace12Saws(scene);
                break;
        }
    }

    #region White_Palace_12 saws

    private static readonly string[] WhitePalace12DisabledSaws = { "wp_saw (18)", "wp_saw (22)", "wp_saw (23)" };

    private void DisableWhitePalace12Saws(Scene scene) {
        try {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (Array.IndexOf(WhitePalace12DisabledSaws, root.name) >= 0) {
                    root.SetActive(false);
                    count++;
                }

            LogDebug($"disabled {count}/{WhitePalace12DisabledSaws.Length} saws");
        } catch (Exception e) {
            LogError(e.Message);
        }
    }

    #endregion

    #region Deepnest_Spider_Town trap bench

    // The trap bench's `Fade` FSM falls the hero and waits for `Hero Y <= Hero Land Y` (baked 19.5, calibrated to the
    // KNIGHT) to fire LAND -> return control. Hornet rests ~0.16 higher (deeper collider feet), so her Y never reaches
    // the Knight-frame land-Y -> the FSM hangs, no-input, on the floor. Raise the baked land-Y into her frame by the
    // live collider feet-delta; the FSM then runs Fall -> Land -> Relinquish Control (incl. SetBenchRespawn, SaveGame).
    // TODO(unverified): numbers confirmed live, but the softlock -> fixed round-trip isn't verified end-to-end (the trap
    // is a one-time spiderCapture and the test save is past it). Re-verify on a fresh save reaching this bench.
    private void FixSpiderTrapBench() {
        try {
            var bench = GameObject.Find("RestBench Spider");
            if (bench == null) return;

            PlayMakerFSM? fade = null;
            foreach (var f in bench.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Fade") {
                    fade = f;
                    break;
                }

            var landY = fade?.FsmVariables.FindFsmFloat("Hero Land Y");
            if (landY == null) return;

            var delta = FeetDelta();
            if (delta <= 0f) return; // colliders not ready, or no overshoot to correct

            var before = landY.Value;
            landY.Value = before + delta;
            LogDebug($"raised 'Hero Land Y' {before} -> {landY.Value} (+{delta} collider feet-delta)");
        } catch (Exception e) {
            LogError(e.Message);
        }
    }

    // How much higher Hornet's origin rests on a floor than the Knight's, from the live colliders (collider bottom =
    // offset.y - size.y/2). Returns 0 if either collider is missing (fail-safe: no patch).
    private static float FeetDelta() {
        var hornetCol = HornetSpawner.TerrainCollider;
        var knight = HeroController.UnsafeInstance;
        var knightCol = knight != null ? knight.GetComponent<BoxCollider2D>() : null;
        if (hornetCol == null || knightCol == null) return 0f;
        var hornetBottom = hornetCol.offset.y - hornetCol.size.y / 2f;
        var knightBottom = knightCol.offset.y - knightCol.size.y / 2f;
        return knightBottom - hornetBottom;
    }

    #endregion
}
