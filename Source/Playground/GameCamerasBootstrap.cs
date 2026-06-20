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
    private const string RigName = "Silksong_GameCameras";
    private static GameObject? rig;
    private static Transform? inGame; // Hornet HUD content container ("In-game"), cached by BringUpHud for cheap toggling

    // Silksong's CameraTarget GameObject (on the rig). Silksong hero FSMs reference a "Camera Target" via a serialized
    // FsmGameObject whose cross-game PPtr is lost -> they'd fall back to GameObject.Find("Camera Target") and hit HK's
    // same-named object (HK's CameraTarget has no SetSprint -> "Method Name is invalid"). Rewire those refs to THIS.
    internal static GameObject? CameraTargetGo =>
        rig != null ? rig.GetComponentInChildren<Silksong::CameraTarget>(true)?.gameObject : null;

    // Hot-reload safe: the rig is DontDestroyOnLoad and survives our DLL reload (our `rig` static does not). Reuse the
    // existing rig instead of spawning a duplicate. Found by its unique instance name (the prefab is "_GameCameras").
    private static GameObject? FindExistingRig() {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == RigName && go.scene.IsValid()) return go;
        return null;
    }

    internal static object Ensure() {
        try {
            rig ??= FindExistingRig();
            if (rig != null) {
                // Re-point GameCameras._instance in case it dangled across the reload (Silksong-assembly static).
                var existing = rig.GetComponentInChildren<Silksong::GameCameras>(true);
                if (existing != null && Silksong::GameCameras.SilentInstance == null)
                    typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, existing);
                return new { ok = true, note = "reused existing rig", instanceSet = Silksong::GameCameras.SilentInstance != null };
            }
            AddressablesBootstrap.Ensure();
            var prefab = Addressables.LoadAssetAsync<GameObject>("_GameCameras").WaitForCompletion();
            if (prefab == null) return new { error = "_GameCameras load returned null" };

            // Instantiate under an INACTIVE holder so NO Awake runs while we neuter components (Awake fires the instant a
            // component is active; the conflicting ones must be disabled first). Then ACTIVATE the holder so every HUD
            // FSM (health_display, silk, tools, …) initializes itself via its real Awake/Start + the normal scene-ready
            // events — the only way to a COMPLETE HUD. (The old DORMANT rig left those FSMs un-started: ActiveStateName
            // empty -> health masks' MeshRenderers never enabled -> no health/silk despite the GameObjects being active.)
            var holder = new GameObject("hp_gc_holder");
            holder.SetActive(false);
            var inst = Object.Instantiate(prefab, holder.transform);
            inst.name = RigName;

            // Neuter the parts HK owns BEFORE activation so their per-frame Update never runs: HK renders the world AND
            // the HUD (HK's HudCamera draws the UI-layer HUD meshes — verified), owns the single AudioListener, and
            // CameraController would fight HK + NullRef without a full GameManager. Disable (don't destroy) so the rig's
            // serialized refs to them stay non-null for code that only reads the fields.
            int cams = 0, listeners = 0, controllers = 0, blur = 0;
            foreach (var c in inst.GetComponentsInChildren<Camera>(true)) { c.enabled = false; cams++; }
            foreach (var a in inst.GetComponentsInChildren<AudioListener>(true)) { a.enabled = false; listeners++; }
            foreach (var cc in inst.GetComponentsInChildren<Silksong::CameraController>(true)) { cc.enabled = false; controllers++; }
            // Per-frame cosmetic background-blur: NullRefs every frame without the scene's BlurPlanes. Off.
            foreach (var b in inst.GetComponentsInChildren<Silksong::BlurManager>(true)) { b.enabled = false; blur++; }
            foreach (var b in inst.GetComponentsInChildren<Silksong::LightBlurredBackground>(true)) { b.enabled = false; blur++; }

            // Seed the singleton the HUD FSMs deref during their Awake/Start, BEFORE activation. PlayerData (health/silk)
            // + GlobalSettings are already seeded earlier in SpawnReal (SilksongBootstrap / GlobalSettingsBootstrap).
            UIManagerBootstrap.Ensure();

            // Set GameCameras._instance NOW (before activation) so child HUD FSMs resolve GameCameras.instance during
            // their Awake. GameCameras.Awake is skipped in Stub (it would warn "DontDestroyOnLoad on non-root" since our
            // rig is under the holder); we DDOL the holder instead, below.
            var gc = inst.GetComponentInChildren<Silksong::GameCameras>(true);
            if (gc != null)
                typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, gc);

            Object.DontDestroyOnLoad(holder);
            rig = inst;
            holder.SetActive(true);   // ACTIVATE -> Awakes/Starts run (GameCameras.Awake/Start skipped in Stub)

            var instanceSet = Silksong::GameCameras.SilentInstance != null;
            Log.Info($"[GameCameras] rig ACTIVE+neutered: instance={instanceSet}, cams={cams} listeners={listeners} controllers={controllers} blur={blur}");
            return new {
                ok = true,
                active = true,
                instanceSet,
                neutered = new { cameras = cams, audioListeners = listeners, cameraControllers = controllers, blur },
                cameraTarget = inst.GetComponentInChildren<Silksong::CameraTarget>(true) != null,
            };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[GameCameras] bootstrap failed: {ex}");
            return new { error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    // Quick-fix the per-frame "Invalid layer id" flood: Silksong HUD elements carry MeshSortingOrder components whose
    // serialized layerID is a Silksong sorting-layer uniqueID. Most match HK, but "Inventory"/"Scene Border" have
    // DIFFERENT uniqueIDs across the games (verified via globalgamemanagers) -> HK rejects them, and MeshSortingOrder
    // .OnUpdate re-applies every frame -> 769/frame. Remap each INVALID layerID (the private field, since OnUpdate
    // re-reads it) to HK's by-name equivalent ("HUD" fallback). Proper fix is a TagManager superset (open item).
    private static readonly System.Collections.Generic.Dictionary<int, string> SsSortingLayerNames = new() {
        { 59515797, "Inventory" }, { 1017658613, "Scene Border" },
    };
    private static int RemapSortingLayers(Transform root) {
        var msoType = typeof(Silksong::MeshSortingOrder);
        var layerIdF = msoType.GetField("layerID", BindingFlags.NonPublic | BindingFlags.Instance);
        if (layerIdF == null) return -1;
        int fixes = 0;
        foreach (var mso in root.GetComponentsInChildren<Silksong::MeshSortingOrder>(true)) {
            var id = (int)(layerIdF.GetValue(mso) ?? 0);
            if (SortingLayer.IsValid(id)) continue;
            var hkId = SortingLayer.NameToID(SsSortingLayerNames.TryGetValue(id, out var nm) ? nm : "HUD");
            layerIdF.SetValue(mso, hkId);
            var rend = ((Component)(object)mso).GetComponent<MeshRenderer>();
            if (rend != null) rend.sortingLayerID = hkId;
            fixes++;
        }
        return fixes;
    }

    private static Transform? FindByName(Transform root, string name) {
        if (root.name == name) return root;
        foreach (Transform c in root) { var r = FindByName(c, name); if (r != null) return r; }
        return null;
    }

    // Bring up Silksong's HUD now that the rig is ACTIVE (Ensure activated it -> every HUD FSM self-initialized). This is
    // just the "slide it in" step: ensure the HUD containers are active, fix sorting layers, re-point the SilkSpool, and
    // call HUDIn. The HUD is NOT extracted onto its own camera — the rig's HudCamera stays DISABLED and HK's HudCamera
    // renders the UI-layer HUD meshes (verified). Data wiring (Health/Tool FSMs) + hiding HK's own HUD come next.
    internal static object BringUpHud(bool on) {
        rig ??= FindExistingRig();
        if (rig == null) return new { error = "rig not up (spawn first)" };
        var hudCam = FindByName(rig.transform, "HudCamera");
        if (hudCam == null) return new { error = "HudCamera not found" };
        var cam = hudCam.GetComponent<Camera>();
        if (!on) {
            FindByName(hudCam, "In-game")?.gameObject.SetActive(false);
            return new { ok = true, hudOn = false };
        }
        // The HudCamera GO is INACTIVE in the prefab (the game's GameCameras.StartScene activates it; we skip that), so
        // its whole HUD subtree — and the FSMs that drive it (health_display, silk, …) — never Awoke. Activate the GO so
        // the FSMs self-initialize. Keep the Camera COMPONENT off: HK's HudCamera renders the UI-layer HUD meshes (no
        // double-render). NOTE: activating the subtree runs InstantiateOnAwake on Health etc. — that's where the
        // transient "(Game Object '<null>')" missing-script warnings come from (harmless: those clones are discarded).
        if (cam != null) cam.enabled = false;
        hudCam.gameObject.SetActive(true);
        FindByName(hudCam, "Hud Canvas")?.gameObject.SetActive(true);
        inGame = FindByName(hudCam, "In-game");      // cache the HUD content container for per-frame visibility toggling
        inGame?.gameObject.SetActive(true);          // the in-game HUD container (Health/Thread/…)
        var layerFixes = RemapSortingLayers(hudCam);

        // Silk meter: the HUD's OWN SilkSpool (Thread/Spool) has the visual refs (capR/seg1/silkChunkPrefab); our
        // bootstrap's BARE SilkSpool (on the GM GO, added for AddUsingSilk) hijacked SilkSpool.Instance, so the real
        // one's Awake returned early ("if (Instance) return") and the meter never drew. Re-point Instance to the real
        // one + DrawSpool now that the HUD is active.
        var realSpool = hudCam.GetComponentInChildren<Silksong::SilkSpool>(true);
        if (realSpool != null) {
            typeof(Silksong::SilkSpool).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetSetMethod(true)?.Invoke(null, new object[] { realSpool });
            try { realSpool.DrawSpool(); } catch (Exception e) { Log.Error($"[HUD] DrawSpool: {e.Message}"); }
        }

        // Fade/slide the HUD content in (health/silk start hidden until the game fires this on scene-ready).
        try { Silksong::GameCameras.instance?.HUDIn(); } catch (Exception e) { Log.Error($"[HUD] HUDIn: {e.Message}"); }

        var uiSet = Silksong::UIManager.instance != null;
        return new { ok = true, hudOn = true, camEnabled = cam != null && cam.enabled, uiManager = uiSet, layerFixes,
                     silkSpool = realSpool != null };
    }

    internal static bool HornetHudReady => inGame != null;

    // Show/hide Hornet's HUD content (the "In-game" container) without re-running the heavy BringUpHud — the HudCamera GO
    // + FSMs stay alive, only the content is toggled. Cheap; safe to call per-frame (only SetActive on change).
    internal static void SetHornetHudVisible(bool on) {
        if (inGame == null) return;
        if (inGame.gameObject.activeSelf != on) inGame.gameObject.SetActive(on);
    }

    // Show/hide HK's native Knight HUD (the hudCanvas on HK's own GameCameras). global::GameCameras is HK's (unprefixed).
    // Read the cached _instance FIELD directly, NOT the `instance` getter — that getter logs "Couldn't find GameCameras"
    // when null (e.g. at the menu, where this runs per-frame via HeroSwitch) -> spam. The field is null-safe + silent.
    private static FieldInfo? hkGcInstanceField;
    internal static void SetHkHudVisible(bool on) {
        try {
            hkGcInstanceField ??= typeof(global::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (hkGcInstanceField?.GetValue(null) is not global::GameCameras hk) return;
            if (hk.hudCanvas != null && hk.hudCanvas.activeSelf != on) hk.hudCanvas.SetActive(on);
        } catch (Exception e) { Log.Error($"[HUD] SetHkHudVisible({on}): {e.Message}"); }
    }

    internal static void Cleanup() {
        inGame = null;
        // DestroyImmediate (synchronous) so the rig is gone before a hot-reload's Initialize runs Ensure again — a
        // deferred Object.Destroy could leave it alive into the next generation, where FindExistingRig would then reuse
        // a doomed object. Also clear GameCameras._instance (Silksong-assembly static) so it doesn't dangle.
        rig ??= FindExistingRig();
        // Destroy the persisted root (the inactive holder), which owns the rig as a child — not just `rig` itself, or the
        // holder would leak. root == rig in the degenerate case where rig is already a root.
        if (rig != null) { Object.DestroyImmediate(rig.transform.root.gameObject); rig = null; }
        typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
    }
}
