extern alias Silksong;
using System;
using System.Collections.Generic;
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

    private static Transform?
        inGame; // Hornet HUD content container ("In-game"), cached by BringUpHud for cheap toggling

    // Quick-fix the per-frame "Invalid layer id" flood: Silksong HUD elements carry MeshSortingOrder components whose
    // serialized layerID is a Silksong sorting-layer uniqueID. Most match HK, but "Inventory"/"Scene Border" have
    // DIFFERENT uniqueIDs across the games (verified via globalgamemanagers) -> HK rejects them, and MeshSortingOrder
    // .OnUpdate re-applies every frame -> 769/frame. Remap each INVALID layerID (the private field, since OnUpdate
    // re-reads it) to HK's by-name equivalent ("HUD" fallback). Proper fix is a TagManager superset (open item).
    private static readonly Dictionary<int, string> SsSortingLayerNames = new() {
        { 59515797, "Inventory" }, { 1017658613, "Scene Border" }
    };

    // Show/hide HK's native Knight HUD (the hudCanvas on HK's own GameCameras). global::GameCameras is HK's (unprefixed).
    // Read the cached _instance FIELD directly, NOT the `instance` getter — that getter logs "Couldn't find GameCameras"
    // when null (e.g. at the menu, where this runs per-frame via HeroSwitch) -> spam. The field is null-safe + silent.
    private static FieldInfo? hkGcInstanceField;
    private static Vector3 hkHudScale = Vector3.one; // HK hudCanvas' real scale, cached so we can restore after hiding
    private static HashSet<PlayMakerFSM>? disabledHudFsms; // HK HUD FSMs we disabled when Knight is inert

    private static Transform?
        ssMainCamT; // Silksong rig's (neutered) main camera transform, kept on HK's camera for audio

    // Silksong's CameraTarget GameObject (on the rig). Silksong hero FSMs reference a "Camera Target" via a serialized
    // FsmGameObject whose cross-game PPtr is lost -> they'd fall back to GameObject.Find("Camera Target") and hit HK's
    // same-named object (HK's CameraTarget has no SetSprint -> "Method Name is invalid"). Rewire those refs to THIS.
    internal static GameObject? CameraTargetGo =>
        rig != null ? rig.GetComponentInChildren<Silksong::CameraTarget>(true)?.gameObject : null;

    internal static bool HornetHudReady => inGame != null;

    // Hot-reload safe: the rig is DontDestroyOnLoad and survives our DLL reload (our `rig` static does not). Reuse the
    // existing rig instead of spawning a duplicate. Found by its unique instance name (the prefab is "_GameCameras").
    private static GameObject? FindExistingRig() {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == RigName && go.scene.IsValid())
                return go;
        return null;
    }

    internal static object Ensure() {
        try {
            rig ??= FindExistingRig();
            if (rig != null) {
                // Re-point GameCameras._instance in case it dangled across the reload (Silksong-assembly static).
                var existing = rig.GetComponentInChildren<Silksong::GameCameras>(true);
                if (existing != null && Silksong::GameCameras.SilentInstance == null)
                    typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                        ?.SetValue(null, existing);
                return new {
                    ok = true, note = "reused existing rig", instanceSet = Silksong::GameCameras.SilentInstance != null
                };
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
            foreach (var c in inst.GetComponentsInChildren<Camera>(true)) {
                c.enabled = false;
                cams++;
            }

            foreach (var a in inst.GetComponentsInChildren<AudioListener>(true)) {
                a.enabled = false;
                listeners++;
            }

            foreach (var cc in inst.GetComponentsInChildren<Silksong::CameraController>(true)) {
                cc.enabled = false;
                controllers++;
            }

            // Per-frame cosmetic background-blur: NullRefs every frame without the scene's BlurPlanes. Off.
            foreach (var b in inst.GetComponentsInChildren<Silksong::BlurManager>(true)) {
                b.enabled = false;
                blur++;
            }

            foreach (var b in inst.GetComponentsInChildren<Silksong::LightBlurredBackground>(true)) {
                b.enabled = false;
                blur++;
            }

            // Seed the singleton the HUD FSMs deref during their Awake/Start, BEFORE activation. PlayerData (health/silk)
            // + GlobalSettings are already seeded earlier in SpawnReal (SilksongBootstrap / GlobalSettingsBootstrap).
            UIManagerBootstrap.Ensure();

            // Set GameCameras._instance NOW (before activation) so child HUD FSMs resolve GameCameras.instance during
            // their Awake. GameCameras.Awake is skipped in Stub (it would warn "DontDestroyOnLoad on non-root" since our
            // rig is under the holder); we DDOL the holder instead, below.
            var gc = inst.GetComponentInChildren<Silksong::GameCameras>(true);
            if (gc != null) {
                typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.SetValue(null, gc);
                // Wire gm.gameCams (public field, set by GameManager.SetupGameRefs which we don't run) -> several paths
                // deref it (e.g. inventory open's SetPausedState gameCams.StopCameraShake, HUD). GameManager.instance is
                // already up (SilksongBootstrap ran before us in SpawnReal).
                if (Silksong::GameManager.instance != null) Silksong::GameManager.instance.gameCams = gc;
            }

            Object.DontDestroyOnLoad(holder);
            rig = inst;
            holder.SetActive(true); // ACTIVATE -> Awakes/Starts run (GameCameras.Awake/Start skipped in Stub)

            var instanceSet = Silksong::GameCameras.SilentInstance != null;
            Log.Info(
                $"[GameCameras] rig ACTIVE+neutered: instance={instanceSet}, cams={cams} listeners={listeners} controllers={controllers} blur={blur}");
            return new {
                ok = true,
                active = true,
                instanceSet,
                neutered = new { cameras = cams, audioListeners = listeners, cameraControllers = controllers, blur },
                cameraTarget = inst.GetComponentInChildren<Silksong::CameraTarget>(true) != null
            };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[GameCameras] bootstrap failed: {ex}");
            return new { error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    private static int RemapSortingLayers(Transform root) {
        var msoType = typeof(Silksong::MeshSortingOrder);
        var layerIdF = msoType.GetField("layerID", BindingFlags.NonPublic | BindingFlags.Instance);
        if (layerIdF == null) return -1;
        var fixes = 0;
        foreach (var mso in root.GetComponentsInChildren<Silksong::MeshSortingOrder>(true)) {
            var id = (int)(layerIdF.GetValue(mso) ?? 0);
            if (SortingLayer.IsValid(id)) continue;
            var hkId = SortingLayer.NameToID(SsSortingLayerNames.TryGetValue(id, out var nm) ? nm : "HUD");
            layerIdF.SetValue(mso, hkId);
            var rend = mso.GetComponent<MeshRenderer>();
            if (rend != null) rend.sortingLayerID = hkId;
            fixes++;
        }

        return fixes;
    }

    private static Transform? FindByName(Transform root, string name) {
        if (root.name == name) return root;
        foreach (Transform c in root) {
            var r = FindByName(c, name);
            if (r != null) return r;
        }

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
        // The HudCamera subtree contains FAR more than the in-game HUD: under "In-game" sit Inventory / Quick Map /
        // Game Map Rendering / DialogueManager / Prompts / Menus panes that NullRef on Awake without their managers
        // (ToolItemManager #6, gm.tilemap, quest/tool lists, button-skin input). The ONLY in-game HUD is "Anchor TL"
        // (Health + silk under it). Deactivate In-game's other children BEFORE activating HudCamera, so those panes
        // never Awake (a child set inactive while its parent chain is inactive won't fire Awake on activation).
        inGame = FindByName(hudCam, "In-game"); // cache the HUD content container for per-frame visibility toggling
        // Keep "Anchor TL" (Health/silk HUD) AND "Inventory" (tools/crests/items — its #6 managers are now up). The
        // remaining In-game children (Quick Map / Game Map Rendering / DialogueManager / Prompts / Menus / vignettes)
        // still NullRef on Awake without GameMap/QuestManager/etc., so deactivate them before activating HudCamera.
        Transform? inv = null;
        if (inGame != null)
            foreach (Transform c in inGame) {
                if (c.name == "Inventory") {
                    inv = c;
                    continue;
                }

                if (c.name != "Anchor TL") c.gameObject.SetActive(false);
            }

        // Inside Inventory keep only the tools/crests/items panes (Inv + Tools + Border); the Map/Quests/Journal panes
        // need GameMap/QuestManager (map/journal intentionally out of scope — would Awake a wall of NullRefs). Drop them
        // BEFORE Inventory activates (a child set inactive while its parent chain is inactive won't fire Awake), then
        // ensure Inventory itself is active so InventoryPaneList/Inv/Tools run their setup.
        if (inv != null) {
            foreach (Transform p in inv)
                if (p.name is "Map" or "Quests" or "Journal")
                    p.gameObject.SetActive(false);
            inv.gameObject.SetActive(true);
        }

        // Wire UIButtonSkins.ih BEFORE activating the subtree. ih is a one-shot snapshot taken in Start()->SetupRefs()
        // (ih = GameManager.instance.inputHandler); without it every button-prompt lookup logs "Attempting to get button
        // skins before the Input Handler is ready". Activating HudCamera below fires the inventory panes' Awakes, which
        // query button skins — so we must populate ih first (gm.inputHandler is already wired by SilksongBootstrap). The
        // instance lives in the loaded-but-inactive rig; FindObjectsByType(Include) reaches it. SetupRefs only sets
        // ih/active + subscribes an event (deps ready), so it's safe to call directly.
        try {
            var setupRefs = typeof(Silksong::UIButtonSkins)
                .GetMethod("SetupRefs", BindingFlags.Instance | BindingFlags.NonPublic);
            var skins = Object.FindObjectsByType<Silksong::UIButtonSkins>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in skins) setupRefs?.Invoke(s, null);
            Log.Info($"[HUD] wired UIButtonSkins.ih on {skins.Length} instance(s) before HUD activation");
        } catch (Exception e) {
            Log.Error($"[HUD] UIButtonSkins.SetupRefs: {e.Message}");
        }

        hudCam.gameObject.SetActive(true);
        inGame?.gameObject.SetActive(true); // the in-game HUD container (Anchor TL + Inventory)
        FindByName(hudCam, "Hud Canvas")?.gameObject.SetActive(true);
        var layerFixes = RemapSortingLayers(hudCam);

        // Silk meter: the HUD's OWN SilkSpool (Thread/Spool) has the visual refs (capR/seg1/silkChunkPrefab); our
        // bootstrap's BARE SilkSpool (on the GM GO, added for AddUsingSilk) hijacked SilkSpool.Instance, so the real
        // one's Awake returned early ("if (Instance) return") and the meter never drew. Re-point Instance to the real
        // one + DrawSpool now that the HUD is active.
        var realSpool = hudCam.GetComponentInChildren<Silksong::SilkSpool>(true);
        if (realSpool != null) {
            typeof(Silksong::SilkSpool).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetSetMethod(true)?.Invoke(null, [realSpool]);
            try {
                realSpool.DrawSpool();
            } catch (Exception e) {
                Log.Error($"[HUD] DrawSpool: {e.Message}");
            }
        }

        // Fade/slide the HUD content in (health/silk start hidden until the game fires this on scene-ready).
        try {
            Silksong::GameCameras.instance?.HUDIn();
        } catch (Exception e) {
            Log.Error($"[HUD] HUDIn: {e.Message}");
        }

        var uiSet = Silksong::UIManager.instance != null;
        return new {
            ok = true, hudOn = true, camEnabled = cam != null && cam.enabled, uiManager = uiSet, layerFixes,
            silkSpool = realSpool != null
        };
    }

    // Show/hide Hornet's HUD content (the "In-game" container) without re-running the heavy BringUpHud — the HudCamera GO
    // + FSMs stay alive, only the content is toggled. Cheap; safe to call per-frame (only SetActive on change).
    internal static void SetHornetHudVisible(bool on) {
        if (inGame == null) return;
        if (inGame.gameObject.activeSelf != on) inGame.gameObject.SetActive(on);
    }

    // Keep Silksong's neutered main camera on HK's camera. Silksong's AudioEventManager.TryPlayAudioClip culls 3D
    // one-shots whose world position is farther than the prefab's maxDistance from GameCameras.mainCamera — and our
    // rig's camera is disabled + parked at the rig origin, so EVERY hero SFX (dash/slash/attack, spatialBlend≈1) is
    // distance-culled. Parking its TRANSFORM on HK's live camera makes the gate pass; HK's AudioListener still does the
    // actual 3D mix. Cheap (one transform copy/frame); call from the per-frame CameraSwitchDriver.
    internal static void SyncAudioCamera() {
        try {
            if (ssMainCamT == null) {
                var ssGc = Silksong::GameCameras.SilentInstance;
                if (ssGc == null || ssGc.mainCamera == null) return;
                ssMainCamT = ssGc.mainCamera.transform;
            }

            hkGcInstanceField ??=
                typeof(GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (hkGcInstanceField?.GetValue(null) is not GameCameras hk || hk.mainCamera == null) return;
            ssMainCamT.position = hk.mainCamera.transform.position;
        } catch (Exception e) {
            Log.Error($"[HUD] SyncAudioCamera: {e.Message}");
        }
    }

    internal static void SetHkHudVisible(bool on) {
        try {
            hkGcInstanceField ??=
                typeof(GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (hkGcInstanceField?.GetValue(null) is not GameCameras hk || hk.hudCanvas == null) return;

            // Hide/show via localScale, NOT GameObject.SetActive: re-activating hudCanvas re-fires its OnEnable, which
            // re-runs HK's HUD slide-in animation (~1s) — the lag on a Knight<-Hornet switch. Scaling to zero hides the
            // whole HUD instantly (HK's HUD is tk2d MESH-based, so a CanvasGroup alpha wouldn't touch it) while the
            // GameObject stays active (no OnEnable, FSMs untouched). Cache the real scale so we can restore it exactly.
            var t = hk.hudCanvas.transform;
            if (t.localScale != Vector3.zero) hkHudScale = t.localScale; // remember the last non-hidden scale
            if (!hk.hudCanvas.activeSelf) hk.hudCanvas.SetActive(true);
            t.localScale = on ? hkHudScale : Vector3.zero;

            // When hiding (Knight inert), disable all PlayMakerFSM components in the HK HUD subtree. HK HUD FSMs
            // (Soul Orb Control, Spell Control, etc.) call GetHero() -> HeroProxy redirects to Hornet -> they try
            // HK-specific methods (ClearMP) / FSMs (Spell Control) that don't exist on Silksong's HeroController.
            // Disabling the FSMs (not the GO) stops them cleanly without triggering OnEnable's slide-in animation.
            if (on) {
                if (disabledHudFsms != null) {
                    foreach (var fsm in disabledHudFsms) {
                        if (fsm != null) {
                            // Prevent OnEnable from restarting the FSM (RestartOnEnable=true by default resets to
                            // startState and re-runs the full appear chain: Init → Check Type → ... → Idle, 2-3s delay).
                            fsm.Fsm.RestartOnEnable = false;
                            fsm.enabled = true;
                        }
                    }
                    disabledHudFsms = null;
                }
            } else {
                disabledHudFsms ??= new HashSet<PlayMakerFSM>();
                foreach (var fsm in hk.hudCanvas.GetComponentsInChildren<PlayMakerFSM>(true)) {
                    if (fsm.enabled) {
                        fsm.enabled = false;
                        disabledHudFsms.Add(fsm);
                    }
                }
            }
        } catch (Exception e) {
            Log.Error($"[HUD] SetHkHudVisible({on}): {e.Message}");
        }
    }

    internal static void Cleanup() {
        inGame = null;
        // DestroyImmediate (synchronous) so the rig is gone before a hot-reload's Initialize runs Ensure again — a
        // deferred Object.Destroy could leave it alive into the next generation, where FindExistingRig would then reuse
        // a doomed object. Also clear GameCameras._instance (Silksong-assembly static) so it doesn't dangle.
        rig ??= FindExistingRig();
        // Destroy the persisted root (the inactive holder), which owns the rig as a child — not just `rig` itself, or the
        // holder would leak. root == rig in the degenerate case where rig is already a root.
        if (rig != null) {
            Object.DestroyImmediate(rig.transform.root.gameObject);
            rig = null;
        }

        typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
    }
}
