extern alias Silksong;
using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SHeroBox = Silksong::HeroBox;
using SHeroController = Silksong::HeroController;
using SHazard = Silksong::GlobalEnums.HazardType;
using SSide = Silksong::GlobalEnums.CollisionSide;
using SDmgFlags = Silksong::GlobalEnums.DamagePropertyFlags;

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


    internal static void Install() {
        var mi = typeof(SHeroBox).GetMethod("CheckForDamage", BindingFlags.Public | BindingFlags.Instance,
            null, [typeof(GameObject)], null);
        if (mi == null) {
            Log.Error("[ContactDamageBridge] HeroBox.CheckForDamage(GameObject) not found");
            return;
        }

        hook = new Hook(mi, (Hooked)OnCheckForDamage);
        Log.Debug("[ContactDamageBridge] installed: HeroBox.CheckForDamage");
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
            // Read HK's DamageHero for damage + hazardType. Distinguish hazards from enemies by hazardType:
            // enemies default to hazardType=1 (HK SPIKES); HK's spike hazards actually use 2+ (ACID, LAVA, PIT).
            // Only map 2+ to Silksong's hazard enum so DieFromHazard fires for real hazards, not enemy hits.
            // (HK's "damages_hero" PlayMakerFSM can't be found from our assembly — PlayMakerFSM resolves to
            // Silksong's type, not HK's. DamageHero.hazardType is the reliable signal.)
            var dmg = 0;
            var ssHazard = SHazard.ENEMY;
            var dh = other.GetComponentInParent<DamageHero>();
            if (dh != null && dh.enabled) {
                dmg = dh.damageDealt;
                if (dh.hazardType >= 2)
                    ssHazard = MapHazard(dh.hazardType);
            }

            // Hazards (acid/lava/pit/spikes) carry damageDealt=0 in HK — the death is intrinsic to the hazard type, not
            // driven by a damage number. But Silksong's TakeDamage gates its ENTIRE hazard/death body on `damageAmount > 0`
            // (HeroController:5281; the DieFromHazard switch lives inside it), and it re-normalizes ACID/SPIKES to 1 anyway.
            // So force a positive amount for real hazards or DieFromHazard never fires (she walks through acid unharmed).
            if (ssHazard != SHazard.ENEMY && dmg <= 0) dmg = 1;
            if (dmg <= 0) return;

            var hc = SHeroController.instance;
            if (hc == null) return;
            Log.InfoOnce($"contact:{other.name}",
                $"[ContactDamageBridge] HK contact damage from '{other.name}' dmg={dmg} hazard={ssHazard}");
            // damageSide = the side the damager is on (matches HeroBox.CheckForDamage's own computation).
            var side = other.transform.position.x > self.transform.position.x ? SSide.right : SSide.left;
            // NonLethal flag: a fatal hit routes through Die(nonLethal:true) (HeroController:5618), which SKIPS the whole
            // lethal corpse/cocoon block (gm.tilemap / gm.gameMap.PositionCompassAndCorpse / HeroCorpseMarker — all null on
            // our inactive bootstrap GM, so the lethal path NullRefs mid-coroutine before it can hand off). nonLethal still
            // spawns a death prefab + reaches the gm.PlayerDead handoff (which HornetDeath intercepts -> HK bench respawn).
            // The flag (bit 2) is consumed ONLY at that Die() call, so non-fatal hits are unaffected. Real corpse/cocoon =
            // a later feature that brings up gm.gameMap/tilemap properly and drops this flag.
            hc.TakeDamage(other, side, dmg, ssHazard, SDmgFlags.NonLethal);
        } catch (Exception e) {
            Log.Error($"[ContactDamageBridge] {e.Message}");
        }
    }

    // Map HK's DamageHero.hazardType to Silksong's HazardType enum. CRITICAL: DamageHero.hazardType is a raw INT, NOT
    // HK's HazardType enum — and HK's content tagging is inconsistent (spikes sometimes authored as "acid", etc). The
    // authoritative interpretation is HK's OWN TakeDamage switch (HeroController:2400), which reads the same int:
    //   1 = generic contact (no special hazard), 2 = SPIKES, 3 = ACID, 4 = LAVA, 5 = PIT.
    // Mirroring that switch makes Hornet die from a given hazard exactly as HK's Knight would from the same DamageHero.
    private static SHazard MapHazard(int hkHazard) {
        return hkHazard switch {
            2 => SHazard.SPIKES,
            3 => SHazard.ACID,
            4 => SHazard.LAVA,
            5 => SHazard.PIT,
            _ => SHazard.ENEMY // 1/unknown -> generic contact damage
        };
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }

    private delegate void Orig(SHeroBox self, GameObject other);

    private delegate void Hooked(Orig orig, SHeroBox self, GameObject other);
}
