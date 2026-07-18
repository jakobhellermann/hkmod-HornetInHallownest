extern alias Silksong;
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

            // The roar's real source is HK's "Roar Object" (a boss body), not the event sender.
            if (name == "ROAR ENTER") {
                var source = FsmLookupModule.RoarObject;
                if (!source) source = self.GameObject;
                try { SetRoarFacingTarget(hero.gameObject, source); }
                catch (Exception e) { LogError($"roar facing: {e.Message}"); }
            }

            foreach (var fsm in hero.gameObject.GetComponents<SilksongPM::PlayMakerFSM>())
                fsm.SendEvent(name);
        } catch (Exception e) {
            LogError($"relay: {e.Message}");
        }
    }

    #region Roar facing

    // Hornet faces a roar by comparing her x to a "Roar Wave Emitter" object's x (CheckTargetDirection). HK bosses never
    // position that object (Silksong bosses do; HK's own facing lives on the Knight-only "Roar Lock" FSM she lacks), and
    // the emitter isn't a real FsmVariable, so it can't be rebound by name. Feed the roaring boss straight into the
    // action's target instead, before ROAR ENTER enters the state that reads it.
    private static void SetRoarFacingTarget(GameObject heroGo, GameObject? boss) {
        if (!boss) return;
        foreach (var fsm in heroGo.GetComponents<SilksongPM::PlayMakerFSM>()) {
            if (fsm.FsmName != "Roar and Wound States") continue;
            var state = fsm.Fsm.GetState("Roar Lock Start");
            if (state == null) return;
            foreach (var action in state.Actions)
                if (action is Silksong::HutongGames.PlayMaker.Actions.CheckTargetDirection ctd && ctd.target != null)
                    ctd.target.Value = boss;
            return;
        }
    }

    #endregion
}
