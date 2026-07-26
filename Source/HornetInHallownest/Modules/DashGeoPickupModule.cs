extern alias Silksong;
using System.Reflection;
using HornetInHallownest.HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.HornetInHallownest.Modules;

// HK geo is collected on contact with a "HeroBox"-tagged collider (GeoControl.OnTriggerEnter2D -> AddGeo). During a dash
// Hornet's kinematic HeroBox sweeps past ground geo between physics steps without firing OnTriggerEnter2D, so geo isn't
// picked up mid-dash. While dashing, sweep-overlap her HeroBox path and drive GeoControl's own collect for covered geo
public sealed class DashGeoPickupModule : ModuleBase {
    private readonly ContactFilter2D filter = new() { useTriggers = true, useLayerMask = false };
    private readonly Collider2D[] hits = new Collider2D[16];
    private MethodInfo? geoTrigger;
    private Collider2D? heroBox;
    private bool hadPrev;
    private Vector2 prevCenter;

    public override string Id => "geo-dash";

    public override void Initialize() {
    }

    protected override void OnDeinitialize() {
        heroBox = null;
        hadPrev = false;
    }

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        if (!heroBox) {
            var hb = hero.transform.Find("HeroBox");
            if (hb) heroBox = hb.GetComponent<Collider2D>();
        }

        if (!heroBox || !(hero.cState?.dashing ?? false)) {
            hadPrev = false;
            return;
        }

        var b = heroBox.bounds;
        var cur = (Vector2)b.center;
        var half = (Vector2)b.extents;
        // Sweep last frame's HeroBox to this frame's: the dash moves ~1 box-width/frame, so a point-in-time box tunnels.
        var min = (hadPrev ? Vector2.Min(prevCenter, cur) : cur) - half;
        var max = (hadPrev ? Vector2.Max(prevCenter, cur) : cur) + half;
        prevCenter = cur;
        hadPrev = true;

        // Ground geo sits at foot level (the HeroBox's bottom edge), so extend the pickup area down + a little each side.
        min.x -= 0.3f;
        max.x += 0.3f;
        min.y -= 0.9f;

        var n = Physics2D.OverlapArea(min, max, filter, hits);
        for (var i = 0; i < n; i++) {
            if (!hits[i]) continue;
            var geo = hits[i].GetComponent<GeoControl>();
            if (!geo) geo = hits[i].GetComponentInParent<GeoControl>();
            if (!geo) continue;
            geoTrigger ??= typeof(GeoControl)
                .GetMethod("OnTriggerEnter2D", BindingFlags.Instance | BindingFlags.NonPublic);
            geoTrigger?.Invoke(geo, [heroBox]);
        }
    }
}
