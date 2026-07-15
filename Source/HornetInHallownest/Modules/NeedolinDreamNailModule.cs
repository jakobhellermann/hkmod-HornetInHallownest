extern alias Silksong;
extern alias SilksongPM;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.HornetInHallownest.Modules;

// Hornet's Needolin acts as a Dream Nail on nearby HK dream-reactive objects.
public sealed class NeedolinDreamNailModule : ModuleBase {
    private const float Radius = 6f; // Needolin noise radius
    private const int HeroAttackLayer = 17; // HK's dream nail Hitbox layer

    private GameObject? hitboxGo;
    private Collider2D? collider;
    private SilksongPM::PlayMakerFSM? silkSpecials;
    private bool wasPlaying;

    public override string Id => "needolin-dreamnail";

    public override void Initialize() {
    }

    protected override void OnDeinitialize() {
        if (hitboxGo) Object.Destroy(hitboxGo);
        hitboxGo = null;
        collider = null;
        silkSpecials = null;
        wasPlaying = false;
    }

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        var playing = IsNeedolinPlaying(hero);
        if (playing == wasPlaying) return; 
        wasPlaying = playing;
        
        NeedolinToggled(hero, playing);
    }

    private void NeedolinToggled(Silksong::HeroController hero, bool playing) {
        if (!playing) {
            if (collider) collider.enabled = false;
            return;
        }

        EnsureHitbox(hero);
        // Re-enable so Unity re-emits OnTriggerEnter2D for everything already inside the radius (Hornet is stationary
        // while playing, so a freshly-enabled trigger cleanly reports current overlaps).
        if (collider) {
            collider.enabled = false;
            collider.enabled = true;
        }

        // Frees an already-wounded HK dreamer (drains on "DREAM FOCUS START"). Vanilla broadcasts this while Focus is
        // held; Needolin is Hornet's sustained-focus equivalent. Global like vanilla — only a wounded dreamer reacts.
        PlayMakerFSM.BroadcastEvent("DREAM FOCUS START");
    }

    private bool IsNeedolinPlaying(Silksong::HeroController hero) {
        if (!silkSpecials) {
            // TODO: perf
            foreach (var f in hero.GetComponents<SilksongPM::PlayMakerFSM>()) {
                if (f.FsmName == "Silk Specials") {
                    silkSpecials = f;
                    break;
                }
            }
        }

        return silkSpecials && silkSpecials.ActiveStateName == "Needolin Sub";
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
