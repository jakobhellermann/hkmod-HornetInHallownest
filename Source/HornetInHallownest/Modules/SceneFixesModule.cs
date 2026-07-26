extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Linq;
using HornetInHallownest.HornetInHallownest.Core;
using HornetInHallownest.Playground;
using UnityEngine;
using UnityEngine.SceneManagement;
using SFsm = SilksongPM::HutongGames.PlayMaker.Fsm;
using SFsmState = SilksongPM::HutongGames.PlayMaker.FsmState;

namespace HornetInHallownest.HornetInHallownest.Modules;

public sealed class SceneFixesModule : ModuleBase {
    public override void Initialize() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        // TODO: just hook into thread storm
        Detour(typeof(SFsm), "SwitchState",
            (Action<Action<SFsm, SFsmState?>, SFsm, SFsmState?>)OnSilksongSwitchState, typeof(SFsmState));
    }

    public override string Id => "scene-fixes";

    protected override void OnDeinitialize() {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (birthplaceForcedCharm) RestoreBirthplaceCharm();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (birthplaceForcedCharm && scene.name != "Abyss_06_Core") RestoreBirthplaceCharm();
        screamGet = scene.name == "Abyss_12" ? FindScreamGetFsm() : null;
        inShamanTemple = scene.name == "Crossroads_ShamanTemple";

        switch (scene.name) {
            case "Deepnest_Spider_Town":
                FixSpiderTrapBench();
                break;
            case "White_Palace_12":
                DisableWhitePalace12Saws(scene);
                break;
            case "Abyss_06_Core":
                OpenBirthplaceForKingsoulOwner();
                break;
            case "Crossroads_ShamanTemple":
                EnsureVengefulSpiritEquipped();
                break;
            case "Fungus1_04_boss":
                FixDreamerFallGate("Dreamer Scene 1");
                break;
        }
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
        while (inShamanTemple && PlayerData.instance != null && PlayerData.instance.fireballLevel < 1) yield return null;
        if (inShamanTemple && PlayerData.instance != null && PlayerData.instance.fireballLevel >= 1) EquipSilkSpear();
    }

    private static void EquipSilkSpear() => ToolItemManagerBootstrap.EquipToolByName("Silk Spear");

    #endregion

    #region Abyss shriek (Scream 2)

    private PlayMakerFSM? screamGet;

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
            }
        } catch (Exception e) {
            LogError($"shriek cutscene: {e.Message}");
        }

        orig(fsm, to);
    }

    #endregion

    #region Abyss birthplace floor

    private bool birthplaceForcedCharm;
    private bool priorEquippedCharm36;

    // Hornet doesn't equip kingsoul, so pretend it is equipped in the abyss scene if we have it
    // royalCharmState 3 = King Soul, 4 = Void Heart
    private void OpenBirthplaceForKingsoulOwner() {
        var pd = PlayerData.instance;
        if (pd.royalCharmState <= 2 || pd.equippedCharm_36) return;
        priorEquippedCharm36 = pd.equippedCharm_36;
        pd.equippedCharm_36 = true;
        birthplaceForcedCharm = true;
    }

    private void RestoreBirthplaceCharm() {
        birthplaceForcedCharm = false;
        var pd = PlayerData.instance;
        pd.equippedCharm_36 = priorEquippedCharm36;
    }

    #endregion

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

    #region Knight-calibrated fall-land gates

    // A class of HK cutscene FSM: relinquish control, drop the hero, then gate the landing on a FloatCompare of the
    // hero's Y against a baked target Y (equal/lessThan -> FINISHED, greaterThan has no transition). The target is
    // calibrated to where the Knight's origin rests on that floor. Hornet rests ~0.16 higher (deeper collider feet),
    // so her Y never reaches it -> the compare never fires -> the FSM hangs no-input and control is never returned.
    // Raise the baked target into Hornet's frame by the live collider feet-delta; the gate then fires as she lands.
    private void RaiseHeroLandGate(GameObject? go, string fsmName, string varName) {
        try {
            if (go == null) return;

            PlayMakerFSM? fsm = null;
            foreach (var f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == fsmName) {
                    fsm = f;
                    break;
                }

            var landY = fsm?.FsmVariables.FindFsmFloat(varName);
            if (landY == null) return;

            var delta = FeetDelta();
            if (delta <= 0f) return; // colliders not ready, or no overshoot to correct

            var before = landY.Value;
            landY.Value = before + delta;
            LogDebug($"raised '{varName}' {before} -> {landY.Value} (+{delta} feet-delta) on {go.name}/{fsmName}");
        } catch (Exception e) {
            LogError(e.Message);
        }
    }

    // Trap bench `Fade` FSM: Fall -> gate `Hero Y <= Hero Land Y` -> Land -> Relinquish Control (SetBenchRespawn,
    // SaveGame). TODO(unverified): numbers confirmed live, but the softlock -> fixed round-trip isn't verified
    // end-to-end (the trap is a one-time spiderCapture and the test save is past it). Re-verify on a fresh save.
    private void FixSpiderTrapBench() => RaiseHeroLandGate(GameObject.Find("RestBench Spider"), "Fade", "Hero Land Y");

    // Dreamer cutscene `Control` FSM: Blast -> gate `Hero Y <= Knight Scene Y` (Hero Fall) -> Land -> ... -> End
    // (RegainControl). Without the raise, Hero Fall hangs no-input (verified: raising the gate live runs it to End).
    private void FixDreamerFallGate(string sceneObjectName) =>
        RaiseHeroLandGate(GameObject.Find(sceneObjectName), "Control", "Knight Scene Y");

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
