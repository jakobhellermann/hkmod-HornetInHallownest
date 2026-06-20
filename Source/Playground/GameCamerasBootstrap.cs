extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Bring up Silksong's `_GameCameras` rig (loaded via the registered Addressables catalog) just enough to satisfy
// Silksong's camera code: `GameCameras.instance` non-null + a live `CameraTarget`, so the per-frame "Couldn't find
// GameCameras" / "Failed to find camera target" + CallMethodProper "SetSprint" errors stop (SetSprint is just
// CameraTarget.sprinting = a lookahead flag — hero-driven, portable).
//
// HK's camera keeps rendering the world, so we NEUTER the rig's gameplay camera: disable every Camera (HK renders),
// every AudioListener (Unity allows ONE; HK owns it), and CameraController (NullRefs without a GameManager context +
// would fight HK). The HUD lives in this same rig (`HudCamera` + `Hud Canvas`); we keep it but SetActive(false) for now
// — its many FSMs need PlayerData/silk wiring and would spam. Revive it when the HUD is brought online. CameraTarget
// stays live (its lookahead feeds HK's camera follow later).
internal static class GameCamerasBootstrap {
    private static GameObject? rig;

    internal static object Ensure() {
        try {
            if (rig != null) return new { ok = true, note = "already", instanceSet = Silksong::GameCameras.SilentInstance != null };
            AddressablesBootstrap.Ensure();
            var prefab = Addressables.LoadAssetAsync<GameObject>("_GameCameras").WaitForCompletion();
            if (prefab == null) return new { error = "_GameCameras load returned null" };

            // Instantiate under an INACTIVE holder so nothing Awakes; we keep the rig DORMANT (see below).
            var holder = new GameObject("hp_gc_holder");
            holder.SetActive(false);
            var inst = Object.Instantiate(prefab, holder.transform);
            inst.name = "Silksong_GameCameras";

            int cams = 0, listeners = 0, controllers = 0, blur = 0;
            foreach (var c in inst.GetComponentsInChildren<Camera>(true)) { c.enabled = false; cams++; }
            foreach (var a in inst.GetComponentsInChildren<AudioListener>(true)) { a.enabled = false; listeners++; }
            foreach (var cc in inst.GetComponentsInChildren<Silksong::CameraController>(true)) { cc.enabled = false; controllers++; }
            // Per-frame cosmetic background-blur: NullRefs every frame without the scene's BlurPlanes / a real camera
            // setup (BlurManager.Update + LightBlurredBackground.LateUpdate -> BlurPlane.ClosestBlurPlane). Off.
            foreach (var b in inst.GetComponentsInChildren<Silksong::BlurManager>(true)) { b.enabled = false; blur++; }
            foreach (var b in inst.GetComponentsInChildren<Silksong::LightBlurredBackground>(true)) { b.enabled = false; blur++; }

            // HUD subtree: present but dormant for now.
            var hud = FindByName(inst.transform, "Hud Canvas");
            if (hud != null) hud.gameObject.SetActive(false);

            // Register GameCameras._instance so Silksong code resolves it.
            var gc = inst.GetComponentInChildren<Silksong::GameCameras>(true);
            if (gc != null)
                typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, gc);

            // Keep the rig DORMANT: setting _instance + the prefab-serialized fields (cameras, cameraTarget, …) is all
            // Silksong's code reads off GameCameras.instance — those are live on an inactive GameObject. Activating it
            // instead runs GameCameras.Awake/Start (DontDestroyOnLoad on this + gs.LoadOverscanSettings() where gs is
            // null -> NullRef) and every child Update/LateUpdate (blur, FSMs, HUD) — all of which we don't want yet.
            // SetActive(false) BEFORE reparenting so it stays inactive as a root (else SetParent(null) would activate it
            // and fire Awake). Revive selectively (HudCamera + Hud Canvas, with gs wired) when the HUD is brought online.
            inst.SetActive(false);
            inst.transform.SetParent(null, false);
            Object.DontDestroyOnLoad(inst);
            Object.DestroyImmediate(holder);
            rig = inst;

            var instanceSet = Silksong::GameCameras.SilentInstance != null;
            Log.Info($"[GameCameras] rig up: instance={instanceSet}, neutered cams={cams} listeners={listeners} controllers={controllers} blur={blur}, hudDormant={hud != null}");
            return new {
                ok = true,
                instanceSet,
                neutered = new { cameras = cams, audioListeners = listeners, cameraControllers = controllers, blur },
                hudDormant = hud != null,
                cameraTarget = inst.GetComponentInChildren<Silksong::CameraTarget>(true) != null,
            };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[GameCameras] bootstrap failed: {ex}");
            return new { error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    private static Transform? FindByName(Transform root, string name) {
        if (root.name == name) return root;
        foreach (Transform c in root) { var r = FindByName(c, name); if (r != null) return r; }
        return null;
    }

    internal static void Cleanup() {
        if (rig != null) { Object.Destroy(rig); rig = null; }
    }
}
