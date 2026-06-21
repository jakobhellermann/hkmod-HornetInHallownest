extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SHeroBox = Silksong::HeroBox;
using SHeroController = Silksong::HeroController;
using SHazard = Silksong::GlobalEnums.HazardType;
using SSide = Silksong::GlobalEnums.CollisionSide;

namespace HornetPlayer.Playground;

// Reverse of EnemyDamageBridge: make HK enemies/hazards deal CONTACT damage to Hornet.
//
// In both games the hero's box detects contact damage on whatever its collider overlaps: Silksong's HeroBox.CheckForDamage
// looks for a "damages_hero" PlayMakerFSM, else a DamageHero component. But Hornet's HeroBox is Silksong's — its
// FSMUtility scans the ISOLATED Silksong.PlayMaker (HK FSMs invisible) and its GetComponent<DamageHero> resolves the
// Silksong.DamageHero type. HK enemies carry HK's "damages_hero" FSM / HK's DamageHero (other assembly), so Hornet's box
// finds nothing -> she walks through HK enemies unharmed.
//
// Fix: hook HeroBox.CheckForDamage. After the Silksong path (a no-op for HK objects), read HK's DamageHero / HK
// "damages_hero" FSM off the overlapped object and route it through Hornet's own TakeDamage (which keeps her i-frames /
// invulnerability gating, so the per-frame OnTriggerStay calls don't multi-hit). HeroBox only exists on Hornet, so this
// fires exclusively for her box (HK's Knight uses HK's own HeroBox in the other assembly).
internal static class ContactDamageBridge {
    private static Hook? hook;
    private delegate void Orig(SHeroBox self, GameObject other);
    private delegate void Hooked(Orig orig, SHeroBox self, GameObject other);

    // Log the first contact from each distinct HK object (clean log, still names every new hazard that hurts her).
    private static readonly HashSet<string> seen = new();

    internal static void Install() {
        var mi = typeof(SHeroBox).GetMethod("CheckForDamage", BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(GameObject) }, null);
        if (mi == null) { Log.Error("[ContactDamageBridge] HeroBox.CheckForDamage(GameObject) not found"); return; }
        hook = new Hook(mi, (Hooked)OnCheckForDamage);
        Log.Info("[ContactDamageBridge] installed: HeroBox.CheckForDamage");
    }

    private static void OnCheckForDamage(Orig orig, SHeroBox self, GameObject other) {
        orig(self, other); // Silksong damagers (no-op for HK objects)
        if (other == null || self == null) return;
        // Only hurt Hornet while she's the ACTIVE hero. When she's inert (Knight active) "inert" only flips
        // HeroController.enabled + rb.simulated + freezes anim — her HeroBox/colliders/GameObject stay live and
        // HeroController.instance still points at her, so this hook would otherwise fire on overlap. TakeDamage is a
        // plain method call (enabled=false doesn't gate it) and StartCoroutine(StartRecoil) ticks while the GO is active,
        // so it turns gravity OFF + sets no_input — but the FixedUpdate-driven recovery is gated by enabled, never runs
        // while inert, and leaves her stranded floating in no_input on the next switch. So: no contact damage while inert.
        if (!HeroSwitch.HornetActive) return;
        try {
            // HK's "damages_hero" FSM takes priority (some hazards drive damage through it), else HK's DamageHero data comp.
            int dmg = 0, hazard = (int)SHazard.ENEMY;
            var fsm = global::FSMUtility.LocateFSM(other, "damages_hero");
            if (fsm != null) {
                dmg = fsm.FsmVariables.GetFsmInt("damageDealt").Value;
                hazard = fsm.FsmVariables.GetFsmInt("hazardType").Value;
            } else {
                var dh = other.GetComponent<global::DamageHero>();
                if (dh != null && dh.enabled) { dmg = dh.damageDealt; hazard = dh.hazardType; }
            }
            if (dmg <= 0) return;

            var hc = SHeroController.instance;
            if (hc == null) return;
            if (seen.Add(other.name))
                Log.Info($"[ContactDamageBridge] HK contact damage from '{other.name}' dmg={dmg} hazard={hazard}");
            // damageSide = the side the damager is on (matches HeroBox.CheckForDamage's own computation).
            var side = other.transform.position.x > self.transform.position.x ? SSide.right : SSide.left;
            hc.TakeDamage(other, side, dmg, (SHazard)hazard);
        } catch (Exception e) {
            Log.Error($"[ContactDamageBridge] {e.Message}");
        }
    }

    internal static void Cleanup() { hook?.Dispose(); hook = null; }
}
