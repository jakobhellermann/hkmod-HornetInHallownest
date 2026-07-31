extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using SIHit = Silksong::IHitResponder;
using SHitInstance = Silksong::HitInstance;
using SHealthManager = Silksong::HealthManager;

namespace HornetInHallownest.Modules;

// Bridge Hornet's attacks to HK enemies. The nail slash DamageEnemies runs normally, but for that it needs a Silksong
// HealthManager. So we create one that relays the Hit to the HK HealthManager
public sealed class HitBridgeModule : ModuleBase {
    public override string Id => "enemy-damage";

    public override void Initialize() {
        Detour(typeof(Silksong::HitTaker), "GetHitResponders", OnGetHitResponders,
            typeof(List<SIHit>), typeof(GameObject), typeof(int), typeof(HashSet<SIHit>));
        Detour(typeof(SHealthManager), "Hit", OnHealthManagerHit, typeof(SHitInstance));
        // might be called by IInitialisable?
        Detour(typeof(SHealthManager), "OnAwake", OnHealthManagerAwake);
        Detour(typeof(Silksong::DamageEnemies), "DoEnemyDamageNailImbuement", OnNailImbuement,
            typeof(SHealthManager), typeof(SHitInstance));
        // HK enemy hits call HeroController.instance.SoulGain, redirect to Hornet's SilkGain when active.
        Detour(typeof(HeroController), "SoulGain", OnSoulGain);
    }

    protected override void OnDeinitialize() {
        foreach (var b in Resources.FindObjectsOfTypeAll<HkEnemyHitBridge>()) Object.Destroy(b);
    }

    private void OnGetHitResponders(Action<List<SIHit>, GameObject, int, HashSet<SIHit>> orig, List<SIHit> store,
        GameObject target, int depth, HashSet<SIHit> blackList) {
        orig(store, target, depth, blackList);
        if (!target) return;

        foreach (var r in store) {
            if (r is HkEnemyHitBridge b) {
                if (b.gameObject == target) {
                    // already added
                    return;
                } else {
                    // on e.g. False Knight, body and head both have HealthManagers and get added.
                    // continue creating bridge healthmanager for the head
                    continue;
                }
            }

            if (r is SHealthManager) return;
        }

        var resp = target.GetComponentInParent<IHitResponder>();
        if (resp as Component is not { } respComp) return;
        
        var respGo = respComp.gameObject;
        if (!respGo.TryGetComponent<HkEnemyHitBridge>(out var bridge)) {
            bridge = respGo.AddComponent<HkEnemyHitBridge>();
            bridge.enabled = false;
        }

        bridge.Responder = resp;
        if (!blackList.Contains(bridge)) store.Add(bridge);
    }

    private static SIHit.HitResponse OnHealthManagerHit(Func<SHealthManager, SHitInstance, SIHit.HitResponse> orig,
        SHealthManager self, SHitInstance si) {
        if (self is HkEnemyHitBridge b) return b.ForwardHit(si);
        
        return orig(self, si);
    }

    // Skip hit bridge awake (TagDamagerTaker.Add, FSMs). TODO: set this up
    private static bool OnHealthManagerAwake(Func<SHealthManager, bool> orig, SHealthManager self) {
        if (self is not HkEnemyHitBridge) return orig(self);
        return false;
    }

    // Requries HealthManager.tagDamageTaker
    private void OnNailImbuement(Action<Silksong::DamageEnemies, SHealthManager, SHitInstance> orig,
        Silksong::DamageEnemies self, SHealthManager hm, SHitInstance hit) {
        if (hm is HkEnemyHitBridge) {
            LogDebug($"nail imbuement on '{hm.gameObject.name}' not implemented yet");
            return;
        }

        orig(self, hm, hit);
    }

    private static void OnSoulGain(Action<HeroController> orig, HeroController self) {
        if (HeroSwitch.HornetActive && HornetSpawner.Hornet is { } hero) hero.SilkGain();
        else orig(self);
    }
}

internal sealed class HkEnemyHitBridge : SHealthManager {
    internal IHitResponder Responder = null!;
    private NonBouncer? nonBouncer;
    
#pragma warning disable UNT0039
    private new void Awake() => nonBouncer = GetComponent<NonBouncer>();                                                                                                                                            
#pragma warning restore UNT0039

    internal SIHit.HitResponse ForwardHit(SHitInstance si) {
        var isUnhandledAttackType = (int) si.AttackType > (int) AttackTypes.NailBeam;
        var attackType = isUnhandledAttackType ? AttackTypes.Generic : (AttackTypes) si.AttackType;
        
        var hkHit = new HitInstance {
            Source = si.Source,
            AttackType = attackType,
            SpecialType = SpecialTypes.None,
            DamageDealt = si.DamageDealt,
            Direction = si.Direction,
            MagnitudeMultiplier = si.MagnitudeMultiplier,
            Multiplier = si.Multiplier <= 0f ? 1f : si.Multiplier,
            MoveAngle = si.MoveAngle,
            IgnoreInvulnerable = false
        };
        Responder.Hit(hkHit);
        
        if (si.Source && !(nonBouncer && nonBouncer.active)) {
            Silksong::FSMUtility.SendEventToGameObject(si.Source, "HIT LANDED");
            // necessary for reaper's bounce
            Silksong::FSMUtility.SendEventToGameObject(si.Source, "DEALT ACTUAL DAMAGE");
        }

        return SIHit.Response.DamageEnemy;
    }
}
