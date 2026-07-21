extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.Playground;
using UnityEngine;
using Object = UnityEngine.Object;
using SIHit = Silksong::IHitResponder;
using SHitInstance = Silksong::HitInstance;
using SHealthManager = Silksong::HealthManager;
using SDamageEnemies = Silksong::DamageEnemies;

namespace HornetPlayer.HornetInHallownest.Modules;

// Bridge Hornet nail damage to HK.
// How it works:
// - DamageEnemies DoDamage collects targets via HitTaker.GetHitResponders and calls .Hit()
// - but only if the type is known silksong ReceivedDamageProxy / HitResponse / CurrencyObjectBase
// Solution:
// - inject a ReceivedDamageProxy component  that forwards the Silksong HitInstance to HK's IHitResponder.Hit.
// Breakables go through damages_enemy (see FsmLookpPModule)
public sealed class DamageEnemiesModule : ModuleBase {
    // Stand-in HealthManager for the WillDamageEnemyOptions notification (see OnDoDamage). Inactive GO so its Awake never
    // runs; the relevant subscriber (DashStabNailAttack.OnWillDamageEnemy) only reads GetComponent<NonBouncer>() (null).
    private SHealthManager? standInHm;

    public override string Id => "enemy-damage";

    public override void Initialize() {
        Detour(typeof(Silksong::HitTaker), "GetHitResponders", OnGetHitResponders,
            typeof(List<SIHit>), typeof(GameObject), typeof(int), typeof(HashSet<SIHit>));
        Detour(typeof(SDamageEnemies), "DoDamage", OnDoDamage, typeof(GameObject), typeof(bool));
        // HK enemy hits call HeroController.instance.SoulGain, redirect to Hornet's SilkGain when active.
        Detour(typeof(HeroController), "SoulGain", OnSoulGain);
    }

    protected override void OnDeinitialize() {
        if (standInHm) Object.Destroy(standInHm.gameObject);
        standInHm = null;
        // Our component type identity changes on a hot-reload; strip stale bridges so the next Initialize re-adds fresh
        // ones instead of leaving orphaned references on HK enemies.
        foreach (var b in Resources.FindObjectsOfTypeAll<HkEnemyHitBridge>()) Object.Destroy(b);
    }

    private static void OnGetHitResponders(Action<List<SIHit>, GameObject, int, HashSet<SIHit>> orig, List<SIHit> store,
        GameObject target, int depth, HashSet<SIHit> blackList) {
        orig(store, target, depth, blackList);
        if (!target) return;
        
        foreach (var r in store) {
            // a real Silksong responder is present 
            if (r is SHealthManager) return;
            // our bridge is already on this exact GO. Only same-GO counts: GetHitResponders walks up parents, so a
            // bridge on a parent doesn't cover the target's own responder (e.g. False Knight Head has its own HM).
            if (r is HkEnemyHitBridge b && b.gameObject == target) return;
        }

        var resp = target.GetComponentInParent<IHitResponder>(); // includes the target GO itself, then walks up
        if (resp as Component is not { } respComp) return; 
        var respGo = respComp.gameObject;
        var bridge = respGo.GetComponent<HkEnemyHitBridge>();
        if (!bridge) bridge = respGo.AddComponent<HkEnemyHitBridge>();
        bridge.Responder = resp;
        if (!blackList.Contains(bridge)) store.Add(bridge);
    }

    // Mirror HK's DamageEnemies.DoDamage
    private bool OnDoDamage(Func<SDamageEnemies, GameObject, bool, bool> orig, SDamageEnemies self, GameObject target,
        bool isFirstHit) {
        var hit = orig(self, target, isFirstHit);
        if (!target) return hit;
        FSMUtility.SendEventToGameObject(target, "TAKE DAMAGE"); 
        // required for sprint / harpoon recoil
        if (hit && target.GetComponentInParent<HealthManager>()) FireWillDamageEnemy(self);
        return hit;
    }

    private void FireWillDamageEnemy(SDamageEnemies self) {
        if (!standInHm) {
            var go = new GameObject("hp_standin_hm");
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            standInHm = go.AddComponent<SHealthManager>();
        }

        self.GetFieldValue<Action>("WillDamageEnemy")?.Invoke();
        self.GetFieldValue<Action<SHealthManager, SHitInstance>>("WillDamageEnemyOptions")
            ?.Invoke(standInHm, new SHitInstance { Source = self.gameObject });
    }

    private static void OnSoulGain(Action<HeroController> orig, HeroController self) {
        if (HeroSwitch.HornetActive && HornetSpawner.Hornet is { } hero) hero.SilkGain();
        else orig(self);
    }
}

// ReceivedDamageProxy so `is ReceivedDamageProxy` flips DoDamage's gate; the explicit IHitResponder.Hit re-implementation
// makes Silksong's `item.Responder.Hit(hit)` land here (the base would no-op) and forwards to HK's responder.
internal sealed class HkEnemyHitBridge : Silksong::ReceivedDamageProxy, SIHit {
    internal IHitResponder Responder = null!; // HK's IHitResponder; set right after AddComponent, before use

    SIHit.HitResponse SIHit.Hit(SHitInstance si) {
        // TODO: double check this is balanced
        var hkHit = new HitInstance {
            Source = si.Source,
            AttackType = (AttackTypes)(int)si.AttackType,
            SpecialType = SpecialTypes.None,
            DamageDealt = si.DamageDealt,
            Direction = si.Direction,
            MagnitudeMultiplier = si.MagnitudeMultiplier,
            Multiplier = si.Multiplier <= 0f ? 1f : si.Multiplier,
            MoveAngle = si.MoveAngle,
            IgnoreInvulnerable = false
        };
        Responder.Hit(hkHit);
        return SIHit.Response.DamageEnemy;
    }
}
