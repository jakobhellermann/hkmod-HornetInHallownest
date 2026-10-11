extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;

namespace HornetInHallownest.Modules;

// Hornet's down-attack pogo vs HK objects. Two cross-game gaps, both on HeroDownAttack:
//   - IsNonBounce: don't pogo off HK objects HK marks non-pogoable (they carry HK's NonBouncer, a different type than
//     Silksong's, so her gate misses them, and she'd bounce off everything).
//   - ContinueBounceTrigger: do give the big launch off HK's BounceShroom (her downspike only knows the normal rebound),
//     mirroring HK's NailSlash branch.
public sealed class PogoModule : ModuleBase {
    public override string Id => "pogo";

    public override void Initialize() {
        Detour(typeof(Silksong::HeroDownAttack), "IsNonBounce", OnIsNonBounce, typeof(GameObject));
        Detour(typeof(Silksong::HeroDownAttack), "ContinueBounceTrigger", OnContinueBounce, typeof(GameObject));
        Detour(typeof(BounceShroom), "Start", OnShroomStart);
        Detour(typeof(Silksong::DamageEnemies), "DoDamage", OnDoDamage, typeof(GameObject), typeof(bool));
    }

    // Side and up slashes into a shroom: HK's NailSlash knocks the Knight back off it and bounces the shroom. Down
    // slashes are the pogo in OnContinueBounce.
    private static bool OnDoDamage(Func<Silksong::DamageEnemies, GameObject, bool, bool> orig,
        Silksong::DamageEnemies self, GameObject target, bool isFirstHit) {
        var result = orig(self, target, isFirstHit);
        var hero = HeroSwitch.HornetActive ? HornetSpawner.Hornet : null;
        if (!hero || !target || !self.transform.IsChildOf(hero.transform)) return result;
        var shroom = target.GetComponentInParent<BounceShroom>();
        if (!shroom || !shroom.active) return result;

        var direction = self.GetDirection();
        if (Mathf.Abs(Mathf.DeltaAngle(direction, 0f)) <= 45f) hero.RecoilLeftLong();
        else if (Mathf.Abs(Mathf.DeltaAngle(direction, 180f)) <= 45f) hero.RecoilRightLong();
        else if (Mathf.Abs(Mathf.DeltaAngle(direction, 90f)) <= 45f) hero.RecoilDown();
        else return result;

        var shroomFsm = FSMUtility.LocateFSM(target, "Bounce Shroom");
        if (shroomFsm) shroomFsm.SendEvent("BOUNCE UPWARD");
        else shroom.BounceLarge(false);
        return result;
    }

    private readonly List<CollisionEnterEvent> subscribedTriggers = [];

    // Landing on a shroom: BounceShroom's collision handler messages HeroController.instance, the inert Knight.
    private void OnShroomStart(Action<BounceShroom> orig, BounceShroom self) {
        orig(self);
        if (!self.active) return;
        subscribedTriggers.RemoveAll(t => !t); // died with their scene
        foreach (var trigger in self.GetComponentsInChildren<CollisionEnterEvent>()) {
            trigger.OnCollisionEnteredDirectional += BounceOffShroom;
            subscribedTriggers.Add(trigger);
        }
    }

    protected override void OnDeinitialize() {
        foreach (var trigger in subscribedTriggers)
            if (trigger) trigger.OnCollisionEnteredDirectional -= BounceOffShroom;
        subscribedTriggers.Clear();
    }

    private void BounceOffShroom(CollisionEnterEvent.Direction direction, Collision2D collision) {
        var hero = HeroSwitch.HornetActive ? HornetSpawner.Hornet : null;
        if (!hero || collision.gameObject != hero.gameObject) return;
        switch (direction) {
            case CollisionEnterEvent.Direction.Left:
                hero.RecoilLeft();
                break;
            case CollisionEnterEvent.Direction.Right:
                hero.RecoilRight();
                break;
            case CollisionEnterEvent.Direction.Top:
                if (hero.Bounce()) RecoilRandomly(hero);
                else StartCoroutine(BounceWhenControlled(hero, collision.otherCollider, collision.collider));
                break;
        }
    }

    // Mantling onto a shroom lands her while control is still relinquished, which Bounce refuses, and no new collision
    // follows once she's standing on it.
    private static IEnumerator BounceWhenControlled(Silksong::HeroController hero, Collider2D shroom,
        Collider2D heroCollider) {
        var deadline = Time.time + 1f;
        while (hero && hero.controlReqlinquished && Time.time < deadline) yield return null;
        if (hero && shroom && shroom.IsTouching(heroCollider) && hero.Bounce()) RecoilRandomly(hero);
    }

    private static void RecoilRandomly(Silksong::HeroController hero) {
        if (UnityEngine.Random.Range(0, 2) == 0) hero.RecoilLeft();
        else hero.RecoilRight();
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
            // SHROOM_BOUNCE_VELOCITY back down). A shroom carries no DamageHero, so no damage handling is lost.
            var shroom = otherObj.GetComponentInParent<BounceShroom>();
            if (shroom && shroom.active) {
                // End the downspike first (else the stab keeps driving her velocity down): mirror DownspikeBounce's exit
                // (FinishDownspike + BecomeAirborne) without its velocity-capping jump_steps bounce, then ShroomBounce.
                hero.FinishDownspike(true);
                hero.InvokeMethod("BecomeAirborne");
                hero.ShroomBounce();
                MatchKnightShroomHeight(hero);
                DoShroomEffects(shroom, hero);
                return;
            }
        }

        orig(self, otherObj);
    }

    private static void MatchKnightShroomHeight(Silksong::HeroController hero) {
        var knight = HeroController.instance;
        if (!knight) return;
        var body = hero.GetComponent<Rigidbody2D>();
        var velocity = knight.SHROOM_BOUNCE_VELOCITY * Mathf.Sqrt(hero.DEFAULT_GRAVITY / knight.DEFAULT_GRAVITY);
        body.linearVelocity = new Vector2(body.linearVelocity.x, velocity);
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
