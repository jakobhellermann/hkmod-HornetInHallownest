extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using UnityEngine;
using HeroBouncer = Silksong::HeroBouncer;
using SHeroBox = Silksong::HeroBox;
using SHeroController = Silksong::HeroController;
using SHazard = Silksong::GlobalEnums.HazardType;
using SSide = Silksong::GlobalEnums.CollisionSide;
using SDmgFlags = Silksong::GlobalEnums.DamagePropertyFlags;

namespace HornetPlayer.HornetInHallownest.Modules;

// TODO: check if this can be simplified

// Make HK enemies/hazards deal contact damage to Hornet. Her HeroBox is Silksong's: its FSMUtility scans the isolated
// Silksong PlayMaker and its GetComponent<DamageHero> resolves the Silksong type, so HK's "damages_hero" FSM / HK
// DamageHero are invisible and she walks through HK damagers unharmed. Read HK's side here and route it through her own
// TakeDamage (which keeps her i-frame gating). HeroBox only exists on Hornet, so this fires only for her.
public sealed class ContactDamageModule : ModuleBase {
    public override string Id => "contact-damage";

    public override void Initialize() {
        Detour(typeof(SHeroBox), "CheckForDamage", OnCheckForDamage, typeof(GameObject));
    }

    private void OnCheckForDamage(Action<SHeroBox, GameObject> orig, SHeroBox self, GameObject other) {
        orig(self, other); 
        if (!other || !self) return;
        if (!HeroSwitch.HornetActive) return;
        try {
            var dmg = 0;
            var ssHazard = SHazard.ENEMY;
            // HK damages via a "damages_hero" FSM (most hazards) or a plain DamageHero component (enemies/beams).
            if (FSMUtility.ContainsFSM(other, "damages_hero")) {
                var fsm = FSMUtility.LocateFSM(other, "damages_hero");
                dmg = FSMUtility.GetInt(fsm, "damageDealt");
                var hkHazard = FSMUtility.GetInt(fsm, "hazardType");
                if (hkHazard >= 2) ssHazard = MapHazard(hkHazard);
            }
            else {
                var damageHero = other.GetComponentInParent<DamageHero>();
                if (damageHero && damageHero.enabled) {
                    dmg = damageHero.damageDealt;
                    if (damageHero.hazardType >= 2) ssHazard = MapHazard(damageHero.hazardType);
                }
            }

            // Isma's zeroes damageDealt
            if (ssHazard == SHazard.ACID && dmg == 0) AcidSwimBridge.NotifyInAcid(other);
            if (dmg <= 0) return;

            var hc = SHeroController.instance;
            if (hc == null) return;

            // Knight-height collider toggle shrinks her terrain collider but not her tall HeroBox hurtbox, so skip a
            // hazard sitting entirely above her (now shorter) body. Hazards only; enemy combat keeps the full hurtbox.
            if (HornetSpawner.KnightHeightCollider && ssHazard != SHazard.ENEMY) {
                var body = HornetSpawner.TerrainCollider;
                var dmgCol = other.GetComponent<Collider2D>();
                if (body != null && dmgCol != null && dmgCol.bounds.min.y >= body.bounds.max.y) return;
            }

            var side = other.transform.position.x > self.transform.position.x ? SSide.right : SSide.left;
            // TODO: use lethal death animation if appropriate 
            // NonLethal: routes a fatal hit through Die(nonLethal), which skips the lethal corpse/cocoon block
            // but still reaches the PlayerDead handoff HornetDeath intercepts.
            hc.TakeDamage(other, side, dmg, ssHazard, SDmgFlags.NonLethal);
        } catch (Exception e) {
            LogError(e.Message);
        }
    }

    private static SHazard MapHazard(int hkHazard) {
        return hkHazard switch {
            2 => SHazard.SPIKES,
            3 => SHazard.ACID,
            4 => SHazard.LAVA,
            5 => SHazard.PIT,
            _ => SHazard.ENEMY
        };
    }
}
