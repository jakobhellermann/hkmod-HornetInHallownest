extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.Playground;
using UnityEngine;
using Object = UnityEngine.Object;
using SHero = Silksong::HeroController;
using SWaterController = Silksong::HeroWaterController;
using SWaterRegion = Silksong::SurfaceWaterRegion;
using HeroTransitionState = Silksong::GlobalEnums.HeroTransitionState;

namespace HornetPlayer.HornetInHallownest.Modules;

public sealed class SwimModule : ModuleBase {
    private const float GracePeriod = 0.2f; // time out after last water region overlap -> exit
    private static readonly int terrainMask = LayerMask.GetMask("Terrain");

    // Feet-origin vs the acid surface. Chosen by what looks good.
    internal static float SurfaceOffset = 0.9f;

    private static SwimModule? instance;

    private float inWaterGraceTimer;
    private bool floatingInWater;
    private SWaterController? activeWc;
    private SWaterRegion? carrier; // stand-in region EnterWaterRegion requires + reads its swim params off

    public override string Id => "swim";

    public override void Initialize() {
        instance = this;
    }

    internal static void NotifyInAcid(GameObject acid) => instance?.OnAcidOverlap(acid);

    private void OnAcidOverlap(GameObject acid) {
        if (!acid) return;
        var hero = BundleSpike.RealHero;
        if (!hero || hero.transitionState != HeroTransitionState.WAITING_TO_TRANSITION) return;
        inWaterGraceTimer = GracePeriod;
        if (floatingInWater) return;
        var col = acid.GetComponentInParent<Collider2D>();
        if (col) Enter(hero, col);
    }

    private void Enter(SHero hero, Collider2D acid) {
        try {
            if (!hero.TryGetComponent<SWaterController>(out var wc)) return;
            if (!wc.IsInWater) {
                SnapToSurface(hero, acid);
                wc.EnterWaterRegion(EnsureCarrier());
                // so that TumbleOut's exit-recoil picks the correct side
                wc.SetFieldValue("waterBounds", acid.bounds);
            }
            activeWc = wc;
            floatingInWater = true;
        } catch (Exception e) {
            LogError($"enter: {e}");
        }
    }

    // The controller never re-levels vertically, so this snap sets her depth. Prevent snapping into terrain.
    private void SnapToSurface(SHero hero, Collider2D acid) {
        var surfaceY = acid.bounds.max.y + SurfaceOffset;
        var p = hero.transform.position;
        var rise = surfaceY - p.y;
        var blocked = rise > 0f && Physics2D.Raycast(p, Vector2.up, rise, terrainMask).collider != null;
        if (!blocked) hero.transform.position = new Vector3(p.x, surfaceY, p.z);
        LogDebug($"enter '{acid.name}' surfaceY={surfaceY:F2} fromY={p.y:F2} " +
                 (blocked ? "terrain overhead -> float in place" : "snapped to surface"));
    }

    public override void HornetActiveUpdate(SHero hero) {
        if (!floatingInWater) return;
        if (!activeWc || !activeWc.IsInWater) {
            floatingInWater = false;
            activeWc = null;
            return;
        }
        inWaterGraceTimer -= Time.deltaTime;
        if (inWaterGraceTimer <= 0f) Exit();
    }

    public override void HornetToggled(bool active) {
        if (!active && floatingInWater) Exit();
    }

    private void Exit() {
        floatingInWater = false;
        if (activeWc && activeWc.IsInWater) activeWc.ExitWaterRegion();
        activeWc = null;
        LogDebug("exit acid");
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
        floatingInWater = false;
        activeWc = null;
        inWaterGraceTimer = 0f;
        if (carrier != null) {
            Object.Destroy(carrier.gameObject);
            carrier = null;
        }
        instance = null;
    }
}
