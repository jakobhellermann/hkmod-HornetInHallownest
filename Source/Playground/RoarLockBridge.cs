extern alias Silksong;
extern alias SilksongPM;
using UnityEngine;

namespace HornetPlayer.Playground;

// Roar facing, on top of HeroEventBridge (which forwards HK's ROAR ENTER/EXIT onto Hornet's "Roar and Wound States").
// The relay can't carry the push direction — HK seeds it on the Knight's "Roar Lock" FSM, which Hornet lacks — so on
// ROAR ENTER we set her "Roar Wave Emitter" to the roaring FSM's owner (the boss).
internal static class RoarLockBridge {
    private const string FsmName = "Roar and Wound States";

    internal static void Install() {
        HeroEventBridge.BeforeForward += OnHeroEvent;
    }

    private static void OnHeroEvent(string eventName, GameObject? sender) {
        if (eventName != "ROAR ENTER" || sender == null) return;
        var hero = BundleSpike.RealHero;
        if (hero == null) return;
        foreach (var fsm in hero.gameObject.GetComponents<SilksongPM::PlayMakerFSM>())
            if (fsm.FsmName == FsmName) {
                var emitter = fsm.FsmVariables.GetFsmGameObject("Roar Wave Emitter");
                emitter?.Value = sender;
                return;
            }
    }

    internal static void Cleanup() {
        HeroEventBridge.BeforeForward -= OnHeroEvent;
    }
}
