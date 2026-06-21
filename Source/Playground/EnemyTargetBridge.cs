using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// HK enemy AI keys "where is the hero" off HK's HeroController.instance == the KNIGHT (LineOfSightDetector LoS raycast,
// chase/recoil FSM targets). While Hornet is active the Knight is inert and DELIBERATELY NOT positionally glued to her
// — a stale Knight keeps every still-broken consumer visibly broken (so we find them via HeroControllerProbe), instead
// of a global glue silently "fixing" them all (which would also drop the Knight's live HeroBox into enemies -> double
// contact). So we redirect each query to Hornet's position one at a time, as the probe surfaces it.
//
// We do NOT reimplement each consumer (the HK decomp drifts from the live build — e.g. the probe shows
// LineOfSightDetector.Update reading BOTH get_instance AND get_SilentInstance, the decomp only `instance`). Instead, for
// the DURATION of the hooked call, we place the inert (rb.simulated=false) Knight at Hornet's position so HK's own
// verbatim logic targets her, then restore in a finally. Set+restore is synchronous within one call -> no net
// transform/physics change, no trigger callbacks (those fire on the physics sync, not on a transform write), and it's
// robust to HK-version drift since we never duplicate HK's logic.
internal static class EnemyTargetBridge {
    private static Hook? losHook;
    private static Hook? getHeroHook;

    internal static void Install() {
        var mi = typeof(LineOfSightDetector).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        if (mi == null) {
            Log.Error("[EnemyTargetBridge] LineOfSightDetector.Update not found");
            return;
        }

        losHook = new Hook(mi, (Action<Action<LineOfSightDetector>, LineOfSightDetector>)OnLosUpdate);
        Log.Info("[EnemyTargetBridge] installed: LineOfSightDetector.Update -> active hero position");

        // The DOMINANT enemy hero-caching path: the `GetHero` PlayMaker action stores HeroController.instance (= the
        // Knight) into a per-FSM LOCAL "Hero" var, ONCE at the enemy's Initialise (census: 2367 usages, all local).
        // Enemies then read that cached var for GetPosition/FaceObject/chase (and some CallMethodProper / Tk2dPlayAnimation,
        // which resolve on Hornet's real components / Tk2dClipShim — same as the global "Hero" repoint). So redirect
        // GetHero's result to Hornet too. NOTE: cached at Initialise -> enemies already in the scene keep the stale Knight
        // ref until they re-init (scene reload); fresh/reloaded enemies cache Hornet.
        var gh = typeof(GetHero).GetMethod("OnEnter", BindingFlags.Instance | BindingFlags.Public);
        if (gh == null) {
            Log.Error("[EnemyTargetBridge] GetHero.OnEnter not found");
            return;
        }

        getHeroHook = new Hook(gh, (Action<Action<GetHero>, GetHero>)OnGetHero);
        Log.Info("[EnemyTargetBridge] installed: GetHero -> active hero");
    }

    private static void OnGetHero(Action<GetHero> orig, GetHero self) {
        orig(self); // resolves + Finish()es; sets storeResult to HeroController.instance (the Knight)
        if (self.storeResult != null && HeroSwitch.ActiveHeroGameObject is { } hero)
            self.storeResult.Value = hero;
    }

    private static void OnLosUpdate(Action<LineOfSightDetector> orig, LineOfSightDetector self) {
        var knight = HeroController.UnsafeInstance;
        var hornet = BundleSpike.RealHero;
        if (!HeroSwitch.HornetActive || knight == null || hornet == null) {
            orig(self);
            return;
        }

        var kt = knight.transform;
        var saved = kt.position;
        kt.position = hornet.transform.position;
        try {
            orig(self);
        } finally {
            kt.position = saved;
        }
    }

    internal static void Cleanup() {
        losHook?.Dispose();
        losHook = null;
        getHeroHook?.Dispose();
        getHeroHook = null;
    }
}
