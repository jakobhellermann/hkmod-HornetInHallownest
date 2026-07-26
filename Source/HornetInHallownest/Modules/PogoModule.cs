extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.Playground;
using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Modules;

// Hornet's down-attack pogo vs HK objects. Two cross-game gaps, both on HeroDownAttack:
//   - IsNonBounce: don't pogo off HK objects HK marks non-pogoable (they carry HK's NonBouncer, a different type than
//     Silksong's, so her gate misses them, and she'd bounce off everything).
//   - ContinueBounceTrigger: do give the big launch off HK BounceShroom/BigBouncer (her downspike only knows the normal
//     rebound), mirroring HK's NailSlash branch.
public sealed class PogoModule : ModuleBase {
    public override string Id => "pogo";

    public override void Initialize() {
        Detour(typeof(Silksong::HeroDownAttack), "IsNonBounce", OnIsNonBounce, typeof(GameObject));
        Detour(typeof(Silksong::HeroDownAttack), "ContinueBounceTrigger", OnContinueBounce, typeof(GameObject));
    }

    // Honor HK's NonBouncer too (postfix). Covers both pogo paths (ContinueBounceTrigger, OnHitResponded).
    private static bool OnIsNonBounce(Func<GameObject, bool> orig, GameObject obj) {
        if (orig(obj)) return true; // Silksong NonBouncer/BounceBalloon already said no-bounce
        if (!obj) return false;
        var nb = obj.GetComponent<NonBouncer>(); // HK's NonBouncer (global)
        return nb && nb.active;
    }

    private void OnContinueBounce(Action<Silksong::HeroDownAttack, GameObject?> orig, Silksong::HeroDownAttack self,
        GameObject otherObj) {
        var hero = HeroSwitch.HornetActive ? HornetSpawner.Hornet : null;
        if (hero && otherObj) {
            // Do the dedicated launch instead of orig's normal downspike QueueBounce (which would override the bigger
            // SHROOM_BOUNCE_VELOCITY back down). A shroom/bouncer carries no DamageHero, so no damage handling is lost.
            if (otherObj.GetComponent<BigBouncer>()) {
                hero.BounceHigh();
                return;
            }

            var shroom = otherObj.GetComponentInParent<BounceShroom>();
            if (shroom && shroom.active) {
                // End the downspike first (else the stab keeps driving her velocity down): mirror DownspikeBounce's exit
                // (FinishDownspike + BecomeAirborne) without its velocity-capping jump_steps bounce, then ShroomBounce.
                hero.FinishDownspike(true);
                hero.InvokeMethod("BecomeAirborne");
                hero.ShroomBounce();
                DoShroomEffects(shroom, hero);
                return;
            }
        }

        orig(self, otherObj);
    }

    // Shroom squish anim + particles. Guarded: BounceLarge touches GameCameras.instance.cameraShakeFSM, which may be
    // null on our neutered camera rig, and the bounce itself must not depend on it.
    private void DoShroomEffects(BounceShroom shroom, Silksong::HeroController hero) {
        try {
            shroom.BounceLarge();
            // BounceLarge parents its particles to HeroController.instance (HK's inert Knight) -> re-parent onto Hornet.
            var hp = typeof(BounceShroom).GetFieldValue<GameObject>("heroParticles");
            if (hp) {
                hp.transform.SetParent(hero.transform, false);
                hp.transform.localPosition = new Vector3(0f, -1.5f, -0.002f);
            }
        } catch (Exception e) {
            LogDebugOnce("shroom-bouncelarge-fail", $"BounceLarge effects skipped: {e.Message}");
        }
    }
}
