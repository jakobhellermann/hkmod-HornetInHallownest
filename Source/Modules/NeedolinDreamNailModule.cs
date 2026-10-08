extern alias Silksong;
using System;
using GlobalEnums;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules;

// Hornet's Needolin acts as a Dream Nail on nearby HK dream-reactive objects.
public sealed class NeedolinDreamNailModule : ModuleBase {
    private const float Radius = 6f; // Needolin noise radius
    private const int HeroAttackLayer = 17; // HK's dream nail Hitbox layer

    private GameObject? hitboxGo;
    private Collider2D? collider;
    private bool wasPlaying;
    private int silkAtStart;
    private bool impactArmed;
    private static string? drainEndScene;

    public override string Id => "needolin-dreamnail";

    public override void Initialize() {
        Detour(typeof(Fsm), "SwitchState", OnSwitchState, typeof(FsmState));
    }

    private static void OnSwitchState(Action<Fsm, FsmState> orig, Fsm self, FsmState to) {
        orig(self, to);
        if (self is { Name: "Display", GameObjectName: "Control Reminder" }) ShowDreamerReminder(self, to);

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

    // A dreamer's dream prompts "Hold [Cast] Focus" through the HUD Control Reminder's "Display" FSM; Hornet drains the
    // dreamer with Needolin on the Dream Nail bind instead.
    private static void ShowDreamerReminder(Fsm self, FsmState to) {
        var reminder = self.GameObject.transform;
        var needolin = to.Name == "Focus" && HeroSwitch.HornetActive &&
                       UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                           .StartsWith("Dream_Guardian_", StringComparison.Ordinal);
        var cast = reminder.Find("ActionButton Cast");
        if (cast && cast.TryGetComponent<ActionButtonIcon>(out var icon)) {
            var action = needolin ? HeroActionButton.DREAM_NAIL : HeroActionButton.CAST;
            if (icon.action != action) icon.SetAction(action);
        }

        var text = reminder.Find("Text");
        if (!needolin || !text) return;
        var label = Array.Find(text.GetComponents<Component>(), c => c.GetType().Name == "TextMeshPro");
        if (label) label.SetPropertyValue("text", "Needolin");
    }

    protected override void OnDeinitialize() {
        if (hitboxGo) Object.Destroy(hitboxGo);
        hitboxGo = null;
        collider = null;
        wasPlaying = false;
        impactArmed = false;
    }

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        HoldThroughDrainEnd(hero);

        // Not Silk Specials' "Needolin Sub" state: holding the button while CanPlayNeedolin() fails re-enters it every
        // few frames, and each re-entry would dream-impact (white flash) everything in range.
        var playing = Silksong::HeroPerformanceRegion.IsPerforming;
        if (playing != wasPlaying) {
            wasPlaying = playing;
            NeedolinToggled(hero, playing);
        }

        if (playing && !impactArmed && Silksong::PlayerData.instance.silk < silkAtStart) ArmImpact();
    }

    private void NeedolinToggled(Silksong::HeroController hero, bool playing) {
        impactArmed = false;
        if (!playing) {
            if (collider) collider.enabled = false;
            // Releasing Focus broadcasts this in vanilla: a draining dreamer goes back to waiting instead of draining on
            // while Hornet walks off its platform.
            PlayMakerFSM.BroadcastEvent("DREAM FOCUS END");
            return;
        }

        EnsureHitbox(hero);
        silkAtStart = Silksong::PlayerData.instance.silk;

        // Frees an already-wounded HK dreamer (drains on "DREAM FOCUS START"). Vanilla broadcasts this while Focus is
        // held; Needolin is Hornet's sustained-focus equivalent. Global like vanilla, only a wounded dreamer reacts.
        PlayMakerFSM.BroadcastEvent("DREAM FOCUS START");
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
