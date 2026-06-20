extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SIHit = Silksong::IHitResponder;
using SHitInstance = Silksong::HitInstance;

namespace HornetPlayer.Playground;

// Bridge Hornet's (Silksong) nail damage onto HK hit-takers (enemies AND breakables).
//
// Hornet's attack pipeline (Silksong.DamageEnemies) resolves its target via HitTaker.GetHitResponders, which collects
// Silksong.IHitResponder components — Silksong enemies implement it via Silksong.HealthManager. HK hit-takers implement
// HK's IHitResponder (HealthManager, Breakable, BreakablePole — a DIFFERENT type, HK's unprefixed Assembly-CSharp), so
// nothing is found. Worse: DamageEnemies.DoDamage bails at `if (onlyDamageEnemies && !healthManager && !flag) return
// false;` BEFORE ever calling Responder.Hit — and that `flag` is only set for a few recognized proxy types
// (ReceivedDamageProxy / HitResponse / CurrencyObjectBase). So a plain IHitResponder shim isn't enough; the bridge must
// BE one of those types to pass the gate.
//
// Fix: a ReceivedDamageProxy-derived component (so `is ReceivedDamageProxy` flips the gate's `flag`) that re-implements
// IHitResponder.Hit to translate the Silksong HitInstance into HK's and forward to HK's IHitResponder.Hit. The HK
// target then reacts natively (enemy hit anim/geo/death, breakable topple/shatter). We inject it lazily from a
// HitTaker.GetHitResponders hook so it auto-covers every HK target Hornet hits, across scene loads, with no scene
// scanning. The hook is on Silksong.HitTaker only, so it fires exclusively for Hornet's pipeline (HK's Knight uses HK's
// own HitTaker in the other assembly).
//
// NOTE: this covers HK targets that implement IHitResponder (Hit-based). HK breakables that instead self-detect via
// their own OnTriggerEnter2D + read the attacker's `damages_enemy` FSM (BreakableObject, BreakablePoleSimple) are
// handled separately by DamagesEnemyFsmShim.
internal sealed class HkEnemyHitBridge : Silksong::ReceivedDamageProxy, SIHit {
    internal IHitResponder responder;   // HK's IHitResponder (HealthManager / Breakable / BreakablePole / …)

    // Explicit re-implementation: ReceivedDamageProxy already implements IHitResponder.Hit (non-virtual); listing the
    // interface again + an explicit member overrides the interface dispatch for this type, so Silksong's
    // `item.Responder.Hit(hit)` lands HERE instead of the base (which would no-op with no handlers registered).
    SIHit.HitResponse SIHit.Hit(SHitInstance si) {
        if (responder == null) return SIHit.Response.None;
        var hkHit = new HitInstance {
            Source = si.Source,
            // AttackTypes/SpecialTypes are global enums in BOTH assemblies; Nail=0 / None=0 align. Map AttackType by
            // value; force HK SpecialType=None (HK only defines None/Acid — Silksong's imbuement flags don't translate).
            AttackType = (AttackTypes)(int)si.AttackType,
            SpecialType = SpecialTypes.None,
            DamageDealt = si.DamageDealt,
            Direction = si.Direction,
            MagnitudeMultiplier = si.MagnitudeMultiplier,
            Multiplier = si.Multiplier <= 0f ? 1f : si.Multiplier,
            MoveAngle = si.MoveAngle,
            IgnoreInvulnerable = false,
        };
        responder.Hit(hkHit);
        return SIHit.Response.DamageEnemy;
    }
}

internal static class EnemyDamageBridge {
    private static Hook? hook;
    private static Hook? takeDamageHook;

    private delegate void Orig(List<SIHit> store, GameObject target, int depth, HashSet<SIHit> blackList);
    private delegate void Hooked(Orig orig, List<SIHit> store, GameObject target, int depth, HashSet<SIHit> blackList);

    private delegate bool DoDmgOrig(Silksong::DamageEnemies self, GameObject target, bool isFirstHit);
    private delegate bool DoDmgHooked(DoDmgOrig orig, Silksong::DamageEnemies self, GameObject target, bool isFirstHit);

    internal static void Install() {
        var mi = typeof(Silksong::HitTaker).GetMethod(
            "GetHitResponders",
            BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(List<SIHit>), typeof(GameObject), typeof(int), typeof(HashSet<SIHit>) }, null);
        if (mi == null) { Log.Error("[EnemyDamageBridge] HitTaker.GetHitResponders(4-arg) not found"); return; }
        hook = new Hook(mi, (Hooked)OnGetHitResponders);
        Log.Info("[EnemyDamageBridge] installed: HitTaker.GetHitResponders");

        // Mirror HK's DamageEnemies.DoDamage, which sends BOTH HitTaker.Hit (-> IHitResponder, our bridge above) AND
        // `FSMUtility.SendEventToGameObject(target, "TAKE DAMAGE")`. The latter drives HK objects that react via a
        // PlayMaker FSM rather than IHitResponder (geo rocks listen for "TAKE DAMAGE"; their FSM has no IHitResponder
        // and no `damages_enemy` read). Hornet's Silksong pipeline only sends Silksong's "TAKE DAMAGE" to Silksong FSMs,
        // so HK FSMs never hear it. We forward HK's "TAKE DAMAGE" to the target's HK FSMs after each Silksong DoDamage.
        var dd = typeof(Silksong::DamageEnemies).GetMethod("DoDamage", BindingFlags.Instance | BindingFlags.Public,
            null, new[] { typeof(GameObject), typeof(bool) }, null);
        if (dd != null) takeDamageHook = new Hook(dd, (DoDmgHooked)OnDoDamage);
        else Log.Error("[EnemyDamageBridge] DamageEnemies.DoDamage(GameObject,bool) not found");
    }

    // Identify what Hornet's slash actually overlaps. Once per distinct GameObject name so the log stays clean (no
    // per-frame spam) while still naming every new thing she hits — useful for tracking down "object X doesn't react"
    // (e.g. the stag Station Bell) when the object itself produces no log.
    private static readonly HashSet<string> hitSeen = new();

    private static bool OnDoDamage(DoDmgOrig orig, Silksong::DamageEnemies self, GameObject target, bool isFirstHit) {
        var hit = orig(self, target, isFirstHit);
        if (target == null) return hit;
        if (hitSeen.Add(target.name))
            Log.Info($"[EnemyDamageBridge] hit '{target.name}' layer={target.layer} hkFSM={(target.GetComponent<PlayMakerFSM>() != null)} hkResponder={(target.GetComponentInParent<IHitResponder>() != null)}");
        // Only HK objects with a PlayMaker FSM care; FSMUtility no-ops otherwise but GetComponent gates the per-hit cost.
        if (target.GetComponent<PlayMakerFSM>() != null)
            FSMUtility.SendEventToGameObject(target, "TAKE DAMAGE");
        return hit;
    }

    private static void OnGetHitResponders(Orig orig, List<SIHit> store, GameObject target, int depth, HashSet<SIHit> blackList) {
        orig(store, target, depth, blackList);
        if (target == null) return;
        // A real Silksong responder is present (Silksong enemy, or already-bridged) -> nothing to do.
        for (int i = 0; i < store.Count; i++)
            if (store[i] is Silksong::HealthManager || store[i] is HkEnemyHitBridge) return;
        // HK target? Walk up to the nearest HK IHitResponder (HealthManager / Breakable / BreakablePole). HK colliders
        // may sit on a child of the target root, so search parents.
        var resp = target.GetComponentInParent<IHitResponder>();
        var respGo = (resp as Component)?.gameObject;
        if (respGo == null) return;
        var bridge = respGo.GetComponent<HkEnemyHitBridge>();
        if (bridge == null) bridge = respGo.AddComponent<HkEnemyHitBridge>();
        bridge.responder = resp;
        if (blackList == null || !blackList.Contains(bridge)) store.Add(bridge);
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        takeDamageHook?.Dispose();
        takeDamageHook = null;
        // Our component type identity changes on a hot-reload; strip stale bridges so the next Initialize re-adds fresh
        // ones (and they don't linger as orphaned references on HK enemies).
        foreach (var b in Resources.FindObjectsOfTypeAll<HkEnemyHitBridge>())
            UnityEngine.Object.Destroy(b);
    }
}
