extern alias Silksong;
extern alias SilksongPM;
using System;
using HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Modules;

// Bring HK's lifeblood grant into Hornet's real Silksong blue-health FSM ("Blue Health Control") 
public sealed class LifebloodModule : ModuleBase {
    private const string AddEvent = "ADD BLUE HEALTH";
    private const string SceneReadyEvent = "LAST HP ADDED"; // Wait -> Set Blue -> Add Existing? -> Idle

    private SilksongPM::PlayMakerFSM? fsm;

    public override string Id => "lifeblood";

    public override void Initialize() {
        // TODO: less specific hook?
#if HK1578
        Detour(typeof(EventRegister), "SendEvent", OnSendEvent, typeof(string));
#else
        Detour(typeof(EventRegister), "SendEvent", OnSendEvent, typeof(string), typeof(GameObject));
#endif
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
#if HK1578
    private void OnSendEvent(Action<string> orig, string name) {
        orig(name);
        Relay(name);
    }
#else
    private void OnSendEvent(Action<string, GameObject> orig, string name, GameObject exclude) {
        orig(name, exclude);
        Relay(name);
    }
#endif

    private void Relay(string name) {
        if (name != AddEvent || !HeroSwitch.HornetActive) return;
        try {
            Silksong::EventRegister.SendEvent(AddEvent);
        } catch (Exception e) {
            LogError($"relay: {e.Message}");
        }
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
