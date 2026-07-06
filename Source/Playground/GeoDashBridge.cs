extern alias Silksong;
using System.Reflection;
using UnityEngine;

namespace HornetPlayer.Playground;

// HK geo is collected on contact with a "HeroBox"-tagged collider (GeoControl.OnTriggerEnter2D -> hero.AddGeo). During
// a dash Hornet's HeroBox (Kinematic Rigidbody2D) sweeps past ground geo between physics steps — kinematic bodies aren't
// swept by Continuous collision detection, so OnTriggerEnter2D never fires and geo isn't picked up mid-dash (the geo is
// then collected only once she slows, e.g. the sprint after). While she's dashing, actively overlap-check her HeroBox
// and drive GeoControl's own collect for any geo it covers — reuses HK's AddGeo + pickup sound + recycle, and its
// `activated`/`pickupStartTime` guards make re-invoking each frame idempotent (no double-collect).
internal static class GeoDashBridge {
    private static Collider2D? heroBox;
    private static readonly Collider2D[] hits = new Collider2D[16];
    private static MethodInfo? geoTrigger;
    private static readonly ContactFilter2D filter = new() { useTriggers = true, useLayerMask = false };
    private static Vector2 prevCenter;
    private static bool hadPrev;

    // Called per frame from HornetEnvironmentAdapter while Hornet is active; only does work during a dash. A point-in-time
    // OverlapBox still tunnels: the dash moves ~0.45u/frame ≈ the HeroBox width, so the box's per-frame coverage barely
    // connects and small geo slips through the gap. Sweep instead — overlap the whole rectangle from last frame's HeroBox
    // to this frame's, which covers the entire dash path so nothing is missed.
    // Cache the HeroBox collider once, at spawn (SilksongBootstrap.SetHeroCtrl) — keeps the string lookup out of Tick.
    internal static void CacheHeroBox(Silksong::HeroController hero) {
        heroBox = hero.transform.Find("HeroBox")?.GetComponent<Collider2D>();
    }

    internal static void Tick(Silksong::HeroController hero) {
        if (heroBox == null || !(hero.cState?.dashing ?? false)) {
            hadPrev = false;
            return;
        }

        var b = heroBox.bounds;
        var cur = (Vector2)b.center;
        var half = (Vector2)b.extents;
        var min = (hadPrev ? Vector2.Min(prevCenter, cur) : cur) - half;
        var max = (hadPrev ? Vector2.Max(prevCenter, cur) : cur) + half;
        prevCenter = cur;
        hadPrev = true;

        // Ground geo sits at foot level — right at (or just below) the HeroBox's bottom edge, so the raw box misses it.
        // Extend the pickup area downward (and a little each side) so geo she dashes over is reliably covered.
        min.x -= 0.3f;
        max.x += 0.3f;
        min.y -= 0.9f;

        var n = Physics2D.OverlapArea(min, max, filter, hits);
        for (var i = 0; i < n; i++) {
            if (hits[i] == null) continue;
            var geo = hits[i].GetComponent<GeoControl>() ?? hits[i].GetComponentInParent<GeoControl>();
            if (geo == null) continue;
            geoTrigger ??= typeof(GeoControl)
                .GetMethod("OnTriggerEnter2D", BindingFlags.Instance | BindingFlags.NonPublic);
            geoTrigger?.Invoke(geo, new object[] { heroBox });
        }
    }

    internal static void Cleanup() {
        heroBox = null;
        hadPrev = false;
    }
}
