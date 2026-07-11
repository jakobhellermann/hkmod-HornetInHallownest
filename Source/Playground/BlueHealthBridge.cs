extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// Bring HK's real lifeblood grant into Hornet's real Silksong blue-health system through the real event bus -- no
// hand-rolled queue/drain/prime/direct-poke. Two pieces, both verified with /blue-fire-* against the live FSM:
//
// (1) RELAY: HK's ScuttlerControl fires EventRegister.SendEvent("ADD BLUE HEALTH") on HK's bus. Hornet's "Blue Health
//     Control" FSM is natively registered on SILKSONG's EventRegister for the same event, so we simply mirror the
//     signal onto Silksong's bus -- the FSM then grants with its OWN logic (increments PlayerData.healthBlue, spawns
//     the mask). No finding/poking the FSM ourselves. (Verified: an SS-bus "ADD BLUE HEALTH" grants healthBlue 0->1.)
//
// (2) SCENE-READY INIT (KeepReady, per-frame while Hornet's HUD is up): the FSM runs Init -> "Wait" and only reaches
//     "Idle" -- the state where it accepts "ADD BLUE HEALTH" -- on the game's scene-ready "LAST HP ADDED", which our
//     HUD bring-up never fires. It also re-runs Init -> Wait every time the HUD reactivates (Knight<->Hornet switch,
//     scene load). So we replay that scene-ready signal whenever the FSM is resting in "Wait", bringing it to "Idle".
//     In "Wait" it silently ignores "ADD BLUE HEALTH" (verified: healthBlue stays 0), so the grant would be lost
//     otherwise. Same "backfill the scene-ready step the un-run environment would have done" pattern as the red-mask
//     bindCutscenePlayed fix; cached FSM + a cheap ActiveStateName compare (cf. NeedolinDreamNail.Tick).
internal static class BlueHealthBridge {
    private const string AddEvent = "ADD BLUE HEALTH";
    private const string SceneReadyEvent = "LAST HP ADDED"; // Wait -> Set Blue -> Add Existing? -> Idle
    private static Hook? hook;
    private static Hook? healHook;
    private static SilksongPM::PlayMakerFSM? fsm;

    internal static void Install() {
        if (hook != null) return;
        var mi = typeof(EventRegister).GetMethod("SendEvent", BindingFlags.Public | BindingFlags.Static, null,
            [typeof(string), typeof(GameObject)], null);
        if (mi == null) {
            Log.Error("[BlueHealthBridge] EventRegister.SendEvent(string,GameObject) not found");
            return;
        }

        hook = new Hook(mi, (Action<Action<string, GameObject>, string, GameObject>)((orig, name, exclude) => {
            orig(name, exclude); // let HK dispatch too (its own Blue Health Control is disabled while Hornet is active)
            if (name != AddEvent || !HeroSwitch.HornetActive) return;
            try {
                Silksong::EventRegister.SendEvent(AddEvent); // mirror onto Silksong's bus; Hornet's FSM grants natively
            } catch (Exception e) {
                Log.Error($"[BlueHealthBridge] relay: {e.Message}");
            }
        }));
        Log.Debug("[BlueHealthBridge] installed: HK 'ADD BLUE HEALTH' -> Silksong EventRegister bus");

        // Also fix the UPSTREAM: ScuttlerControl.Heal() fires "ADD BLUE HEALTH" 1.2s after a scuttler reaches the hero,
        // but FIRST does `if (Distance(scuttler, HeroController.instance /*=the Knight*/) > 40) SetActive(false)`. The
        // scuttler already homed to Hornet (its "Hero" var = FindWithTag("Player") -> Hornet), so it IS near her -- but
        // the Knight parks at the switch point and doesn't follow, so that distance is usually >40, the scuttler
        // deactivates, its immediate grant never runs, and only the UnloadingLevel fallback fires -> blue appears on
        // SCENE EXIT. While Hornet is active, run a version that drops the Knight-distance deactivation (and the now-
        // redundant UnloadingLevel fallback): wait 1.2s, fire "ADD BLUE HEALTH" (our relay above mirrors it to Hornet's
        // bus), deactivate. Skips orig, so no double-fire.
        var hi = typeof(ScuttlerControl).GetMethod("Heal", BindingFlags.Instance | BindingFlags.NonPublic);
        if (hi != null)
            healHook = new Hook(hi, (Func<Func<ScuttlerControl, IEnumerator>, ScuttlerControl, IEnumerator>)((orig, self) =>
                HeroSwitch.HornetActive ? HornetHeal(self) : orig(self)));
        else
            Log.Error("[BlueHealthBridge] ScuttlerControl.Heal not found");
    }

    private static IEnumerator HornetHeal(ScuttlerControl self) {
        yield return new WaitForSeconds(1.2f);
        try {
            EventRegister.SendEvent(AddEvent); // no Knight-distance gate; the relay hook mirrors this to Hornet's FSM
        } catch (Exception e) {
            Log.Error($"[BlueHealthBridge] scuttler heal: {e.Message}");
        }

        if (self != null) self.gameObject.SetActive(false);
    }

    // Replay the scene-ready "LAST HP ADDED" so the FSM rests in "Idle" (where it accepts grants) instead of "Wait".
    // Called per-frame from CameraSwitchDriver while Hornet's HUD is active; fires only while the FSM is actually in
    // "Wait" -- once per HUD (re)activation, then it settles in Idle and this no-ops.
    internal static void KeepReady() {
        var f = Fsm();
        if (f == null || !f.gameObject.activeInHierarchy) return;
        if (f.ActiveStateName == "Wait") f.SendEvent(SceneReadyEvent);
    }

    // The rig's "Blue Health Control" FSM (a second, inactive instance lives on the _GameCameras prefab). Scoped to
    // GameCameras.SilentInstance so we get the live rig one; cached (the rig is DontDestroyOnLoad).
    private static SilksongPM::PlayMakerFSM? Fsm() {
        if (fsm != null) return fsm;
        var gc = Silksong::GameCameras.SilentInstance;
        if (gc == null) return null;
        foreach (var f in gc.gameObject.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true))
            if (f.FsmName == "Blue Health Control") {
                fsm = f;
                return fsm;
            }

        return null;
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        healHook?.Dispose();
        healHook = null;
        fsm = null;
    }
}
