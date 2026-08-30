extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using UScene = UnityEngine.SceneManagement.Scene;
using SIHit = Silksong::IHitResponder;
using SHitInstance = Silksong::HitInstance;
using SHealthManager = Silksong::HealthManager;
using STinkEffect = Silksong::TinkEffect;

namespace HornetInHallownest.Modules;

// Bridge Hornet's attacks to HK
// - The nail slash `DamageEnemies` runs normally, but it needs a Silksong `HealthManager` -> create that on hit
// - Non-Hunter crests and harpoon rely on silksongs `TinkEffect` to be present. Those can't easily be created on demand
// (required before GetHitResponders runs) so we create them all on scene change.
public sealed class HitBridgeModule : ModuleBase {
    public override string Id => "enemy-damage";

    public override void Initialize() {
        Detour(typeof(Silksong::HitTaker), "GetHitResponders", OnGetHitResponders,
            typeof(List<SIHit>), typeof(GameObject), typeof(int), typeof(HashSet<SIHit>));
        Detour(typeof(SHealthManager), "Hit", OnHealthManagerHit, typeof(SHitInstance));
        // OnAwake, not Awake: HealthManager also runs it via IInitialisable, which hooking Awake alone would miss.
        Detour(typeof(SHealthManager), "OnAwake", OnHealthManagerAwake);
        Detour(typeof(Silksong::DamageEnemies), "DoEnemyDamageNailImbuement", OnNailImbuement,
            typeof(SHealthManager), typeof(SHitInstance));
        // HK enemy hits call HeroController.instance.SoulGain, redirect to Hornet's SilkGain when active.
        Detour(typeof(HeroController), "SoulGain", OnSoulGain);

        USceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    protected override void OnDeinitialize() {
        USceneManager.activeSceneChanged -= OnActiveSceneChanged;
        foreach (var b in Resources.FindObjectsOfTypeAll<HkEnemyHitBridge>()) Object.Destroy(b);
        foreach (var t in Resources.FindObjectsOfTypeAll<HkTinkBridge>()) Object.Destroy(t);
    }

    private static void OnActiveSceneChanged(UScene from, UScene to) => AddTinkEffectMirrorsInScene();

    private static void AddTinkEffectMirrorsInScene() {
        foreach (var tink in Object.FindObjectsByType<TinkEffect>(FindObjectsSortMode.None)) {
            var go = tink.gameObject;
            if (!go.TryGetComponent<HkTinkBridge>(out _)) go.AddComponent<HkTinkBridge>();
        }
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
        if (resp as Component is not { } respComp) {
            // No IHitResponder (e.g. Geo Rock): HK's DamageEnemies sends TAKE DAMAGE always
            FSMUtility.SendEventToGameObject(target, "TAKE DAMAGE");
            return;
        }

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

// Required to pass a GetComponent<TinkEffect> in DamageEnemies.OnTriggerEnter2D
internal sealed class HkTinkBridge : STinkEffect {
    protected override void Awake() {
        base.Awake();
        OnTinked = new UnityEvent();
        OnTinkedHeavy = new UnityEvent();
        OnTinkedUp = new UnityEvent();
        OnTinkedDown = new UnityEvent();
        OnTinkedLeft = new UnityEvent();
        OnTinkedRight = new UnityEvent();
        // without this, some spikes (Kings station roof rightmost) don't make sounds until after knight does it.
        if (TryGetComponent<TinkEffect>(out var hk)) blockEffect = hk.blockEffect;
    }
}
