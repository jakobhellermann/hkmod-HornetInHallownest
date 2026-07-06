extern alias SilksongPM;
using System;
using System.Reflection;
using HutongGames.PlayMaker;
using MonoMod.RuntimeDetour;
using UnityEngine;
// HK's PlayMaker — the FSMs that emit events at the hero

namespace HornetPlayer.Playground;

// General HK -> Hornet PlayMaker EVENT bridge.
//
// HK FSMs deliver events to the "Hero" GameObject via SendEventByName(eventTarget=GameObject) -> Fsm.Event ->
// Fsm.BroadcastEventToGameObject, which iterates HK's `PlayMakerFSM.FsmList` and matches `fsm.gameObject == Hero`.
// Hornet's FSMs live in the ISOLATED Silksong.PlayMaker runtime with its OWN FsmList, so they're invisible to HK's
// dispatch — HK events silently no-op on her even though "Hero" (HeroProxy) points at Hornet_Real. Same class as the
// #8/#8b cross-game GetComponent(s) collisions, but for event dispatch.
//
// Fix: hook BroadcastEventToGameObject and, when the target is the active Hornet, forward the event to HER Silksong
// FSMs. General (not an event allowlist): a Silksong FSM only reacts to an event it has a transition for in its current
// state (SendEvent no-ops otherwise), and while Hornet is active the Knight's own FSMs are disabled (HeroSwitch), so the
// only HK events reaching the Hero GO are scene/enemy FSMs reacting to the player — exactly what she should receive.
//
// Feature-specific reactions that need to prep state BEFORE the event lands (e.g. roar facing direction) subscribe to
// BeforeForward — no feature logic lives here.
internal static class HeroEventBridge {
    private static Hook? hook;

    // (eventName, senderGameObject) — senderGameObject is the emitting FSM's owner (may be null). Fires once per
    // forwarded HK event, immediately BEFORE the event is delivered to Hornet's FSMs.
    internal static event Action<string, GameObject?>? BeforeForward;

    internal static void Install() {
        if (hook != null) return;
        var mi = typeof(Fsm).GetMethod("BroadcastEventToGameObject",
            BindingFlags.Instance | BindingFlags.Public, null,
            [typeof(GameObject), typeof(FsmEvent), typeof(FsmEventData), typeof(bool), typeof(bool)], null);
        if (mi == null) {
            Log.Error("[HeroEventBridge] Fsm.BroadcastEventToGameObject(GameObject,FsmEvent,FsmEventData,bool,bool) " +
                      "not found");
            return;
        }

        hook = new Hook(mi, (Hooked)OnBroadcast);
        Log.Debug("[HeroEventBridge] installed HK->Hornet event relay");
    }

    private static void OnBroadcast(Orig orig, Fsm self, GameObject go, FsmEvent? ev, FsmEventData? data,
        bool sendToChildren, bool excludeSelf) {
        orig(self, go, ev, data, sendToChildren, excludeSelf);
        try {
            var name = ev?.Name;
            if (string.IsNullOrEmpty(name) || !HeroSwitch.HornetActive) return;
            var hero = BundleSpike.RealHero;
            if (hero == null || go != hero.gameObject) return; // only events aimed at the active Hornet

            BeforeForward?.Invoke(name!, self.GameObject); // self is `this` of the instance method — never null
            // Hornet's FSMs on the hero GO (~16) — more targeted than upstream's global FsmList scan; SendEvent no-ops on
            // any FSM without a transition for this event in its current state.
            foreach (var fsm in hero.gameObject.GetComponents<SilksongPM::PlayMakerFSM>())
                fsm.SendEvent(name);
        } catch (Exception e) {
            Log.Error($"[HeroEventBridge] relay: {e.Message}");
        }
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        BeforeForward = null;
    }

    private delegate void Orig(Fsm self, GameObject go, FsmEvent? ev, FsmEventData? data, bool sendToChildren,
        bool excludeSelf);

    private delegate void Hooked(Orig orig, Fsm self, GameObject go, FsmEvent? ev, FsmEventData? data,
        bool sendToChildren, bool excludeSelf);
}
