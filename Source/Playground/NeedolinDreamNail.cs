extern alias Silksong;
extern alias SilksongPM;
using System.Reflection;
using HutongGames.PlayMaker; // HK's PlayMakerFSM (for BroadcastEvent); Hornet's own FSMs use SilksongPM::PlayMakerFSM
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Hornet's Needolin, played near HK dream-reactive objects, acts as a Dream Nail — with NO per-object logic.
//
// HK delivers a dream-nail hit three heterogeneous ways (there is no single entrypoint): (1)
// EnemyDreamnailReaction.RecieveDreamImpact (enemies/NPCs -> soul[->silk] + thought convo), driven by the
// SendDreamImpact action; (2) target-side OnTriggerEnter2D that checks `collision.tag == "Dream Attack"` (whispering
// roots via DreamPlant, dreamer seals via BossStatueDreamToggle) — they detect the incoming collider themselves; (3)
// the "DREAM IMPACT" PlayMaker event. HK's own dream nail is the GO `Knight/Dream Effects/Hitbox`: a collider tagged
// "Dream Attack" + a 2-action "Send Event" FSM (SendEventByName "DREAM IMPACT" + SendDreamImpact) — that single object
// covers all three at once.
//
// Silksong's Needolin is a broadcast/listen ability and does NOT scan a radius (unlike HK's hitbox), so it can't reach
// HK objects on its own. We supply the hitbox: a radius trigger tagged "Dream Attack" parented UNDER Hornet — so its
// collider belongs to her dynamic Rigidbody2D, and dynamic-vs-static/-dynamic OnTriggerEnter2D fires cleanly for both
// static roots and moving enemies (and self-overlap with her own body colliders is suppressed, same shared body). The
// MonoBehaviour below is a faithful port of HK's "Send Event" FSM state, so paths (1) and (3) run generically; the tag
// on the collider covers path (2). Enabled once per Needolin (rising edge of the Silk Specials FSM's "Needolin Sub").
internal sealed class NeedolinHitbox : MonoBehaviour {
    // EnemyDreamnailReaction.noSoul gates the AddMPCharge (-> HK soul, redirected to Hornet's silk by SoulOrbBridge).
    // We force it true around the impact: a real Dream Nail grants soul, but Needolin granting silk per enemy would be
    // OP (Hornet has no Dream Nail — this is the only way she'd "dream nail" an enemy). Recoil/convo/impact still play.
    private static readonly FieldInfo? NoSoul =
        typeof(EnemyDreamnailReaction).GetField("noSoul", BindingFlags.Instance | BindingFlags.NonPublic);

    private void OnTriggerEnter2D(Collider2D other) {
        var go = other.gameObject;
        // Port of the HK "Send Event" state: notify FSM listeners...
        FSMUtility.SendEventToGameObject(go, "DREAM IMPACT");
        // ...and the SendDreamImpact action: resolve EnemyDreamnailReaction on the hit collider (or a parent that opts
        // in via allowUseChildColliders), then deliver the impact.
        var reaction = go.GetComponent<EnemyDreamnailReaction>();
        if (reaction == null) {
            var parent = go.GetComponentInParent<EnemyDreamnailReaction>();
            if (parent != null && parent.allowUseChildColliders) reaction = parent;
        }

        if (reaction == null) return;

        if (NoSoul == null) {
            reaction.RecieveDreamImpact();
            return;
        }

        // Suppress the silk grant for this impact only, then restore the enemy's own config.
        var prev = (bool)NoSoul.GetValue(reaction);
        NoSoul.SetValue(reaction, true);
        try {
            reaction.RecieveDreamImpact();
        } finally {
            NoSoul.SetValue(reaction, prev);
        }
    }
}

internal static class NeedolinDreamNail {
    // Needolin's own noise radius is 6 (Silk Specials CreateNoise); match it so the dream-nail field feels like the tune.
    private const float Radius = 6f;
    private const int HeroAttackLayer = 17; // HK's dream nail Hitbox layer

    private static GameObject? hitbox;
    private static Collider2D? col;
    private static SilksongPM::PlayMakerFSM? silkSpecials;
    private static bool wasPlaying;

    // Called per frame from HornetEnvironmentAdapter while Hornet is the active hero.
    internal static void Tick(Silksong::HeroController hero) {
        var playing = IsNeedolinPlaying(hero);
        if (playing == wasPlaying) return; // edge-triggered: act once per Needolin, not every frame
        wasPlaying = playing;

        if (playing) {
            EnsureHitbox(hero);
            // Re-enable so Unity emits OnTriggerEnter2D for everything already inside the radius (Hornet is stationary
            // while playing, so a freshly-enabled trigger cleanly reports current overlaps — same path as a normal slash).
            if (col != null) {
                col.enabled = false;
                col.enabled = true;
            }

            // Drive HK's dreamer-freeing: a Dreamer NPC that's already been dream-nailed (wounded, waiting in "Wound
            // Idle" showing "HOLD to Focus") starts draining on "DREAM FOCUS START" and completes the free on its own
            // timers. In vanilla the Knight's Spell Control broadcasts this while Focus is held; Needolin is Hornet's
            // sustained-focus equivalent, so we broadcast it when the tune starts. Global (BroadcastAll) exactly like
            // vanilla — only a wounded dreamer reacts; everything else ignores it. (First Needolin wounds via the hitbox
            // above; a second one, with the dreamer now in "Wound Idle", frees it.)
            PlayMakerFSM.BroadcastEvent("DREAM FOCUS START");
        } else if (col != null) {
            col.enabled = false;
        }
    }

    private static bool IsNeedolinPlaying(Silksong::HeroController hero) {
        if (silkSpecials == null) {
            foreach (var f in hero.GetComponents<SilksongPM::PlayMakerFSM>())
                if (f.FsmName == "Silk Specials") {
                    silkSpecials = f;
                    break;
                }
        }

        return silkSpecials != null && silkSpecials.ActiveStateName == "Needolin Sub";
    }

    private static void EnsureHitbox(Silksong::HeroController hero) {
        if (hitbox != null) return;
        hitbox = new GameObject("hp_needolin_dreamnail") { tag = "Dream Attack", layer = HeroAttackLayer };
        hitbox.transform.SetParent(hero.transform, false); // part of Hornet's dynamic body -> clean trigger enter
        var c = hitbox.AddComponent<CircleCollider2D>();
        c.isTrigger = true;
        c.radius = Radius;
        c.enabled = false;
        col = c;
        hitbox.AddComponent<NeedolinHitbox>();
    }

    internal static void Cleanup() {
        if (hitbox != null) Object.Destroy(hitbox);
        hitbox = null;
        col = null;
        silkSpecials = null;
        wasPlaying = false;
    }
}
