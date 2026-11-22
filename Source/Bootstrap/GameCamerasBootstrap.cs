extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections.Generic;
using HornetInHallownest.Util;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using HornetInHallownest.Modules;
using HornetInHallownest.Core;

namespace HornetInHallownest.Bootstrap;

// Silksong's `_GameCameras` rig, for GameCameras.instance, its CameraTarget and Hornet's HUD. HK renders and owns the
// camera, so the rig's cameras, listener and controller stay disabled.
internal static class GameCamerasBootstrap {
    private const string RigName = "Silksong_GameCameras";
    private static GameObject? rig;

    private static Transform? inGame;

    // Silksong sorting layers whose ids differ from HK's same-named ones; the rest fall back to "HUD".
    private static readonly Dictionary<int, string> ssSortingLayerNames = new() {
        {
            59515797, "Inventory"
        }, {
            1017658613, "Scene Border"
        }
    };

    // Personal taste, this feels most in line with hollowknight when switching
    private const float BaseHudScale = 0.9f;

    private static Transform? hudOffsetTransform;
    private static Silksong::HudGlobalHide? hudGlobalHide;
    private static Vector3 hudCanvasPos;
    private static SilksongPM::PlayMakerFSM? hudSlideFsm;
    private static Vector3 hkHudScale = Vector3.one;
    private static HashSet<PlayMakerFSM>? disabledHudFsms;
    private static Transform? ssMainCamT;

    // Silksong's CameraTarget GO (on the rig). Hero FSMs reference "Camera Target" via a serialized FsmGameObject whose
    // cross-game PPtr is lost -> they'd GameObject.Find and hit HK's same-named object (no SetSprint). Rewire to this.
    internal static GameObject? CameraTargetGo =>
        rig != null ? rig.GetComponentInChildren<Silksong::CameraTarget>(true)?.gameObject : null;

    internal static GameObject? HudCameraGo =>
        rig != null ? FindByName(rig.transform, "HudCamera")?.gameObject : null;

    private static bool HornetHudReady => inGame != null;

    // HUD elements reset while In-game is deactivated by a hero switch.
    internal static event Action? HornetHudShown;

    // The DontDestroyOnLoad rig survives a hot-reload, our static doesn't.
    private static GameObject? FindExistingRig() {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == RigName && go.scene.IsValid())
                return go;
        return null;
    }

    internal static void Ensure() {
        rig ??= FindExistingRig();
        if (rig != null) {
            Silksong::GameCameras._instance = rig.GetComponentInChildren<Silksong::GameCameras>(true);
            return;
        }

        SilksongAddressables.EnsureMounted();
        var prefab = Addressables.LoadAssetAsync<GameObject>("_GameCameras").WaitForCompletion();

        // Instantiate inactive to disable HK-owned parts before any Awake, then activate so the HUD FSMs run their Awake.
        var holder = new GameObject("hp_gc_holder");
        holder.SetActive(false);
        var inst = Object.Instantiate(prefab, holder.transform);
        inst.name = RigName;

        foreach (var c in inst.GetComponentsInChildren<Camera>(true)) c.enabled = false;
        // Unity allows one AudioListener, HK's.
        foreach (var a in inst.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
        foreach (var cc in inst.GetComponentsInChildren<Silksong::CameraController>(true)) cc.enabled = false;
        // The blurs NullRef every frame without the scene's BlurPlanes.
        foreach (var b in inst.GetComponentsInChildren<Silksong::BlurManager>(true)) b.enabled = false;
        foreach (var b in inst.GetComponentsInChildren<Silksong::LightBlurredBackground>(true)) b.enabled = false;
        // Full-screen overlay for Silksong's scene Remaskers.
        foreach (var m in inst.GetComponentsInChildren<Silksong::MaskerBlackout>(true)) m.gameObject.SetActive(false);

#if HK1578
        // Their shaders are unsupported on Unity 2020: OnEnable disables them again and OnDisable NullRefs.
        foreach (var e in inst.GetComponentsInChildren<Silksong::UnityStandardAssets.ImageEffects.PostEffectsBase>(true))
            e.enabled = false;
#endif

        UIManagerBootstrap.Ensure();

        // GameCameras.Awake is skipped (Stub), and the HUD FSMs read the instance in theirs.
        var gc = inst.GetComponentInChildren<Silksong::GameCameras>(true);
        Silksong::GameCameras._instance = gc;
        Silksong::GameManager.instance.gameCams = gc;

        Object.DontDestroyOnLoad(holder);
        rig = inst;
        holder.SetActive(true);
    }

    private static void RemapSortingLayers(Transform root) {
        foreach (var mso in root.GetComponentsInChildren<Silksong::MeshSortingOrder>(true)) {
            var id = mso.layerID;
            if (SortingLayer.IsValid(id)) continue;
            var hkId = SortingLayer.NameToID(ssSortingLayerNames.TryGetValue(id, out var nm) ? nm : "HUD");
            mso.layerID = hkId;
            mso.GetComponent<MeshRenderer>().sortingLayerID = hkId;
        }
    }

    private static Transform? FindByName(Transform root, string name) {
        if (root.name == name) return root;
        foreach (Transform c in root) {
            var r = FindByName(c, name);
            if (r != null) return r;
        }

        return null;
    }

    // The rig's HudCamera stays disabled: HK's HudCamera renders the HUD's UI-layer meshes.
    internal static void BringUpHud() {
        rig ??= FindExistingRig();
        var hudCam = FindByName(rig!.transform, "HudCamera")!;

        // In-game also holds panes (Quick Map, DialogueManager, Prompts, Menus) that NullRef on Awake without their
        // managers; deactivated while still inside the inactive HudCamera, they never Awake.
        inGame = FindByName(hudCam, "In-game")!;
        Transform? inv = null;
        foreach (Transform c in inGame) {
            if (c.name == "Inventory") {
                inv = c;
                continue;
            }

            // Screen Fader: hazard respawn sends "HAZARD RESPAWN" to it to hide the hero during the get-up anim.
            // Status Vignette: the Beast Crest's rage tints the screen edges red through it.
            if (c.name is "Anchor TL" or "Screen Fader" or "Status Vignette") continue;

            c.gameObject.SetActive(false);
        }

        // AreaTitleInitialiser re-activates the deactivated Area Title until it initialised, flashing "The fading town".
        inGame.GetComponent<Silksong::AreaTitleInitialiser>().enabled = false;

        // These panes need GameMap/QuestManager.
        foreach (Transform p in inv!)
            if (p.name is "Map" or "Quests" or "Journal")
                p.gameObject.SetActive(false);
        inv.gameObject.SetActive(true);

        // UIButtonSkins.ih is set in their Start, after the inventory panes' Awakes read it.
        foreach (var skins in UnityApi.FindAll<Silksong::UIButtonSkins>(true)) skins.SetupRefs();

        hudCam.gameObject.SetActive(true);
        inGame.gameObject.SetActive(true);
        FindByName(hudCam, "Hud Canvas")!.gameObject.SetActive(true);

        // Shrink via Hud Canvas Offset: Hud Canvas' scale belongs to its slide FSM, Anchor TL's position to
        // ForceCameraAspect. The offset keeps Hud Canvas' origin in place, so it shrinks towards its top-left content.
        var hudOffset = FindByName(hudCam, "Hud Canvas Offset")!;
        var hudCanvas = FindByName(hudOffset, "Hud Canvas")!;
        hudOffsetTransform = hudOffset;
        hudSlideFsm = SilksongPM::PlayMakerFSM.FindFsmOnGameObject(hudCanvas.gameObject, "Slide Out");
        hudGlobalHide = hudOffset.GetComponent<Silksong::HudGlobalHide>();
        hudCanvasPos = hudCanvas.localPosition;
#if HK1578
        ApplyHudScale();
#else
        var hudSetting = GameManager.instance.gameSettings.hudScaleSetting;
        SetHudAppearance(hudSetting == 2, hudSetting == 1);
#endif

        RemapSortingLayers(hudCam);

        // Some HUD objects' tag index is HK's "Boss", which HK FSMs count (e.g. Collector never drops jars).
        foreach (var t in hudCam.GetComponentsInChildren<Transform>(true))
            if (t.CompareTag("Boss"))
                t.gameObject.tag = "Untagged";

        // The bootstrap's bare SilkSpool took SilkSpool.Instance, so the HUD's own spool skipped its Awake.
        var realSpool = hudCam.GetComponentInChildren<Silksong::SilkSpool>(true);
        Silksong::SilkSpool.Instance = realSpool;
        realSpool.DrawSpool();

        Silksong::GameCameras.instance.HUDIn();

        // A reused rig still shows the previous life's masks.
        HudRefresh.ResetHealthHud();
    }

    internal static void SetHudAppearance(bool hidden, bool reduced) {
        Silksong::HudGlobalHide.IsHidden = hidden;
        Silksong::HudGlobalHide.IsReduced = reduced;
        Silksong::HudScalePositioner.IsReduced = reduced;
        ApplyHudScale();
    }

    // HudGlobalHide sets Hud Canvas Offset to Silksong's size on every HUD show; the shrink keeps Hud Canvas' origin.
    private static void ApplyHudScale() {
        // HudGlobalHide parks a hidden HUD below the screen.
        if (!hudOffsetTransform || Silksong::HudGlobalHide.IsHidden) return;
        var reduced = Silksong::HudGlobalHide.IsReduced;
        var scale = reduced ? hudGlobalHide!.reducedScale.x : 1f;
        var pos = reduced ? (Vector3)hudGlobalHide!.reducedPos : Vector3.zero;
        hudOffsetTransform.localScale = Vector3.one * (BaseHudScale * scale);
        hudOffsetTransform.localPosition = pos + hudCanvasPos * (scale * (1f - BaseHudScale));
    }

    internal static void SetHornetHudVisible(bool on) {
        if (inGame == null || inGame.gameObject.activeSelf == on) return;
        inGame.gameObject.SetActive(on);
        if (on) {
            ApplyHudScale();
            HudRefresh.RefreshMaxSilkHud();
            HudRefresh.RefreshBindOrbFrame();
            ModuleBase.Runtime.StartCoroutine(HudRefresh.RelistenLayouts(inGame));
            HornetHudShown?.Invoke();
        }
    }

    internal static void SendHornetHudSlideEvent(string eventName) {
        if (hudSlideFsm) hudSlideFsm.SendEvent(eventName);
    }

    // HK re-grabs its camera target and HUD for the Knight on scene entry.
    internal static void SyncToActiveHero() {
        RetargetCamera(HeroSwitch.TransformOf(HeroSwitch.ActiveHeroGameObject));
        SyncMainCameraToHk();
        if (HornetHudReady) {
            SetHornetHudVisible(HeroSwitch.HornetActive);
            SetHkHudVisible(!HeroSwitch.HornetActive);
        }
        else {
            SetHkHudVisible(true);
        }
    }

    internal static void RetargetCamera(Transform? t) {
        if (!t) return;
        var gc = GameCameras.instance;
        var camTarget = gc ? gc.cameraTarget : null;
        if (!camTarget) return;
        camTarget.heroTransform = t;
    }

    // Not parented under HK's camera: HK rebuilds it on scene load and would destroy the rig camera with it.
    private static void SyncMainCameraToHk() {
        if (ssMainCamT == null) {
            var ssGc = Silksong::GameCameras.SilentInstance;
            if (ssGc == null || ssGc.mainCamera == null) return;
            ssMainCamT = ssGc.mainCamera.transform;
        }

        if (!ssMainCamT.GetComponent<MainCamAudioFollow>())
            ssMainCamT.gameObject.AddComponent<MainCamAudioFollow>();
        if (!ssMainCamT.GetComponent<MainCamShakeRelay>())
            ssMainCamT.gameObject.AddComponent<MainCamShakeRelay>();
    }

    // Silksong's audio culls and muffles hero sounds by their distance to GameCameras.mainCamera.
    private sealed class MainCamAudioFollow : MonoBehaviour {
        private Transform? hkCamT;

        private void LateUpdate() {
            if (!hkCamT) {
                if (GameCameras._instance is not { } hk || !hk.mainCamera) return;
                hkCamT = hk.mainCamera.transform;
            }

            transform.position = hkCamT.position;
        }
    }

    // Silksong shakes the rig's camera, which never renders: its CameraShake FSM moves the rig's CameraParent and
    // CameraShakeManager offsets the main camera at render time. Carry both onto HK's main camera for each frame's render,
    // and take them off again before HK's CameraController moves it in LateUpdate.
    private sealed class MainCamShakeRelay : MonoBehaviour {
        private Silksong::CameraShakeManager? shakeManager;
        private Vector3 parentRest;
        private Transform? hkCamT;
        private Vector3 appliedOffset;
        private bool isApplied;

        private void Awake() {
            shakeManager = GetComponent<Silksong::CameraShakeManager>();
            parentRest = transform.parent ? transform.parent.localPosition : Vector3.zero;
        }

        private void OnEnable() => Camera.onPreCull += OnCameraPreCull;

        private void OnDisable() {
            Camera.onPreCull -= OnCameraPreCull;
            RemoveOffset();
        }

        private void Update() => RemoveOffset();

        private void OnCameraPreCull(Camera cam) {
            if (isApplied || !ResolveHkCamera() || !cam.transform.IsChildOf(hkCamT!)) return;
            // apply HK camera shake multiplier
            appliedOffset = ((transform.parent ? transform.parent.localPosition - parentRest : Vector3.zero) + (shakeManager ? shakeManager.currentOffset : Vector3.zero)) * ConfigManager.CameraShakeMultiplier;
            hkCamT!.localPosition += appliedOffset;
            isApplied = true;
        }

        private void RemoveOffset() {
            if (!isApplied) return;
            if (hkCamT) hkCamT.localPosition -= appliedOffset;
            isApplied = false;
        }

        private bool ResolveHkCamera() {
            if (hkCamT) return true;
            if (GameCameras._instance is not { } hk || !hk.mainCamera) return false;
            hkCamT = hk.mainCamera.transform;
            return true;
        }
    }

    internal static void SetHkHudVisible(bool on) {
        if (GameCameras._instance is not { } hk || !hk.hudCanvas) return;

        // Scaled to zero instead of deactivated: re-activating replays HK's ~1s HUD slide-in.
        var t = hk.hudCanvas.transform;
        if (t.localScale != Vector3.zero) hkHudScale = t.localScale;
        if (!hk.hudCanvas.activeSelf) hk.hudCanvas.SetActive(true);
        t.localScale = on ? hkHudScale : Vector3.zero;

        // HK's HUD FSMs get Hornet as the hero and call HK-only methods (ClearMP) and FSMs (Spell Control) on her.
        if (on) {
            if (disabledHudFsms != null) {
                foreach (var fsm in disabledHudFsms)
                    if (fsm != null) {
                        // A restart replays the masks' 2-3s appear.
                        fsm.Fsm.RestartOnEnable = false;
                        fsm.enabled = true;
                    }

                disabledHudFsms = null;
            }
        }
        else {
            disabledHudFsms ??= new HashSet<PlayMakerFSM>();
            // Hornet's essence counter shows and hides on the states of HK's.
            foreach (var fsm in hk.hudCanvas.GetComponentsInChildren<PlayMakerFSM>(true))
                if (fsm.enabled && !EssenceCounterModule.IsHkCounter(fsm)) {
                    fsm.enabled = false;
                    disabledHudFsms.Add(fsm);
                }
        }
    }

    internal static void Cleanup() {
        inGame = null;
        hudSlideFsm = null;
        // Immediate, or a hot-reload's Ensure would reuse the doomed rig.
        rig ??= FindExistingRig();
        if (rig != null) {
            Object.DestroyImmediate(rig.transform.root.gameObject);
            rig = null;
        }

        Silksong::GameCameras._instance = null;
    }
}
