extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using HornetPlayer.HornetInHallownest.Util;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;
using SIHit = Silksong::IHitResponder;
using SHitInstance = Silksong::HitInstance;
using SHealthManager = Silksong::HealthManager;
using SDamageEnemies = Silksong::DamageEnemies;

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
    internal IHitResponder responder; // HK's IHitResponder (HealthManager / Breakable / BreakablePole / …)

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
            IgnoreInvulnerable = false
        };
        responder.Hit(hkHit);
        return SIHit.Response.DamageEnemy;
    }
}

internal static class EnemyDamageBridge {
    private static Hook? hook;
    private static Hook? takeDamageHook;
    private static Hook? soulGainHook;

    // Stand-in HealthManager for the WillDamageEnemyOptions notification (see OnDoDamage). Lives on an INACTIVE GO so
    // its Awake never runs; the only thing the relevant subscriber (DashStabNailAttack.OnWillDamageEnemy) reads from it
    // is GetComponent<NonBouncer>() — null here, so the recoil fires.
    private static SHealthManager? standInHm;


    internal static void Install() {
        var mi = typeof(Silksong::HitTaker).GetMethod(
            "GetHitResponders",
            BindingFlags.Public | BindingFlags.Static, null,
            [typeof(List<SIHit>), typeof(GameObject), typeof(int), typeof(HashSet<SIHit>)], null);
        if (mi == null) {
            Log.Error("[EnemyDamageBridge] HitTaker.GetHitResponders(4-arg) not found");
            return;
        }

        hook = new Hook(mi, (Hooked)OnGetHitResponders);

        // Mirror HK's DamageEnemies.DoDamage, which sends BOTH HitTaker.Hit (-> IHitResponder, our bridge above) AND
        // `FSMUtility.SendEventToGameObject(target, "TAKE DAMAGE")`. The latter drives HK objects that react via a
        // PlayMaker FSM rather than IHitResponder (geo rocks listen for "TAKE DAMAGE"; their FSM has no IHitResponder
        // and no `damages_enemy` read). Hornet's Silksong pipeline only sends Silksong's "TAKE DAMAGE" to Silksong FSMs,
        // so HK FSMs never hear it. We forward HK's "TAKE DAMAGE" to the target's HK FSMs after each Silksong DoDamage.
        var dd = typeof(Silksong::DamageEnemies).GetMethod("DoDamage", BindingFlags.Instance | BindingFlags.Public,
            null, [typeof(GameObject), typeof(bool)], null);
        if (dd != null) takeDamageHook = new Hook(dd, (DoDmgHooked)OnDoDamage);
        else Log.Error("[EnemyDamageBridge] DamageEnemies.DoDamage(GameObject,bool) not found");

        // Hook HK's HeroController.SoulGain: when HK enemies take a nail hit, HK's HealthManager.Hit calls
        // HeroController.instance.SoulGain() — that's HK's Knight, awarding HK soul. When Hornet is active,
        // redirect to Silksong's HeroController.SilkGain() so she gets silk instead.
        var soulMi = typeof(HeroController).GetMethod("SoulGain", BindingFlags.Public | BindingFlags.Instance);
        if (soulMi != null)
            soulGainHook = new Hook(soulMi, (Action<Action<HeroController>, HeroController>)OnSoulGain);
        else
            Log.Error("[EnemyDamageBridge] HeroController.SoulGain not found");
    }

    private static bool OnDoDamage(DoDmgOrig orig, Silksong::DamageEnemies self, GameObject target, bool isFirstHit) {
        var hit = orig(self, target, isFirstHit);
        if (target == null) return hit;
        // Only HK objects with a PlayMaker FSM care; FSMUtility no-ops otherwise but GetComponent gates the per-hit cost.
        if (target.GetComponent<PlayMakerFSM>() != null)
            FSMUtility.SendEventToGameObject(target, "TAKE DAMAGE");
        // HK enemies are bridged via an IHitResponder shim (not a Silksong HealthManager), so DamageEnemies.DoDamage
        // takes its non-HealthManager branch and never fires WillDamageEnemy*/the HealthManager-path notifications. Those
        // drive the attack-hit recoils (DashStabNailAttack.OnWillDamageEnemy -> sprintFSM "DASH RECOIL"/harpoon bounce).
        // Re-fire them here for a damaging hit on an HK enemy (HK HealthManager present). Firing on `self` only invokes
        // the subscribers bound to THIS attacker's damager, so it's targeted (a normal slash has none).
        if (hit && target.GetComponentInParent<HealthManager>() != null)
            FireWillDamageEnemy(self);
        return hit;
    }

    private static void FireWillDamageEnemy(SDamageEnemies self) {
        if (standInHm == null) {
            var go = new GameObject("hp_standin_hm");
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            standInHm = go.AddComponent<SHealthManager>();
        }

        self.GetFieldValue<Action>("WillDamageEnemy")?.Invoke();
        self.GetFieldValue<Action<SHealthManager, SHitInstance>>("WillDamageEnemyOptions")
            ?.Invoke(standInHm, new SHitInstance { Source = self.gameObject });
    }

    private static void OnGetHitResponders(Orig orig, List<SIHit> store, GameObject target, int depth,
        HashSet<SIHit> blackList) {
        orig(store, target, depth, blackList);
        if (target == null) return;
        // A real Silksong responder is present, or a bridge already on this exact GO -> nothing to do.
        // (A bridge found via walk-up on a PARENT does NOT cover the target's own HealthManager —
        // e.g. False Knight Head has its own HealthManager, but HitTaker walks up to the Body's
        // bridge and stops there.)
        for (var i = 0; i < store.Count; i++) {
            if (store[i] is Silksong::HealthManager)
                return;
            if (store[i] is HkEnemyHitBridge b && b.gameObject == target)
                return;
        }

        // HK target? Check the target GO first (its own HealthManager), then walk up.
        var resp = target.GetComponent<IHitResponder>()
                   ?? target.GetComponentInParent<IHitResponder>();
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
        soulGainHook?.Dispose();
        soulGainHook = null;
        if (standInHm != null) Object.Destroy(standInHm.gameObject);
        standInHm = null;
        // Our component type identity changes on a hot-reload; strip stale bridges so the next Initialize re-adds fresh
        // ones (and they don't linger as orphaned references on HK enemies).
        foreach (var b in Resources.FindObjectsOfTypeAll<HkEnemyHitBridge>())
            Object.Destroy(b);
    }

    private static void OnSoulGain(Action<HeroController> orig, HeroController self) {
        if (HeroSwitch.HornetActive && BundleSpike.RealHero != null)
            BundleSpike.RealHero.SilkGain();
        else
            orig(self);
    }

    private delegate void Orig(List<SIHit> store, GameObject target, int depth, HashSet<SIHit> blackList);

    private delegate void Hooked(Orig orig, List<SIHit> store, GameObject target, int depth, HashSet<SIHit> blackList);

    private delegate bool DoDmgOrig(Silksong::DamageEnemies self, GameObject target, bool isFirstHit);

    private delegate bool DoDmgHooked(DoDmgOrig orig, Silksong::DamageEnemies self, GameObject target, bool isFirstHit);
}
