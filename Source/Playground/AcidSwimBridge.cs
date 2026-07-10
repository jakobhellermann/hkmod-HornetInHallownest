extern alias Silksong;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
using SWaterController = Silksong::HeroWaterController;
using SWaterRegion = Silksong::SurfaceWaterRegion;

namespace HornetPlayer.Playground;

// Float + swim Hornet on the surface of HK acid (like the Knight with Isma's Tear), instead of her passing through it.
//
// HK acid is a DamageHero / "damages_hero" hazard, NOT a Silksong SurfaceWaterRegion, so Hornet's real HeroWaterController
// (present on the prefab and running, but idle) never gets an EnterWaterRegion call: no buoyancy, gravity keeps pulling her
// down, and Isma's Tear only zeroes the damage (HK's per-box "Acid Armour Check" FSM sets DamageHero.damageDealt=0).
// Result: no death, but she sinks straight through. Here we detect the acid overlap and drive her real water controller
// ourselves — buoyancy, swim movement, jump-out and the swim animations all come from HeroWaterController; we only supply
// the entry/exit edges and the surface Y.
//
// Detection reuses HK's OWN armour check (no separate PlayerData read): ContactDamageBridge already resolves ssHazard==ACID
// for HK acid, and dmg==0 there means the Acid Armour Check FSM zeroed it -> she HAS Isma's Tear. So (ACID && dmg==0) is
// exactly "in acid, armoured" -> swim. (ACID && dmg>0) = no armour -> the existing lethal path kills her, same as HK.
//
// Fully EVENT-DRIVEN: the only driver is NotifyInAcid, called from HeroBox.CheckForDamage (a physics overlap callback) and
// so fired ONLY while she overlaps acid. Enter happens there on the rising edge; a self-terminating watch coroutine (runs
// only while she floats) handles exit — "no more overlap" has no callback, so it counts down a short grace window that the
// overlap keeps refreshing. Nothing of ours runs per-frame while she's away from acid.
internal static class AcidSwimBridge {
    // The overlap callback (NotifyInAcid) fires at FIXED-update rate, not every render frame, so "left the acid" can't be
    // read as a single missing frame. Each overlap refreshes this window; the watch coroutine exits once it fully lapses.
    private const float GracePeriod = 0.2f;
    private static float inAcidGrace;
    private static Collider2D? currentAcid;
    private static bool floating;

    // A single reusable SurfaceWaterRegion, used ONLY as the data carrier EnterWaterRegion reads (Color/FlowSpeed/
    // UseSpaAnims/Bounds/rotation). Kept on an INACTIVE GO so its Awake (which pools now-null splash prefabs) never runs;
    // we set the three serialized fields by reflection. Its Bounds are meaningless (zero, on the inactive GO) — that only
    // feeds the exit-recoil direction if she leaves mid-swim rather than jumping out, an accepted edge case.
    private static SWaterRegion? carrier;
    private static PlaygroundHost? host;

    private static FieldInfo? colorField;
    private static FieldInfo? flowSpeedField;
    private static FieldInfo? useSpaField;

    // Tuning knob: where her transform origin sits relative to the acid surface (top of the HK acid collider).
    // 0 = origin exactly on the surface line. Adjustable live via POST /acid-offset for fitment.
    internal static float SurfaceOffset;

    // How far ABOVE her current position the surface may be and still snap her to it (catches fast-fall overshoot where
    // she's detected just past the surface). Beyond this she's treated as "entered from below" and floats in place — a
    // large upward teleport is what risked pushing her into a wall near the edge.
    private const float SnapUpTolerance = 0.5f;

    private static PlaygroundHost? Host {
        get {
            if (host == null) host = Object.FindAnyObjectByType<PlaygroundHost>();
            return host;
        }
    }

    // Called by ContactDamageBridge for every armoured acid overlap frame (ACID hazard whose damage Isma's Tear zeroed).
    internal static void NotifyInAcid(GameObject acid) {
        if (acid == null || !HeroSwitch.HornetActive) return;
        inAcidGrace = GracePeriod;
        if (floating) return; // already swimming; ExitWatch handles the rest — no need to re-resolve the collider
        currentAcid = acid.GetComponent<Collider2D>() ?? acid.GetComponentInParent<Collider2D>();
        Enter();
    }

    private static void Enter() {
        try {
            var hero = BundleSpike.RealHero;
            if (hero == null || currentAcid == null) return;
            var wc = hero.GetComponent<SWaterController>();
            if (wc == null) return;
            var runner = Host;
            if (runner == null) {
                Log.Error("[AcidSwim] no PlaygroundHost to run exit-watch; skipping swim");
                return;
            }

            if (!wc.IsInWater) {
                var b = currentAcid.bounds;
                var surfaceY = b.max.y + SurfaceOffset;

                // Snap to the surface so she floats on top instead of wherever she'd fallen to, then hand off to her real
                // water controller (it flips gravity off, relinquishes control, IsSwimming(), zeroes velocity and drives
                // swim/jump-out). A hard Y-teleport ignores collision, so an UPWARD snap could push her into a wall/ceiling
                // near the edge. She almost always falls in from above, so only snap DOWN to the surface (plus a small
                // up-tolerance for fast-fall overshoot); if she's genuinely below it (entered from the side), float in
                // place — she can swim/jump up — rather than risk teleporting her up into terrain.
                var p = hero.transform.position;
                var snapped = surfaceY <= p.y + SnapUpTolerance;
                if (snapped) hero.transform.position = new Vector3(p.x, surfaceY, p.z);

                EnsureCarrier();
                wc.EnterWaterRegion(carrier);

                Log.Debug($"[AcidSwim] enter '{currentAcid.name}' surfaceY={surfaceY:F2} fromY={p.y:F2} " +
                          (snapped ? "snapped to surface" : "below surface -> float in place (no up-teleport)"));
            }

            floating = true;
            runner.StartCoroutine(ExitWatch(wc));
        } catch (Exception e) {
            Log.Error($"[AcidSwim] enter: {e}");
        }
    }

    // Runs ONLY while she floats. Exits once the overlap grace lapses (she left the acid), or the controller left the
    // water on its own (took damage / scene change -> OnTakenDamage/OnNextSceneLoaded already called ExitWaterRegion).
    private static IEnumerator ExitWatch(SWaterController wc) {
        while (floating) {
            yield return null;
            if (!floating) yield break; // cleared externally (Cleanup)
            if (!wc.IsInWater) {
                // Controller self-exited; nothing more to do (don't double-call ExitWaterRegion).
                floating = false;
                Log.Debug("[AcidSwim] controller self-exited water (damage/scene)");
                yield break;
            }
            inAcidGrace -= Time.deltaTime;
            if (inAcidGrace <= 0f) {
                Exit(wc);
                yield break;
            }
        }
    }

    private static void Exit(SWaterController wc) {
        floating = false;
        if (wc.IsInWater) wc.ExitWaterRegion();
        Log.Debug("[AcidSwim] exit acid");
    }

    private static void EnsureCarrier() {
        if (carrier != null) return;
        var go = new GameObject("HkAcidWaterCarrier");
        go.SetActive(false); // keep inactive so SurfaceWaterRegion.Awake (null-prefab pooling) never runs
        Object.DontDestroyOnLoad(go);
        go.AddComponent<BoxCollider2D>(); // so the Bounds getter's GetComponent<BoxCollider2D> doesn't NPE
        carrier = go.AddComponent<SWaterRegion>();

        colorField ??= typeof(SWaterRegion).GetField("color", BindingFlags.NonPublic | BindingFlags.Instance);
        flowSpeedField ??= typeof(SWaterRegion).GetField("flowSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
        useSpaField ??= typeof(SWaterRegion).GetField("useSpaAnims", BindingFlags.NonPublic | BindingFlags.Instance);
        colorField?.SetValue(carrier, new Color(0.55f, 0.75f, 0.2f, 1f)); // acid-green droplet tint (cosmetic)
        flowSpeedField?.SetValue(carrier, 0f); // HK acid is static
        useSpaField?.SetValue(carrier, false); // regular swim anims, not the spa set
    }

    internal static void Cleanup() {
        floating = false; // stops any running ExitWatch on its next frame
        if (carrier != null) {
            Object.Destroy(carrier.gameObject);
            carrier = null;
        }
        host = null;
        inAcidGrace = 0f;
        currentAcid = null;
    }
}
