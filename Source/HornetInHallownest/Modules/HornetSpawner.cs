extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.Playground;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace HornetPlayer.HornetInHallownest.Modules;

// Instantiate Hero_Hornet via Addressables and fix up everything required for Hollow Knight interop.
public sealed class HornetSpawner : IModule {
    private static GameObject? heroPrefab;

    // The live spawned HeroController (in the DontDestroyOnLoad follower).
    internal static Silksong::HeroController? RealHero =>
        HornetRoot ? HornetRoot.GetComponentInChildren<Silksong::HeroController>() : null;

    // Root of the spawned Hornet subtree
    internal static GameObject? HornetRoot { get; private set; }

    // Hero_Hornet, loaded on first access via Addressables. Addressables pulls the full dependency closure and owns
    // every bundle, so there's no double-load conflict with the game's own runtime loads (GameManager.EnsureGlobalPool
    // -> "GlobalPool", etc.); the monoscripts redirect in SilksongCatalog binds all m_Script PPtrs to Silksong.*.
    private static GameObject? HeroPrefab {
        get {
            if (heroPrefab) return heroPrefab;
            SilksongCatalog.EnsureMounted();
            heroPrefab = Addressables.LoadAssetAsync<GameObject>("Hero_Hornet").WaitForCompletion();
            if (heroPrefab) Log.Info("[HornetSpawner] Hero_Hornet loaded via Addressables");
            return heroPrefab;
        }
    }

    private static Vector3 SpawnPosition => HeroController.UnsafeInstance
        ? HeroController.UnsafeInstance.transform.position
        : Vector3.zero;

    public string Id => "spawn";

    // Lazy: nothing at mod init. SpawnReal is driven by the /spawn-real route or the AutoSpawn coroutine.
    public void Initialize() {
    }

    public void Deinitialize() {
        Despawn();
    }

    // Instantiate the FULL prefab ACTIVE (no stripping) so every component's Awake/Start runs against our prefixed
    // Silksong.* types. Unity swallows per-component Awake exceptions into Player.log — that log is the "what's
    // missing" list (e.g. GameManager.instance null, input/camera singletons absent).
    internal static bool Spawn() {
        var prefab = HeroPrefab;
        if (!prefab) return false;
        GlobalsBootstrap.Ensure();

        // Tear down the previous spawn 
        if (HornetRoot) {
            Object.DestroyImmediate(HornetRoot);
            HornetRoot = null;
        }

        // Instantiate inactive so we can patch null fields before Awake runs, then activate.
        var staging = new GameObject("staging");
        staging.SetActive(false);
        var hornetInstance = Object.Instantiate(prefab, staging.transform);
        hornetInstance.name = "Hornet_Real";

        var heroController = hornetInstance.GetComponent<Silksong::HeroController>();
        // Child components cache HeroController.instance in their Awake
        typeof(Silksong::HeroController).SetFieldValue("_instance", heroController);

        hornetInstance.SetActive(false);
        hornetInstance.transform.SetParent(null, false);
        hornetInstance.transform.position = SpawnPosition;
        Object.DontDestroyOnLoad(hornetInstance);
        Object.DestroyImmediate(staging);

        using (SilksongContext.Enter()) {
            hornetInstance.SetActive(true);
        }

        HornetRoot = hornetInstance;

        // Disable Hornet's standalone screen Vignette 
        var vignette = hornetInstance.transform.Find("Vignette");
        if (vignette) {
            vignette.gameObject.SetActive(false);
            // Without this, HK's SceneManager.Start does FindGameObjectWithTag("Vignette") and
            // LocateFSM(go,"Darkness Control").SendEvent("RESET"), which breaks.
            vignette.gameObject.tag = "Untagged";
        }

        // Re-arm the global hero-box gate. HeroBox.Inactive is a STATIC bool that Die() sets true (no damage during the
        // death sequence) and HornetDeath.Revive clears. A death that didn't complete the revive (e.g. one before this
        // code existed, or a mid-death hot-reload) leaves it stuck true across reloads — the Silksong assembly's statics
        // aren't reset by the mod hot-reload — so CheckForDamage skips forever and Hornet takes no damage. Reset on spawn.
        Silksong::HeroBox.Inactive = false;

        // NOTE: do NOT auto-activate Hornet here — the spawn coincides with HK's scene entry, and inerting the Knight
        // mid-entry breaks HK's entry handshake (it never finishes -> Hornet ends in nirvana). A "reload stays on Hornet"
        // feature must DEFER the switch until the Knight's entry has completed (isHeroInPosition + grounded).
        HeroSwitch.SetActive(HeroSwitch.Active);

        // Seed BEFORE BringUpHud so the health masks / silk meter appear at the Knight's capacity (maxHealth/silkMax),
        // not the bootstrap defaults. HK PD is loaded by now (post scene-entry) -> authoritative over HornetSaveData.
        PlayerDataSync.Seed();

        try {
            GameCamerasBootstrap.BringUpHud(true);
        } catch (Exception e) {
            Log.Error($"[SpawnReal] BringUpHud: {e}");
        }

        // Wire gm.hero_ctrl + bare CameraController.camTarget so Silksong's hazard respawn flow
        // (PlayerDeadFromHazard → HazardRespawn) runs without NullRefs.
        SilksongBootstrap.SetHeroCtrl(heroController!);

        DamageEnemyProxy.Install();

        ApplyColliderHeight();

        Log.Info("[HornetSpawner] instantiated");
        return true;
    }

    // Collider-height toggle (dev knob, Digit8 via InputDriver). HK's level geometry is built for the Knight's collider
    // (0.50 × 1.28); Hornet's Silksong collider is 0.50 × 2.08 — same width but 0.80 taller (almost all upward) — so low
    // passages the Knight clears block her. When true we match the Knight's height so she fits the same corridors. This
    // is the terrain collider (col2d): HeroController never resizes it at runtime and it's separate from the HeroBox
    // hurtbox (combat untouched), so a one-time set is safe. Default ON (the traversal fix). Cost is cosmetic — her tall
    // sprite clips through genuinely low ceilings. (Scuttle can't help: it only resizes the hurtbox.)
    internal static bool KnightHeightCollider = true;

    // The two terrain-collider configs, feet-anchored. Width and feet-bottom are shared; only the height differs, and
    // the offset is COMPUTED from them (feetBottom + height/2) so there's no second hardcoded offset to keep in sync.
    private const float ColliderWidth = 0.50f; // both games' hero terrain collider width (identical, so gaps match)
    private const float KnightHeight = 1.28f; // HK Knight terrain-collider height — she fits the same corridors
    private const float HornetHeight = 2.08f; // Hornet's native Silksong terrain-collider height
    private const float FeetBottomLocalY = -1.55f; // collider bottom in hero-local Y (her sprite feet); both anchor here

    // Hornet's terrain collider (col2d), cached at spawn. Single source of truth for her body height — ContactDamageBridge
    // reads its live bounds to make hazards respect the current height (no duplicated magic number).
    internal static BoxCollider2D? TerrainCollider { get; private set; }

    // Apply the current KnightHeightCollider setting to the live hero's terrain collider, FEET-ANCHORED: keep the collider
    // bottom at her feet (FeetBottomLocalY) and only change the height — so ground-snap (feet-anchored) stays correct.
    internal static void ApplyColliderHeight() {
        var root = HornetRoot;
        if (!root) return;
        TerrainCollider = root.GetComponent<BoxCollider2D>();
        if (!TerrainCollider) {
            Log.Error("[HornetSpawner] no terrain BoxCollider2D on Hornet_Real — collider height not applied");
            return;
        }

        var height = KnightHeightCollider ? KnightHeight : HornetHeight;
        TerrainCollider.size = new Vector2(ColliderWidth, height);
        TerrainCollider.offset = new Vector2(0f, FeetBottomLocalY + height / 2f);
    }

    // Flip the collider-height setting and re-apply to the live hero. Returns the new state.
    internal static bool ToggleColliderHeight() {
        KnightHeightCollider = !KnightHeightCollider;
        ApplyColliderHeight();
        return KnightHeightCollider;
    }

    internal static bool Despawn() {
        if (!HornetRoot) return false;
        Object.DestroyImmediate(HornetRoot);
        HornetRoot = null;
        return true;
    }
}
