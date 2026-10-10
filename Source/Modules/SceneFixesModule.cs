extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Linq;
using GlobalEnums;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using SFsm = SilksongPM::HutongGames.PlayMaker.Fsm;
using SFsmState = SilksongPM::HutongGames.PlayMaker.FsmState;
using SNonSlider = Silksong::NonSlider;

namespace HornetInHallownest.Modules;

public sealed class SceneFixesModule : ModuleBase {
    public override void Initialize() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        ModHooks.GetPlayerBoolHook += OnGetBool;
        ModHooks.GetPlayerIntHook += OnGetInt;
        // TODO: just hook into thread storm
        Detour(typeof(SFsm), "SwitchState",
            (Action<Action<SFsm, SFsmState?>, SFsm, SFsmState?>)OnSilksongSwitchState, typeof(SFsmState));
        Detour(typeof(SNonSlider), nameof(SNonSlider.IsNonSlider),
            (Func<Func<GameObject, bool>, GameObject, bool>)OnIsNonSlider, typeof(GameObject));
        Detour(typeof(SNonSlider), nameof(SNonSlider.IsNonSlider),
            (Func<Func<Collider2D, bool>, Collider2D, bool>)OnIsNonSlider, typeof(Collider2D));
        Detour(typeof(SNonSlider), nameof(SNonSlider.TryGetNonSlider),
            (TryGetNonSliderHook)OnTryGetNonSlider, typeof(GameObject), typeof(SNonSlider).MakeByRefType());
    }

    public override string Id => "scene-fixes";

    protected override void OnDeinitialize() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        ModHooks.GetPlayerBoolHook -= OnGetBool;
        ModHooks.GetPlayerIntHook -= OnGetInt;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        screamGet = scene.name == "Abyss_12" ? FindScreamGetFsm() : null;
        blizzardRescue = scene.name == "Deepnest_East_12" ? FindBlizzardRescueFsm() : null;
        pathOfPainEnd = scene.name == "White_Palace_20" ? GameObject.Find("End Scene") : null;
        inShamanTemple = scene.name == "Crossroads_ShamanTemple";
        inAbyssCore = scene.name == "Abyss_06_Core";

        switch (scene.name) {
            case "White_Palace_12":
                DisableWhitePalace12Saws(scene);
                break;
            case "Crossroads_ShamanTemple":
                EnsureVengefulSpiritEquipped();
                break;
        }
    }

    // Silksong's wall cling and collision handler ask her NonSlider registry, which HK's scene objects never join: HK's
    // NonSlider is an empty marker class with no registry, and solid HERO_DETECTOR colliders (e.g. the Godhome edges'
    // "End Superdash") are no-wall only to HK's terrain-only rays. Register them into the registry from its own query
    // paths: no scene-wide sweep, and blockers enabled after scene load are covered too.
    private static void TryRegister(GameObject go) {
        if (!go || go.TryGetComponent<SNonSlider>(out _)) return;
        if (!IsHkNonSlider(go)) return;
        go.AddComponent<SNonSlider>();
    }

    // The Knight's cling checks the marker on the hit object itself (raycastHit.collider.GetComponent<NonSlider>), so
    // match that scope.
    private static bool IsHkNonSlider(GameObject go) {
        if (go.GetComponent<NonSlider>()) return true;
        if (go.layer != (int)PhysLayers.HERO_DETECTOR) return false;
        foreach (var collider in go.GetComponents<Collider2D>())
            if (!collider.isTrigger) return true;
        return false;
    }

    private static bool OnIsNonSlider(Func<GameObject, bool> orig, GameObject go) {
        TryRegister(go);
        return orig(go);
    }

    private static bool OnIsNonSlider(Func<Collider2D, bool> orig, Collider2D? collider) {
        if (collider) TryRegister(collider.gameObject);
        return orig(collider!);
    }

    private delegate bool TryGetNonSliderOrig(GameObject go, out SNonSlider nonSlider);
    private delegate bool TryGetNonSliderHook(TryGetNonSliderOrig orig, GameObject go, out SNonSlider nonSlider);

    private static bool OnTryGetNonSlider(TryGetNonSliderOrig orig, GameObject go, out SNonSlider nonSlider) {
        TryRegister(go);
        return orig(go, out nonSlider);
    }

    #region Vengeful Spirit -> Silk Spear equip

    private bool inShamanTemple;

    // VS in Hollow knight is always active, silkspear needs to be equipped to prevent softlock.
    private void EnsureVengefulSpiritEquipped() {
        if (PlayerData.instance.fireballLevel >= 1) {
            EquipSilkSpear();
            return;
        }

        StartCoroutine(WatchForVengefulSpiritGet());
    }

    private IEnumerator WatchForVengefulSpiritGet() {
        while (inShamanTemple && PlayerData.instance != null && PlayerData.instance.fireballLevel < 1)
            yield return null;
        if (inShamanTemple && PlayerData.instance != null && PlayerData.instance.fireballLevel >= 1) EquipSilkSpear();
    }

    private static void EquipSilkSpear() => ToolItemManagerBootstrap.EquipToolByName("Silk Spear");

    #endregion

    #region Abyss shriek (Scream 2)

    private PlayMakerFSM? screamGet;
    private bool holdingForScreamGet;
    private static readonly string[] screamGetFallStates = ["Fall", "Land", "Ui Msg", "Get Up"];

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        HoldThroughScreamGet(hero);
        HoldThroughBlizzardRescue(hero);
        FaceKingAtPathOfPainEnd(hero);
    }

    // The Knight's Spell Control keeps him without input or animation control until the cutscene reaches "End". Hornet's
    // animation control would replace the "Get Up" clip the cutscene waits on, so it never ends.
    // He also hangs without gravity where "Get" places him until "Fall". If she's already grounded on entering "Fall", its
    // landing check fires before the actions that show her again and drop the Knight-skinned stand-in parented to her.
    private void HoldThroughScreamGet(Silksong::HeroController hero) {
        if (!holdingForScreamGet) return;
        if (!screamGet || screamGet.ActiveStateName == "End") {
            holdingForScreamGet = false;
            hero.RegainControl();
            hero.StartAnimationControl();
            return;
        }

        if (!hero.controlReqlinquished || hero.acceptingInput) hero.RelinquishControl();
        if (hero.AnimCtrl.controlEnabled) hero.StopAnimationControl();
        hero.AffectedByGravity(Array.IndexOf(screamGetFallStates, screamGet.ActiveStateName) >= 0);
    }

    private static PlayMakerFSM? FindScreamGetFsm() {
        var go = GameObject.Find("Scream 2 Get");
        if (!go) return null;
        return go.GetComponents<PlayMakerFSM>().FirstOrDefault(fsm => fsm.FsmName == "Scream Get");
    }

    private void OnSilksongSwitchState(Action<SFsm, SFsmState?> orig, SFsm fsm, SFsmState? to) {
        try {
            if (screamGet != null && to is { Name: "Do Sphere" } && fsm.Name == "Silk Specials" &&
                screamGet.ActiveStateName == "In" &&
                PlayerData.instance.screamLevel < 2) {
                LogInfo("Thread Storm cast in Abyss shriek zone -> broadcasting SCREAM GET");
                PlayMakerFSM.BroadcastEvent("SCREAM GET");
                holdingForScreamGet = true;
            }
        } catch (Exception e) {
            LogError($"shriek cutscene: {e.Message}");
        }

        orig(fsm, to);
    }

    #endregion

    #region Blizzard rescue (after King's Brand)

    private PlayMakerFSM? blizzardRescue;
    private bool holdingForBlizzardRescue;

    private static readonly string[] blizzardRescueStates =
        ["Fade Pause", "Fade In", "Burst", "Land", "Leave Antic", "Leave", "Left", "Get Up"];

    // HK's Hornet carries the Knight out of the avalanche and leaves him lying until a button gets him up. Her own animation
    // control would replace the get-up clip the cutscene waits on, so it never ends.
    private void HoldThroughBlizzardRescue(Silksong::HeroController hero) {
        var holding = blizzardRescue && Array.IndexOf(blizzardRescueStates, blizzardRescue!.ActiveStateName) >= 0;
        if (!holding) {
            if (!holdingForBlizzardRescue) return;
            holdingForBlizzardRescue = false;
            hero.RegainControl();
            hero.StartAnimationControl();
            return;
        }

        if (!holdingForBlizzardRescue && blizzardRescue!.GetComponent<tk2dSprite>() is { } rescuer)
            rescuer.color = Color.clear;
        holdingForBlizzardRescue = true;
        if (!hero.controlReqlinquished || hero.acceptingInput) hero.RelinquishControl();
        if (hero.AnimCtrl.controlEnabled) hero.StopAnimationControl();
        if (blizzardRescue!.ActiveStateName != "Get Up" && hero.AnimCtrl.animator.CurrentClip?.name != "Prostrate")
            hero.AnimCtrl.PlayClipForced("Prostrate");
    }

    private static PlayMakerFSM? FindBlizzardRescueFsm() {
        var go = GameObject.Find("Hornet Blizzard Return Scene");
        if (!go) return null;
        return go.GetComponents<PlayMakerFSM>().FirstOrDefault(fsm => fsm.FsmName == "Control");
    }

    #endregion

    #region Path of Pain end

    private GameObject? pathOfPainEnd;

    // The Knight's clip looks up at the King wherever he stands, but the NPC turn before it faces the hero to the middle
    // of the scene, away from the King when she comes from his side.
    private void FaceKingAtPathOfPainEnd(Silksong::HeroController hero) {
        if (!pathOfPainEnd) return;
        var convo = pathOfPainEnd!.GetComponents<PlayMakerFSM>()
            .FirstOrDefault(fsm => fsm.FsmName == "Conversation Control");
        var king = pathOfPainEnd.transform.Find("initial_loop/End Scene King");
        if (!convo || convo.ActiveStateName != "Hero Anim" || !king) return;
        var kingRight = king.position.x > hero.transform.position.x;
        if (kingRight == hero.cState.facingRight) return;
        if (kingRight) hero.FaceRight();
        else hero.FaceLeft();
    }

    #endregion

    #region Abyss_06_Core birthplace floor and lifeblood door

    private const int LifebloodDoorBlueHealth = 15;
    private const int HornetMaxBlueHealth = 9;

    private bool inAbyssCore;

    // Hornet doesn't equip kingsoul, so pretend it is equipped in the abyss scene if we have it
    // royalCharmState 3 = King Soul, 4 = Void Heart
    private bool OnGetBool(string name, bool orig) =>
        orig || inAbyssCore && name == "equippedCharm_36" && HeroSwitch.HornetActive &&
        PlayerData.instance.royalCharmState > 2;

    // The lifeblood door lights a lamp per blue mask up to the Knight's 15. Hornet's blue health stops short of 9: there
    // Silksong turns it into her lifeblood state and empties healthBlue, so only that state opens the door.
    private int OnGetInt(string name, int orig) {
        if (!inAbyssCore || name != "healthBlue" || !HeroSwitch.HornetActive) return orig;
        if (HornetSpawner.Hornet is { IsInLifebloodState: true }) return LifebloodDoorBlueHealth;
        return Silksong::PlayerData.instance.healthBlue * LifebloodDoorBlueHealth / HornetMaxBlueHealth;
    }

    #endregion

    #region White_Palace_12 saws

    private static readonly string[] whitePalace12DisabledSaws = ["wp_saw (18)", "wp_saw (22)", "wp_saw (23)"];

    private void DisableWhitePalace12Saws(Scene scene) {
        try {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (Array.IndexOf(whitePalace12DisabledSaws, root.name) >= 0) {
                    root.SetActive(false);
                    count++;
                }

            LogDebug($"disabled {count}/{whitePalace12DisabledSaws.Length} saws");
        } catch (Exception e) {
            LogError(e.Message);
        }
    }

    #endregion
}
