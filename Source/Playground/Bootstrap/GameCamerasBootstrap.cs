extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Bring up Silksong's `_GameCameras` rig (via Addressables) enough for GameCameras.instance + a live CameraTarget, so
// the "Couldn't find GameCameras" / "Failed to find camera target" / SetSprint errors stop. HK renders the world, so we
// neuter the rig: disable every Camera (HK renders), AudioListener (Unity allows one; HK owns it), CameraController
// (fights HK + NullRefs). The HUD in this same rig is brought up separately (BringUpHud); CameraTarget stays live.
internal static class GameCamerasBootstrap {
    private const string RigName = "Silksong_GameCameras";
    private static GameObject? rig;

    private static Transform?
        inGame; // Hornet HUD content container ("In-game"), cached by BringUpHud for cheap toggling

    // Fixes the per-frame "Invalid layer id" flood: some Silksong HUD MeshSortingOrder layerIDs ("Inventory"/"Scene
    // Border") have different uniqueIDs than HK's, so HK rejects them and OnUpdate re-applies every frame. Remap the
    // invalid ones to HK's by-name equivalent ("HUD" fallback).
    private static readonly Dictionary<int, string> SsSortingLayerNames = new() {
        { 59515797, "Inventory" }, { 1017658613, "Scene Border" }
    };

    // HK's GameCameras._instance field (not the `instance` getter — it logs "Couldn't find GameCameras" when null,
    // and this runs per-frame via HeroSwitch -> spam). global::GameCameras is HK's (unprefixed).
    private static FieldInfo? hkGcInstanceField;
    private static Vector3 hkHudScale = Vector3.one; // HK hudCanvas' real scale, cached so we can restore after hiding
    private static HashSet<PlayMakerFSM>? disabledHudFsms; // HK HUD FSMs we disabled when Knight is inert

    private static Transform?
        ssMainCamT; // Silksong rig's (neutered) main camera transform, kept on HK's camera for audio

    // Silksong's CameraTarget GO (on the rig). Hero FSMs reference "Camera Target" via a serialized FsmGameObject whose
    // cross-game PPtr is lost -> they'd GameObject.Find and hit HK's same-named object (no SetSprint). Rewire to this.
    internal static GameObject? CameraTargetGo =>
        rig != null ? rig.GetComponentInChildren<Silksong::CameraTarget>(true)?.gameObject : null;

    internal static GameObject? HudCameraGo =>
        rig != null ? FindByName(rig.transform, "HudCamera")?.gameObject : null;

    internal static bool HornetHudReady => inGame != null;

    // Fires when Hornet's in-game HUD transitions to visible. Consumers re-trigger HUD elements that reset while their
    // GameObject is deactivated (a hero switch deactivates the whole In-game GO).
    internal static event Action? HornetHudShown;

    // Hot-reload safe: the DDOL rig survives our DLL reload (our `rig` static doesn't) — reuse it, found by its name.
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

            SilksongCatalog.EnsureMounted();
            var prefab = Addressables.LoadAssetAsync<GameObject>("_GameCameras").WaitForCompletion();
            if (prefab == null) return new { error = "_GameCameras load returned null" };

            // Instantiate under an inactive holder so we can neuter components before any Awake fires, then activate it
            // so every HUD FSM (health_display, silk, …) self-initializes via its real Awake/Start (a dormant rig left
            // them un-started -> health masks' renderers never enabled).
            var holder = new GameObject("hp_gc_holder");
            holder.SetActive(false);
            var inst = Object.Instantiate(prefab, holder.transform);
            inst.name = RigName;

            // Neuter HK-owned parts before activation (so their Update never runs). Disable, don't destroy, so the rig's
            // serialized refs stay non-null.
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

            // UIManager singleton the HUD FSMs deref during Awake/Start, before activation (PlayerData/GlobalSettings
            // already seeded earlier in SpawnReal).
            UIManagerBootstrap.Ensure();

            // Set GameCameras._instance before activation so child HUD FSMs resolve GameCameras.instance in their Awake.
            // (GameCameras.Awake is skipped in Stub; we DDOL the holder ourselves below.)
            var gc = inst.GetComponentInChildren<Silksong::GameCameras>(true);
            if (gc != null) {
                typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.SetValue(null, gc);
                // gm.gameCams (set by the skipped SetupGameRefs) -> derefed by inventory pause (StopCameraShake), HUD, etc.
                if (Silksong::GameManager.instance != null) Silksong::GameManager.instance.gameCams = gc;
            }

            Object.DontDestroyOnLoad(holder);
            rig = inst;
            holder.SetActive(true); // activate -> Awakes/Starts run (GameCameras.Awake/Start skipped in Stub)

            var instanceSet = Silksong::GameCameras.SilentInstance != null;
            Log.Debug(
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

    // Slide Silksong's HUD in (the rig is already active from Ensure): activate the HUD containers, fix sorting layers,
    // re-point SilkSpool, call HUDIn. The rig's HudCamera stays disabled — HK's HudCamera renders the UI-layer meshes.
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

        // HudCamera is inactive in the prefab (StartScene activates it; we skip that), so its HUD subtree + FSMs never
        // Awoke. Activate the GO to self-init the FSMs, but keep the Camera component off (HK's HudCamera renders).
        if (cam != null) cam.enabled = false;
        // Under "In-game" sit far more than the HUD: Inventory / Quick Map / DialogueManager / Prompts / Menus panes
        // that NullRef on Awake without their managers. Keep only "Anchor TL" (Health/silk) + "Inventory" (its managers
        // are up) + "Screen Fader"; deactivate the rest before activating HudCamera (inactive-in-inactive won't Awake).
        inGame = FindByName(hudCam, "In-game"); // cached for per-frame visibility toggling
        Transform? inv = null;
        if (inGame != null)
            foreach (Transform c in inGame) {
                if (c.name == "Inventory") {
                    inv = c;
                    continue;
                }

                // Screen Fader: hazard respawn sends "HAZARD RESPAWN" to it to hide the hero during the get-up anim.
                if (c.name is "Anchor TL" or "Screen Fader") continue;

                c.gameObject.SetActive(false);
            }

        // Inside Inventory, drop the Map/Quests/Journal panes (need GameMap/QuestManager, out of scope) before it
        // activates, then activate Inventory so its tools/crests/items panes run their setup.
        if (inv != null) {
            foreach (Transform p in inv)
                if (p.name is "Map" or "Quests" or "Journal")
                    p.gameObject.SetActive(false);
            inv.gameObject.SetActive(true);
        }

        // Wire UIButtonSkins.ih (a one-shot snapshot from Start->SetupRefs) before activation, else the inventory panes'
        // Awakes log "...button skins before the Input Handler is ready". Call SetupRefs directly on the inactive rig's
        // instances (gm.inputHandler is already wired; SetupRefs only sets ih + subscribes an event).
        try {
            var setupRefs = typeof(Silksong::UIButtonSkins)
                .GetMethod("SetupRefs", BindingFlags.Instance | BindingFlags.NonPublic);
            var skins = Object.FindObjectsByType<Silksong::UIButtonSkins>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in skins) setupRefs?.Invoke(s, null);
            Log.Debug($"[HUD] wired UIButtonSkins.ih on {skins.Length} instance(s) before HUD activation");
        } catch (Exception e) {
            Log.Error($"[HUD] UIButtonSkins.SetupRefs: {e.Message}");
        }

        hudCam.gameObject.SetActive(true);
        inGame?.gameObject.SetActive(true); // the in-game HUD container (Anchor TL + Inventory)
        FindByName(hudCam, "Hud Canvas")?.gameObject.SetActive(true);
        var layerFixes = RemapSortingLayers(hudCam);

        // Cross-game tag-index collision: some Silksong HUD GOs carry a tag index that resolves to HK's "Boss" tag, so
        // HK FSMs gating on GetTagCount("Boss") miscount the HUD as bosses (e.g. Collector never drops jars). Clear it.
        var untagged = 0;
        foreach (var t in hudCam.GetComponentsInChildren<Transform>(true))
            if (t.CompareTag("Boss")) {
                t.gameObject.tag = "Untagged";
                untagged++;
            }

        if (untagged > 0) Log.Debug($"[HUD] cleared bogus 'Boss' tag off {untagged} HUD object(s) (tag-index collision)");

        // The bootstrap's bare SilkSpool (added for AddUsingSilk) hijacked SilkSpool.Instance, so the HUD's real spool
        // (with the visual refs) Awoke early-return and never drew. Re-point Instance to it + DrawSpool now.
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

        // Re-sync masks from PlayerData: on a reused (DDOL) rig the mask FSMs still show the previous life's count/fill,
        // so force them back through Init to re-read maxHealth/health. Only runs on spawn, so no repeated appear anim.
        BundleSpike.ResetHealthHud();

        var uiSet = Silksong::UIManager.instance != null;
        return new {
            ok = true, hudOn = true, camEnabled = cam != null && cam.enabled, uiManager = uiSet, layerFixes,
            silkSpool = realSpool != null
        };
    }

    // Toggle Hornet's HUD content ("In-game") without re-running BringUpHud. Cheap; SetActive only on change.
    internal static void SetHornetHudVisible(bool on) {
        if (inGame == null || inGame.gameObject.activeSelf == on) return;
        inGame.gameObject.SetActive(on);
        if (on) HornetHudShown?.Invoke();
    }

    // Park Silksong's neutered main camera transform on HK's live camera: AudioEventManager distance-culls 3D one-shots
    // by distance from GameCameras.mainCamera, so a rig camera parked at origin culls every hero SFX. HK's
    // AudioListener still does the mix. Cheap (one transform copy/frame); call from CameraSwitchDriver.
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

            // Hide via localScale, not SetActive: re-activating hudCanvas re-fires OnEnable -> HK's ~1s HUD slide-in
            // (the switch lag). Scaling to zero hides it instantly (tk2d mesh-based, so CanvasGroup alpha wouldn't work)
            // with the GO staying active. Cache the real scale to restore it.
            var t = hk.hudCanvas.transform;
            if (t.localScale != Vector3.zero) hkHudScale = t.localScale; // remember the last non-hidden scale
            if (!hk.hudCanvas.activeSelf) hk.hudCanvas.SetActive(true);
            t.localScale = on ? hkHudScale : Vector3.zero;

            // When hiding (Knight inert), disable the HK HUD subtree's FSMs: they GetHero() -> HeroProxy redirects to
            // Hornet -> they call HK-only methods (ClearMP) / FSMs (Spell Control) absent on Silksong's HeroController.
            if (on) {
                if (disabledHudFsms != null) {
                    foreach (var fsm in disabledHudFsms)
                        if (fsm != null) {
                            // Prevent OnEnable restarting the FSM (RestartOnEnable resets to startState -> 2-3s re-appear).
                            fsm.Fsm.RestartOnEnable = false;
                            fsm.enabled = true;
                        }

                    disabledHudFsms = null;
                }
            }
            else {
                disabledHudFsms ??= new HashSet<PlayMakerFSM>();
                foreach (var fsm in hk.hudCanvas.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (fsm.enabled) {
                        fsm.enabled = false;
                        disabledHudFsms.Add(fsm);
                    }
            }
        } catch (Exception e) {
            Log.Error($"[HUD] SetHkHudVisible({on}): {e.Message}");
        }
    }

    internal static void Cleanup() {
        inGame = null;
        // DestroyImmediate (synchronous) so the rig is gone before a hot-reload's Initialize runs Ensure again (a
        // deferred Destroy would let FindExistingRig reuse a doomed object).
        rig ??= FindExistingRig();
        // Destroy the persisted root (the holder that owns the rig), not just `rig`, or the holder leaks.
        if (rig != null) {
            Object.DestroyImmediate(rig.transform.root.gameObject);
            rig = null;
        }

        typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
    }
}
