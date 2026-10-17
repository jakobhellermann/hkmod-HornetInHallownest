extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using UScene = UnityEngine.SceneManagement.Scene;
using SIHit = Silksong::IHitResponder;
using SHitInstance = Silksong::HitInstance;
using SHealthManager = Silksong::HealthManager;
using STinkEffect = Silksong::TinkEffect;
using SSpecialTypes = Silksong::SpecialTypes;
using SAttackTypes = Silksong::AttackTypes;

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
        foreach (var tink in UnityApi.FindAll<TinkEffect>()) {
            var go = tink.gameObject;
            if (!go.TryGetComponent<HkTinkBridge>(out _)) go.AddComponent<HkTinkBridge>();
        }
    }

    private void OnGetHitResponders(Action<List<SIHit>, GameObject, int, HashSet<SIHit>> orig, List<SIHit> store,
        GameObject target, int depth, HashSet<SIHit> blackList) {
        orig(store, target, depth, blackList);
        if (!target) return;

        var alreadyBridged = false;
        foreach (var r in store) {
            if (r is HkEnemyHitBridge b) {
                // on e.g. False Knight, body and head both have HealthManagers and get added.
                // continue creating bridge healthmanager for the head
                if (b.gameObject == target) alreadyBridged = true;
                continue;
            }

            if (r is SHealthManager) return;
        }

        // HK's DamageEnemies sends TAKE DAMAGE to every target it hits, responder or not. Geo rocks break on it, and some
        // enemies only wake on it (sleeping Gruz Mother).
        FSMUtility.SendEventToGameObject(target, "TAKE DAMAGE");
        if (alreadyBridged) return;

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
        var isUnhandledAttackType = (int)si.AttackType > (int)AttackTypes.NailBeam;
        var attackType = isUnhandledAttackType ? AttackTypes.Generic : (AttackTypes)si.AttackType;
        // Allow e.g. silkspear to pierce enemies with a shield
        if ((si.SpecialType & SSpecialTypes.Piercer) != 0) attackType = AttackTypes.Spell;
        if (si.CriticalHit) ApplyCrit(ref si);

        var hkHit = new HitInstance {
            Source = si.Source,
            AttackType = attackType,
            SpecialType = SpecialTypes.None,
            DamageDealt = si.DamageDealt,
            Direction = si.Direction,
            MagnitudeMultiplier = si.MagnitudeMultiplier,
            Multiplier = si.Multiplier <= 0f ? 1f : si.Multiplier,
            MoveAngle = si.MoveAngle,
            IgnoreInvulnerable = true, // prevent iframes
        };
        Responder.Hit(hkHit);
        RewardHero(si);

        if (si.Source && !(nonBouncer && nonBouncer.active)) {
            Silksong::FSMUtility.SendEventToGameObject(si.Source, "HIT LANDED");
            // necessary for reaper's bounce
            Silksong::FSMUtility.SendEventToGameObject(si.Source, "DEALT ACTUAL DAMAGE");
        }

        return SIHit.Response.DamageEnemy;
    }

    // A Wanderer Crest crit only rolls on the attack; Silksong's HealthManager.TakeDamage scales and shows it.
    private void ApplyCrit(ref SHitInstance si) {
        si.DamageDealt = Mathf.RoundToInt(si.DamageDealt * Silksong::GlobalSettings.Gameplay.WandererCritMultiplier);
        si.MagnitudeMultiplier *= Silksong::GlobalSettings.Gameplay.WandererCritMagnitudeMult;
        var effectPrefab = Silksong::GlobalSettings.Gameplay.WandererCritEffect;
        if (!effectPrefab) return;
        var effect = Silksong::ObjectPoolExtensions.Spawn(effectPrefab, transform.position);
        Silksong::Extensions.SetRotation2D(effect.transform,
            si.GetOverriddenDirection(transform, SHitInstance.TargetType.Regular));
    }

    // Silksong's HealthManager.TakeDamage, which the HK enemy replaces, also pays the hero back for a nail hit: crest
    // effects through NailHitEnemy (Beast heal, evolved Hunter meter) and the Reaper's silk orbs.
    private void RewardHero(SHitInstance si) {
        var hero = Silksong::HeroController.instance;
        var isNailHit = si.AttackType is SAttackTypes.Nail or SAttackTypes.NailBeam ||
                        (si.AttackType == SAttackTypes.Heavy && si.Source &&
                         Silksong::DamageEnemies.IsNailAttackObject(si.Source));
        if (!hero || !isNailHit) return;
        hero.NailHitEnemy(this, si);

        var reaps = si is { AttackType: SAttackTypes.Nail } or { AttackType: SAttackTypes.Heavy, IsNailTag: true } &&
                    si.SilkGeneration != Silksong::HitSilkGeneration.None &&
                    Silksong::PlayerData.instance.CurrentCrestID == "Reaper" && hero.ReaperState.IsInReaperMode;
        if (!reaps) return;

        var direction = Silksong::DirectionUtils.GetCardinalDirection(
            si.GetActualDirection(transform, SHitInstance.TargetType.Regular));
        var degrees = direction * 90f;
        var payout = hero.GetReaperPayout();
        Silksong::FlingUtils.SpawnAndFling(new Silksong::FlingUtils.Config {
            Prefab = Silksong::GlobalSettings.Gameplay.ReaperBundlePrefab,
            AmountMin = payout,
            AmountMax = payout,
            SpeedMin = 25f,
            SpeedMax = 50f,
            AngleMin = degrees - 45f,
            AngleMax = degrees + 45f
        }, transform, Vector3.zero);

        var effectPrefab = Silksong::GlobalSettings.Effects.ReapHitEffectPrefab;
        if (!effectPrefab) return;
        var effect = Silksong::ObjectPoolExtensions.Spawn(effectPrefab, transform.position);
        Silksong::Extensions.SetRotation2D(effect.transform, direction % 2 == 1 ? 90f : 0f);
        effect.transform.localScale = new Vector3(direction == 2 ? -1f : 1f, direction == 3 ? -1f : 1f,
            effect.transform.localScale.z);
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
