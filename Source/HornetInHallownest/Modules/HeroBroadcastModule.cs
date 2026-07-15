extern alias SilksongPM;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using HutongGames.PlayMaker;
using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Modules;

// General HK -> Hornet PlayMaker broadcast event relay.
public sealed class HeroBroadcastModule : ModuleBase {
    public override string Id => "broadcast";

    public override void Initialize() {
        Detour(typeof(Fsm), "BroadcastEventToGameObject", OnBroadcast,
            typeof(GameObject), typeof(FsmEvent), typeof(FsmEventData), typeof(bool), typeof(bool));
    }

    private void OnBroadcast(Action<Fsm, GameObject, FsmEvent?, FsmEventData?, bool, bool> orig, Fsm self,
        GameObject go, FsmEvent? ev, FsmEventData? data, bool sendToChildren, bool excludeSelf) {
        orig(self, go, ev, data, sendToChildren, excludeSelf);
        try {
            var name = ev?.Name;
            if (string.IsNullOrEmpty(name) || !HeroSwitch.HornetActive) return;
            
            var hero = HornetSpawner.RealHero;
            if (!hero || go != hero.gameObject) return;

            if (name == "ROAR ENTER") SetRoarEmitter(hero.gameObject, self.GameObject); 
            
            foreach (var fsm in hero.gameObject.GetComponents<SilksongPM::PlayMakerFSM>())
                fsm.SendEvent(name);
        } catch (Exception e) {
            LogError($"relay: {e.Message}");
        }
    }

    #region Roar facing

    // The relay can't carry the roar push direction - HK seeds it on the Knight's "Roar Lock" FSM, which Hornet lacks.
    // so point her "Roar and Wound States" FSM's "Roar Wave Emitter" at the roaring boss (the sender) before ROAR ENTER.
    private static void SetRoarEmitter(GameObject heroGo, GameObject? sender) {
        if (!sender) return;
        foreach (var fsm in heroGo.GetComponents<SilksongPM::PlayMakerFSM>()) {
            if (fsm.FsmName == "Roar and Wound States") {
                fsm.FsmVariables.GetFsmGameObject("Roar Wave Emitter")?.Value = sender;
                return;
            }
        }
    }

    #endregion
}
