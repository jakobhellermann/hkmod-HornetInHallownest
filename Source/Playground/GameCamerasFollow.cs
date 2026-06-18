using System;
using UnityEngine;

namespace HornetPlayer.Playground;

// Quick stand-in for Silksong's CameraController (which NullRefs without a GameManager context): pin the camera to
// Hornet's position each frame. Re-finds Hornet by name so it survives respawns. Replace with the real
// CameraController once the GameManager is wired.
internal class GameCamerasFollow : MonoBehaviour {
    internal Transform? cam;
    private Transform? target;

    private void LateUpdate() {
        try {
            // Prefer the spawned Hornet; fall back to HK's Knight so the camera follows whoever the player is.
            var hornet = BundleSpike.HornetRoot;
            if (hornet != null) target = hornet.transform;
            else if (global::HeroController.instance != null) target = global::HeroController.instance.transform;
            else return;
            if (cam == null || target == null) return;
            var p = target.position;
            cam.position = new Vector3(p.x, p.y, cam.position.z);
        } catch (Exception e) {
            Log.Error(e);
        }
    }
}
