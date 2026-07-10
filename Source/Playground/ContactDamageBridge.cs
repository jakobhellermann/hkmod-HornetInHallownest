extern alias Silksong;
using System;
using System.Reflection;
using HornetPlayer.HornetInHallownest.Modules;
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
            // HK objects damage the hero via one of TWO channels (mirrors HK's OWN HeroBox.CheckForDamage order):
            //   (a) a "damages_hero" PlayMakerFSM holding int vars damageDealt/hazardType (mines stompers, saws, spike
            //       hazards, acid colliders, …) — the MAJORITY of HK hazards, and
            //   (b) a plain DamageHero component (enemies, laser beams, …).
            // Silksong's HeroBox path (orig above) is a no-op for BOTH: its FSMUtility scans the ISOLATED Silksong
            // PlayMaker so HK's damages_hero FSM is invisible, and its GetComponent<DamageHero> resolves the Silksong
            // type so HK's DamageHero is invisible. We read HK's side of each here.
            // hazardType: enemies default to 1 (generic); real spike/acid/lava/pit hazards use 2+. Only map 2+ to
            // Silksong's hazard enum so DieFromHazard fires for real hazards, not enemy hits.
            var dmg = 0;
            var ssHazard = SHazard.ENEMY;
            // (a) HK's "damages_hero" FSM — resolved via HK's FSMUtility (HK PlayMakerFSM type). Read exactly the two
            // ints HK's own HeroBox reads (HeroBox.cs:46-47).
            if (FSMUtility.ContainsFSM(other, "damages_hero")) {
                var fsm = FSMUtility.LocateFSM(other, "damages_hero");
                dmg = FSMUtility.GetInt(fsm, "damageDealt");
                var hkHazard = FSMUtility.GetInt(fsm, "hazardType");
                if (hkHazard >= 2) ssHazard = MapHazard(hkHazard);
            }
            else {
                // (b) HK's DamageHero component.
                var dh = other.GetComponentInParent<DamageHero>();
                if (dh != null && dh.enabled) {
                    dmg = dh.damageDealt;
                    if (dh.hazardType >= 2)
                        ssHazard = MapHazard(dh.hazardType);
                }
            }

            // Do NOT force a positive amount here. HK authors every killing hazard (acid/spikes/pit) with damageDealt=1
            // (verified: acid box + White Palace spikes both = 1), and HK's own TakeDamage early-returns on damageAmount<=0
            // (HeroController:2240) BEFORE the DieFromHazard switch — Silksong's does the same. That early-return IS the
            // acid-armour "swim" mechanism: the `Acid Armour Check` FSM on each acid box sets DamageHero.damageDealt=0 when
            // PlayerData.hasAcidArmour is true (Isma's Tear), so touching acid deals 0 -> no death. Forcing dmg=1 here would
            // override that zero and kill Hornet through acid even with the armour equipped. So pass the real value: 1 kills
            // (no armour), 0 no-ops (armour or any other SetDamageHeroAmount-driven disable).

            // Acid + zeroed damage == she has Isma's Tear -> float/swim on the surface (HK acid isn't a Silksong water
            // region, so her real HeroWaterController never fires on its own). ACID with dmg>0 = no armour -> falls through
            // to the lethal path below, same as HK.
            if (ssHazard == SHazard.ACID && dmg == 0)
                AcidSwimBridge.NotifyInAcid(other);

            if (dmg <= 0) return;

            var hc = SHeroController.instance;
            if (hc == null) return;

            // Knight-height collider toggle: her terrain collider (col2d) is shrunk to the Knight's height so she FITS
            // low passages, but her HeroBox hurtbox is still her full tall Silksong box (HeroBoxNormal ~2.25) — so ceiling
            // spikes in a passage she now fits through would still hit her phantom upper body through that tall hurtbox.
            // SCOPE: only while the toggle is ON, and only for real HAZARDS (ssHazard != ENEMY) — enemy combat keeps her
            // full hurtbox, no balance change. Rule: skip a hazard whose collider sits entirely above her actual body,
            // i.e. above the top of her (now Knight-height) terrain collider. The cutoff is read from the LIVE terrain
            // collider bounds (single source of truth — tracks whatever height the toggle set, no magic number). Fail-open:
            // if either collider is missing, take the hit. Bounds reads only happen during active hazard overlap (rare).
            if (HornetSpawner.KnightHeightCollider && ssHazard != SHazard.ENEMY) {
                var body = HornetSpawner.TerrainCollider;
                var dmgCol = other.GetComponent<Collider2D>();
                if (body != null && dmgCol != null && dmgCol.bounds.min.y >= body.bounds.max.y) return;
            }

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
