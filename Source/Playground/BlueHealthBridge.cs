extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// HK Lifeblood (Health Cocoon -> Health Scuttlers) -> Hornet blue health (PlayerData.healthBlue + a blue HUD mask).
//
// The scuttlers already home to Hornet (same retarget class as SoulOrbBridge: their "Player"-tag / hero lookups steer to
// her). But the GRANT is dead: ScuttlerControl.Heal() (1.2s after a scuttler is nailed) fires
// EventRegister.SendEvent("ADD BLUE HEALTH") on HK's global event bus, whose only listener is HK's "Blue Health Control"
// FSM under GameCameras.hudCanvas -- which HeroSwitch DISABLES while Hornet is active (SetHkHudVisible false). So the
// grant lands on a disabled FSM and no mask appears. (HK never writes healthBlue in C# for the cocoon path -- it's
// purely this FSM -- so watching HK's PlayerData.healthBlue wouldn't catch it either.)
//
// Fix (source-agnostic): hook EventRegister.SendEvent and, while Hornet is active, relay "ADD BLUE HEALTH" to HER own
// Silksong "Blue Health Control" FSM on the brought-up HUD rig. That FSM does the whole job natively -- increments
// Silksong PlayerData.healthBlue, spawns the blue mask prefab, drives its blue_health_display -- so Hornet's damage
// (Silksong TakeHealth depletes healthBlue first) and HUD both stay coherent. Any HK blue-health source routed through
// EventRegister funnels through the same seam.
//
// Timing: the Silksong FSM rests in "Wait" (its scene-ready "LAST HP ADDED" was never fired in our HUD bring-up) and
// only ONE "ADD BLUE HEALTH" per Idle visit takes effect (a second while it's mid-transition no-ops). So we QUEUE grants
// and drain them one-per-Idle from a coroutine, priming Wait->Idle with "LAST HP ADDED" once.
internal static class BlueHealthBridge {
    private const string FsmName = "Blue Health Control";
    private const string AddEvent = "ADD BLUE HEALTH";
    private const string PrimeEvent = "LAST HP ADDED"; // Wait -> Set Blue -> Add Existing? -> Idle (game's scene-ready)

    private static Hook? hook;
    private static SilksongPM::PlayMakerFSM? fsm;
    private static int pending;
    private static bool draining;

    internal static void Install() {
        if (hook != null) return;
        var mi = typeof(EventRegister).GetMethod("SendEvent", BindingFlags.Public | BindingFlags.Static, null,
            [typeof(string), typeof(GameObject)], null);
        if (mi == null) {
            Log.Error("[BlueHealthBridge] EventRegister.SendEvent(string,GameObject) not found");
            return;
        }

        hook = new Hook(mi, (Action<Action<string, GameObject>, string, GameObject>)((orig, name, exclude) => {
            orig(name, exclude); // let HK dispatch too (its Blue Health Control is disabled while Hornet is active)
            try {
                if (name == AddEvent && HeroSwitch.HornetActive) Enqueue();
            } catch (Exception e) {
                Log.Error($"[BlueHealthBridge] relay: {e.Message}");
            }
        }));
        Log.Debug("[BlueHealthBridge] installed: EventRegister 'ADD BLUE HEALTH' -> Hornet Blue Health Control");
    }

    private static void Enqueue() {
        pending++;
        if (draining) return;
        var host = Object.FindAnyObjectByType<PlaygroundHost>();
        if (host == null) {
            Log.Error("[BlueHealthBridge] no PlaygroundHost to drain on");
            pending = 0;
            return;
        }

        draining = true;
        host.StartCoroutine(Drain());
    }

    // Cache the HUD rig's Blue Health Control (Silksong-PlayMaker) FSM. Found off GameCameras (the rig root), reachable
    // even while its GO subtree toggles. Two scene instances exist (Menu_Title + our rig); only the rig's is loaded in
    // gameplay.
    private static SilksongPM::PlayMakerFSM? Fsm() {
        if (fsm != null) return fsm;
        var gc = Silksong::GameCameras.SilentInstance;
        if (gc == null) return null;
        foreach (var f in gc.gameObject.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true))
            if (f.FsmName == FsmName) {
                fsm = f;
                break;
            }

        return fsm;
    }

    private static IEnumerator Drain() {
        while (pending > 0) {
            var f = Fsm();
            if (f == null) {
                yield return null; // HUD not up yet; retry next frame
                continue;
            }

            switch (f.ActiveStateName) {
                case "Idle":
                    f.SendEvent(AddEvent);
                    pending--;
                    yield return null; // let it leave Idle before the next grant (else the next SendEvent no-ops)
                    break;
                case "Wait":
                    f.SendEvent(PrimeEvent); // one-time prime; the game fires this at scene-ready, we skip that
                    yield return null;
                    break;
                default:
                    yield return null; // mid-transition (Do Heal?/Add Blue Health/...); wait for it to settle to Idle
                    break;
            }
        }

        draining = false;
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        fsm = null;
        pending = 0;
        draining = false;
    }
}
