extern alias Silksong;
using System;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using Object = UnityEngine.Object;
using SHero = Silksong::HeroController;
using SWaterController = Silksong::HeroWaterController;
using SWaterRegion = Silksong::SurfaceWaterRegion;
using HeroTransitionState = Silksong::GlobalEnums.HeroTransitionState;

namespace HornetInHallownest.Modules;

// Two swim sources feed the same machinery (snap to surface + real HeroWaterController), both overlap-driven with a
// grace timeout for exit (no "left" callback to trust, and our surface snap teleports her, which makes a single
// OnTriggerExit2D fire spuriously; a stay+grace model is immune to that):
//   - HK acid: ContactDamageModule resolves ACID + dmg==0 (Isma's Tear zeroed it) -> NotifyInAcid.
//   - Neutral water (e.g. Abyss "Surface Water Region"): a trigger sensor on Hornet -> NotifyInWater. Always allowed
//     (no hazard). Acid regions (under "Acid Control v2") are left to the acid path so un-armoured acid still kills.
public sealed class SwimModule : ModuleBase {
    private const float GracePeriod = 0.2f; // time out this long after the last overlap
    private static readonly int terrainMask = LayerMask.GetMask("Terrain");

    // Feet-origin vs the surface, chosen by what looks good. Acid and neutral water happen to match.
    private const float SurfaceOffset = 0.9f;
    private const float WaterSurfaceOffset = 0.9f;

    private static SwimModule? instance;

    private float graceTimer;
    private bool floatingInWater;
    private SWaterController? activeWc;
    private Collider2D? waterRegion; // set only for neutral water (marks the source, so we restore its FSM on exit)

    private SWaterRegion? carrier; // stand-in region EnterWaterRegion requires + reads its swim params off
    private WaterSensor? sensor;

    public override string Id => "swim";

    public override void Initialize() {
        instance = this;
    }

    internal static void NotifyInAcid(GameObject acid) => instance?.OnOverlap(acid.GetComponentInParent<Collider2D>(), SurfaceOffset, water: false);
    internal static void NotifyInWater(Collider2D region) => instance?.OnOverlap(region, WaterSurfaceOffset, water: true);

    private void OnOverlap(Collider2D? region, float offset, bool water) {
        if (!region) return;
        var hero = HornetSpawner.Hornet;
        if (!hero || hero.transitionState != HeroTransitionState.WAITING_TO_TRANSITION) return;
        graceTimer = GracePeriod;
        if (floatingInWater) return;
        waterRegion = water ? region : null;
        if (water) SuppressRegionFsm(region); // stop HK's water FSM (Hero->Hornet via global var) from repositioning her
        Enter(hero, region, offset);
    }

    private PlayMakerFSM? suppressedFsm;

    // HK's "Surface Water Region" FSM pins var "Hero" (the global -> Hornet) to its own surface line each splash, which
    // fights our snap and evicts her over the trigger top (oscillation). Disable it while we own her; restore on exit.
    private void SuppressRegionFsm(Collider2D region) {
        foreach (var fsm in region.GetComponents<PlayMakerFSM>()) {
            if (fsm.FsmName != "Surface Water Region") continue;
            fsm.enabled = false;
            suppressedFsm = fsm;
            return;
        }
    }

    private void RestoreRegionFsm() {
        if (suppressedFsm) suppressedFsm.enabled = true;
        suppressedFsm = null;
    }

    // "Surface Water Region" volume that isn't HK acid (acid sits under "Acid Control v2" and is armour-gated elsewhere).
    internal static bool IsNeutralWater(Collider2D col) {
        var go = col.gameObject;
        if (!go.name.StartsWith("Surface Water Region", StringComparison.Ordinal)) return false;
        var parent = go.transform.parent;
        return !parent || !parent.name.StartsWith("Acid Control v2", StringComparison.Ordinal);
    }

    private void Enter(SHero hero, Collider2D region, float offset) {
        try {
            if (!hero.TryGetComponent<SWaterController>(out var wc)) return;
            if (!wc.IsInWater) {
                SnapToSurface(hero, region, offset);
                wc.EnterWaterRegion(EnsureCarrier());
                // so that TumbleOut's exit-recoil picks the correct side
                wc.SetFieldValue("waterBounds", region.bounds);
            }
            activeWc = wc;
            floatingInWater = true;
        } catch (Exception e) {
            LogError($"enter: {e}");
        }
    }

    // The controller never re-levels vertically, so this snap sets her depth. Prevent snapping into terrain.
    private void SnapToSurface(SHero hero, Collider2D region, float offset) {
        var surfaceY = region.bounds.max.y + offset;
        var p = hero.transform.position;
        var rise = surfaceY - p.y;
        var blocked = rise > 0f && Physics2D.Raycast(p, Vector2.up, rise, terrainMask).collider != null;
        if (!blocked) hero.transform.position = new Vector3(p.x, surfaceY, p.z);
        LogDebug($"enter '{region.name}' surfaceY={surfaceY:F2} fromY={p.y:F2} " +
                 (blocked ? "terrain overhead -> float in place" : "snapped to surface"));
    }

    public override void HornetActiveUpdate(SHero hero) {
        EnsureSensor(hero);
        if (!floatingInWater) return;
        if (!activeWc || !activeWc.IsInWater) { // controller self-exited (damage/scene)
            floatingInWater = false;
            activeWc = null;
            waterRegion = null;
            RestoreRegionFsm();
            return;
        }
        graceTimer -= Time.deltaTime; // refreshed each overlap; lapses once she leaves
        if (graceTimer <= 0f) Exit();
    }

    public override void HornetToggled(bool active) {
        if (!active && floatingInWater) Exit();
    }

    private void Exit() {
        floatingInWater = false;
        waterRegion = null;
        RestoreRegionFsm();
        if (activeWc && activeWc.IsInWater) activeWc.ExitWaterRegion();
        activeWc = null;
        LogDebug("exit water");
    }

    private void EnsureSensor(SHero hero) {
        if (sensor) return;
        sensor = hero.gameObject.AddComponent<WaterSensor>();
    }

    private SWaterRegion EnsureCarrier() {
        if (carrier != null) return carrier;
        var go = new GameObject("HkAcidWaterCarrier");
        go.SetActive(false); // inactive so SurfaceWaterRegion.Awake (null-prefab pooling) never runs
        Object.DontDestroyOnLoad(go);
        go.AddComponent<BoxCollider2D>(); // the Bounds getter needs one
        var region = carrier = go.AddComponent<SWaterRegion>();
        region.SetFieldValue("flowSpeed", 0f); // HK acid is static -> no horizontal current
        region.SetFieldValue("useSpaAnims", false); // regular swim anims, not the bathhouse (spa) set
        return region;
    }

    protected override void OnDeinitialize() {
        RestoreRegionFsm();
        floatingInWater = false;
        activeWc = null;
        waterRegion = null;
        graceTimer = 0f;
        if (sensor) Object.Destroy(sensor);
        sensor = null;
        if (carrier != null) {
            Object.Destroy(carrier.gameObject);
            carrier = null;
        }
        instance = null;
    }
}

// Lives on Hornet: her colliders overlapping a neutral-water region's trigger keep the swim engaged (stay-driven, so a
// teleport-snap's spurious OnTriggerExit2D can't end it; exit is the grace timeout instead).
internal sealed class WaterSensor : MonoBehaviour {
    private void OnTriggerStay2D(Collider2D other) {
        if (SwimModule.IsNeutralWater(other)) SwimModule.NotifyInWater(other);
    }
}
