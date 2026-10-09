extern alias Silksong;
using System;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules;

// Hornet's Needolin stands in for the Dream Nail: any song drains a dreamer from inside its dream, and Elegy of the Deep
// (Needolin played holding down) acts as a Dream Nail on nearby HK dream-reactive objects.
public sealed class NeedolinDreamNailModule : ModuleBase {
    private const float Radius = 6f; // Needolin noise radius
    private const int HeroAttackLayer = 17; // HK's dream nail Hitbox layer

    private GameObject? hitboxGo;
    private Collider2D? collider;
    private bool wasPerforming;
    private bool wasPlayingElegy;
    private int silkAtStart;
    private bool impactArmed;
    private static string? drainEndScene;

    public override string Id => "needolin-dreamnail";

    public override void Initialize() {
        Detour(typeof(Fsm), "SwitchState", OnSwitchState, typeof(FsmState));
    }

    private static void OnSwitchState(Action<Fsm, FsmState> orig, Fsm self, FsmState to) {
        orig(self, to);
        if (self is { Name: "Control", GameObjectName: "Dreamer NPC" } && to.Name == "End Start")
            drainEndScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }

    // From "End Start" the dreamer sets the Knight's Spell Control "Dream End" and he finishes draining without input.
    // Hold Hornet every frame until the dream unloads: Needolin hands control back whenever it stops playing.
    private static void HoldThroughDrainEnd(Silksong::HeroController hero) {
        if (drainEndScene == null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != drainEndScene) {
            drainEndScene = null;
            return;
        }

        if (!hero.controlReqlinquished || hero.acceptingInput) hero.RelinquishControl();
    }

    protected override void OnDeinitialize() {
        if (hitboxGo) Object.Destroy(hitboxGo);
        hitboxGo = null;
        collider = null;
        wasPerforming = false;
        wasPlayingElegy = false;
        impactArmed = false;
    }

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        HoldThroughDrainEnd(hero);

        // Not Silk Specials' "Needolin Sub" state: holding the button while CanPlayNeedolin() fails re-enters it every
        // few frames, and each re-entry would dream-impact (white flash) everything in range.
        var performing = Silksong::HeroPerformanceRegion.IsPerforming;
        if (performing != wasPerforming) {
            wasPerforming = performing;
            FocusToggled(performing);
        }

        var playingElegy = performing && hero.cState.needolinPlayingMemory;
        if (playingElegy != wasPlayingElegy) {
            wasPlayingElegy = playingElegy;
            ElegyToggled(hero, playingElegy);
        }

        if (playingElegy && !impactArmed && Silksong::PlayerData.instance.silk < silkAtStart) ArmImpact();
    }

    private static void FocusToggled(bool performing) {
        if (!performing) {
            // Releasing Focus broadcasts this in vanilla: a draining dreamer goes back to waiting instead of draining on
            // while Hornet walks off its platform.
            PlayMakerFSM.BroadcastEvent("DREAM FOCUS END");
            return;
        }

        // Frees an already-wounded HK dreamer (drains on "DREAM FOCUS START"). Vanilla broadcasts this while Focus is
        // held; Needolin is Hornet's sustained-focus equivalent. Global like vanilla, only a wounded dreamer reacts.
        PlayMakerFSM.BroadcastEvent("DREAM FOCUS START");
    }

    private void ElegyToggled(Silksong::HeroController hero, bool playing) {
        impactArmed = false;
        if (!playing) {
            if (collider) collider.enabled = false;
            return;
        }

        EnsureHitbox(hero);
        silkAtStart = Silksong::PlayerData.instance.silk;
    }

    private void ArmImpact() {
        impactArmed = true;
        if (!collider) return;
        // Re-enable so Unity re-emits OnTriggerEnter2D for everything already inside the radius (Hornet is stationary
        // while playing, so a freshly-enabled trigger cleanly reports current overlaps).
        collider.enabled = false;
        collider.enabled = true;
    }

    private void EnsureHitbox(Silksong::HeroController hero) {
        if (hitboxGo) return;
        hitboxGo = new GameObject("hp_needolin_dreamnail") { tag = "Dream Attack", layer = HeroAttackLayer };
        hitboxGo.transform.SetParent(hero.transform, false);
        var c = hitboxGo.AddComponent<CircleCollider2D>();
        c.isTrigger = true;
        c.radius = Radius;
        c.enabled = false;
        collider = c;
        hitboxGo.AddComponent<NeedolinHitbox>();
    }
}

// Port of HK's dream-nail "Send Event" FSM state: notify FSM listeners + deliver the EnemyDreamnailReaction impact.
internal sealed class NeedolinHitbox : MonoBehaviour {
    private void OnTriggerEnter2D(Collider2D other) {
        var go = other.gameObject;
        FSMUtility.SendEventToGameObject(go, "DREAM IMPACT");

        if (!go.TryGetComponent<EnemyDreamnailReaction>(out var reaction)) {
            var parent = go.GetComponentInParent<EnemyDreamnailReaction>();
            if (parent && parent.allowUseChildColliders) reaction = parent;
        }

        if (!reaction) return;

        // Suppress the per-enemy silk grant for this impact only (Needolin granting silk per enemy would be OP)
        var prev = reaction.GetFieldValue<bool>("noSoul");
        reaction.SetFieldValue("noSoul", true);
        try {
            reaction.RecieveDreamImpact();
        } finally {
            reaction.SetFieldValue("noSoul", prev);
        }
    }
}
