extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Modules;

// Bring HK's lifeblood grant into Hornet's real Silksong blue-health FSM ("Blue Health Control") 
public sealed class LifebloodModule : ModuleBase {
    private const string AddEvent = "ADD BLUE HEALTH";
    private const string SceneReadyEvent = "LAST HP ADDED"; // Wait -> Set Blue -> Add Existing? -> Idle

    private SilksongPM::PlayMakerFSM? fsm;

    public override string Id => "lifeblood";

    public override void Initialize() {
        // TODO: less specific hook?
        Detour(typeof(EventRegister), "SendEvent", OnSendEvent, typeof(string), typeof(GameObject));
        Detour(typeof(ScuttlerControl), "Heal", OnScuttlerHeal);
    }

    protected override void OnDeinitialize() {
        fsm = null;
    }

    // Replay the scene-ready "LAST HP ADDED" so the FSM rests in "Idle" (where it accepts grants), not its post-init
    // "Wait" (where "ADD BLUE HEALTH" is silently ignored). The FSM re-inits to Wait on every HUD (re)activation, and our
    // HUD bring-up never fires the real scene-ready signal.
    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        var blueHealthFsm = BlueHealthFsm();
        if (!blueHealthFsm || !blueHealthFsm.gameObject.activeInHierarchy) return;
        if (blueHealthFsm.ActiveStateName == "Wait") blueHealthFsm.SendEvent(SceneReadyEvent);
    }

    // Relay: HK's ScuttlerControl fires "ADD BLUE HEALTH" on HK's bus; Hornet's FSM is registered for the same event on
    // Silksong's bus, so mirror the signal there and let the FSM grant with its own logic.
    private void OnSendEvent(Action<string, GameObject> orig, string name, GameObject exclude) {
        orig(name, exclude);
        if (name != AddEvent || !HeroSwitch.HornetActive) return;
        try {
            Silksong::EventRegister.SendEvent(AddEvent);
        } catch (Exception e) {
            LogError($"relay: {e.Message}");
        }
    }

    private IEnumerator OnScuttlerHeal(Func<ScuttlerControl, IEnumerator> orig, ScuttlerControl self) {
        return HeroSwitch.HornetActive ? HornetHeal(self) : orig(self);
    }

    // ScuttlerControl.Heal deactivates itself if the Knight is >40 away before granting, but the Knight parks at the
    // switch point while the scuttler homed to Hornet, so the immediate grant is lost and blue only appears on scene
    // exit. Drop the Knight-distance gate: wait, fire "ADD BLUE HEALTH" (relayed above), deactivate.
    private IEnumerator HornetHeal(ScuttlerControl self) {
        yield return new WaitForSeconds(1.2f);
        try {
            EventRegister.SendEvent(AddEvent);
        } catch (Exception e) {
            LogError($"scuttler heal: {e.Message}");
        }

        if (self) self.gameObject.SetActive(false);
    }

    private SilksongPM::PlayMakerFSM? BlueHealthFsm() {
        if (fsm) return fsm;
        var gc = Silksong::GameCameras.SilentInstance;
        if (!gc) return null;
        // TODO: perf
        foreach (var f in gc.gameObject.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true)) {
            if (f.FsmName == "Blue Health Control") {
                fsm = f;
                return fsm;
            }
        }

        return null;
    }
}
