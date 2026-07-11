extern alias Silksong;
using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SHeroDownAttack = Silksong::HeroDownAttack;

namespace HornetPlayer.Playground;

// HK bounce mushrooms (BounceShroom component) and BigBouncer launch the hero on a down-attack pogo. HK's NailSlash
// inspects the pogoed object and calls heroCtrl.ShroomBounce() (SHROOM_BOUNCE_VELOCITY, a big launch) / BounceHigh().
// Hornet's Silksong down-attack (HeroDownAttack.ContinueBounceTrigger) only ever runs the normal downspike bounce — it
// has no notion of HK's BounceShroom/BigBouncer types (cross-game component gap), so pogoing an HK shroom gives just a
// normal pogo. Mirror HK's branch: after her normal bounce path runs, if the pogoed object carries an (active) HK
// BounceShroom -> ShroomBounce() (sets SHROOM_BOUNCE_VELOCITY, overriding the smaller downspike rebound); BigBouncer ->
// BounceHigh(). Only the down-attack pogo is covered (the user's case); landing on top of a shroom is a separate path.
internal static class ShroomBounceBridge {
    private static Hook? hook;
    private static MethodInfo? becomeAirborne;
    private static FieldInfo? heroParticlesField;

    internal static void Install() {
        var mi = typeof(SHeroDownAttack).GetMethod("ContinueBounceTrigger",
            BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(GameObject)], null);
        if (mi == null) {
            Log.Error("[ShroomBounce] HeroDownAttack.ContinueBounceTrigger(GameObject) not found");
            return;
        }

        becomeAirborne = typeof(Silksong::HeroController).GetMethod("BecomeAirborne",
            BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        heroParticlesField = typeof(BounceShroom).GetField("heroParticles",
            BindingFlags.NonPublic | BindingFlags.Static);
        hook = new Hook(mi,
            (Action<Action<SHeroDownAttack, GameObject?>, SHeroDownAttack, GameObject>)OnContinueBounce);
        Log.Debug("[ShroomBounce] installed: HeroDownAttack.ContinueBounceTrigger");
    }

    private static void OnContinueBounce(Action<SHeroDownAttack, GameObject?> orig, SHeroDownAttack self,
        GameObject otherObj) {
        var hero = HeroSwitch.HornetActive ? BundleSpike.RealHero : null;
        if (hero != null && otherObj != null) {
            // Mirror HK's NailSlash branch: for a BigBouncer / active BounceShroom we do the dedicated launch INSTEAD of
            // the normal downspike bounce (so we skip orig's QueueBounce). orig is otherwise just QueueBounce here — a
            // shroom/bouncer carries no DamageHero, so no damage handling is lost. Calling ShroomBounce after orig
            // doesn't work: orig's DownspikeBounce (immediate + re-fired on the anim "Bounce" event) overrides the
            // SHROOM_BOUNCE_VELOCITY back down to the normal rebound.
            if (otherObj.GetComponent<BigBouncer>() != null) {
                hero.BounceHigh();
                return;
            }

            var shroom = otherObj.GetComponentInParent<BounceShroom>();
            if (shroom != null && shroom.active) {
                // End the downspike first (else the stab keeps driving her velocity down and overrides the bounce) —
                // mirror DownspikeBounce's exit (FinishDownspike + BecomeAirborne) but WITHOUT its jump_steps bounce,
                // which would cap velocity below SHROOM_BOUNCE_VELOCITY. Then ShroomBounce applies the full launch.
                hero.FinishDownspike(true);
                becomeAirborne?.Invoke(hero, null);
                hero.ShroomBounce();
                // Shroom squish anim + particles. Guarded: BounceLarge touches GameCameras.instance.cameraShakeFSM,
                // which may be null on our neutered camera rig — the bounce itself must not depend on it.
                try {
                    shroom.BounceLarge();
                    // BounceLarge parents its hero particles to HeroController.instance.transform (HK's Knight, not an
                    // FSM) -> the bounce trail sticks to the inert Knight. Re-parent the freshly spawned particles onto
                    // Hornet, keeping BounceLarge's local offset.
                    if (heroParticlesField?.GetValue(null) is GameObject hp && hp != null) {
                        hp.transform.SetParent(hero.transform, false);
                        hp.transform.localPosition = new Vector3(0f, -1.5f, -0.002f);
                    }
                } catch (Exception e) {
                    Log.DebugOnce("shroom-bouncelarge-fail", $"[ShroomBounce] BounceLarge effects skipped: {e.Message}");
                }

                return;
            }
        }

        orig(self, otherObj);
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }
}
