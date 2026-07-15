extern alias Silksong;
using System;
using System.Collections;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Save;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.Playground;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
// A bare `SceneManager` binds to HK's Assembly-CSharp SceneManager (global namespace wins over the using); alias Unity's.
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HornetPlayer.HornetInHallownest.Modules;

// Instantiate Hero_Hornet via Addressables and fix up everything required for Hollow Knight interop.
public sealed class HornetSpawner : ModuleBase {
    private static GameObject? heroPrefab;
    private static Silksong::HeroController? realHero;

    // The live spawned HeroController, cached at spawn — read hot (per-frame, across modules), so avoid a
    // GetComponentInChildren per access. Normalized through Unity's null check so a destroyed hero reads as null.
    internal static Silksong::HeroController? RealHero => realHero ? realHero : null;

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
            if (heroPrefab) Log.Debug("[HornetSpawner] Hero_Hornet loaded via Addressables");
            return heroPrefab;
        }
    }

    private static Vector3 SpawnPosition => HeroController.UnsafeInstance
        ? HeroController.UnsafeInstance.transform.position
        : Vector3.zero;

    public override string Id => "spawn";

    // Hornet's presence tracks HK's scene state, trigger-based (no polling) via two hooks on HK's GameManager:
    // FinishedEnteringScene spawns her once a gameplay scene has placed the Knight; ReturnToMainMenu despawns her on
    // quit-to-menu. The Knight is HK's to manage; Hornet is a separate DontDestroyOnLoad body HK's teardown doesn't know
    // about, so without the despawn she'd linger (visible/active) on the menu. Spawn itself stays lazy — the actual
    // instantiation is Spawn(), also reachable via /spawn-real.
    public override void Initialize() {
        Detour(typeof(GameManager), "FinishedEnteringScene", OnEnteredScene);
        Detour(typeof(GameManager), "ReturnToMainMenu", OnReturnToMainMenu,
            typeof(GameManager.ReturnToMainMenuSaveModes), typeof(Action<bool>));

        // Like HK's own hero, Hornet has no place in non-gameplay scenes (End_Credits, cinematics). Despawn her there so
        // HK owns the camera/scene — else the camera keeps following her and the credits render off-screen (blackscreen).
        USceneManager.sceneLoaded += DespawnOutsideGameplay;

        // Hot-reload mid-game: FinishedEnteringScene already fired this scene, so spawn now if we're already placed.
        var knight = HeroController.UnsafeInstance;
        if (knight && knight.isHeroInPosition && !HornetRoot) Spawn();
    }

    protected override void OnDeinitialize() {
        USceneManager.sceneLoaded -= DespawnOutsideGameplay;
        Despawn();
    }

    private void OnEnteredScene(Action<GameManager> orig, GameManager self) {
        orig(self);
        if (!HornetRoot)
            try {
                Spawn();
                LogDebug("entered gameplay scene -> spawned Hornet");
                // Dev convenience (like the ability-kit grant): unlock every crest's tool slots so all tools are
                // equippable without hunting memory lockets. Runs per spawn (idempotent).
                ToolItemManagerBootstrap.UnlockAllCrestSlots();
            } catch (Exception e) {
                LogError(e.ToString());
            }

        // Apply a save stashed earlier this LoadGame (once PlayerData.instance exists). Self-clearing no-op otherwise.
        HornetSaveBridge.ApplyPending();
    }

    private IEnumerator OnReturnToMainMenu(
        Func<GameManager, GameManager.ReturnToMainMenuSaveModes, Action<bool>, IEnumerator> orig, GameManager self,
        GameManager.ReturnToMainMenuSaveModes mode, Action<bool> cb) {
        if (HornetRoot) {
            // Record the hero the player was actually on: ReturnToMainMenu autosaves, but we force the Knight active
            // below (camera handback), which would otherwise make that save record Knight and clobber the "was playing
            // Hornet" state. Consumed by the next Snapshot.
            HornetSaveBridge.SaveActiveOverride = HeroSwitch.HornetActive;
            // Hand the camera back to the Knight first: despawning while Hornet is active would leave CameraTarget.Update
            // dereferencing a destroyed transform every frame through the menu fade.
            HeroSwitch.SetActive(ActiveHero.Knight);
            Despawn();
            LogDebug("quit to menu -> despawned Hornet");
        }

        return orig(self, mode, cb);
    }

    private void DespawnOutsideGameplay(Scene scene, LoadSceneMode mode) {
        var gm = GameManager.UnsafeInstance;
        if (!HornetRoot || !gm) return;
        // sceneLoaded also fires for HK's additive gameplay transitions (room-to-room + Pantheon boss loads go through
        // GameManager.LoadSceneAsync(..., Additive)), which load the new scene while the PREVIOUS scene is still active.
        // gm.IsGameplayScene() reads the ACTIVE scene, so on an additive load it classifies the stale previous scene, not
        // the one that just loaded — so entering the first Pantheon boss (GG_Ghost_Xero, a gameplay scene) from the
        // non-gameplay GG_Boss_Door_Entrance misreads as non-gameplay and wrongly despawns Hornet. The non-gameplay scenes
        // this handler targets (credits/cinematics/menu) are loaded SINGLE-mode, so the loaded scene is already active.
        // Only act when the loaded scene is the active one; skip additive loads (Hornet stays — correct for gameplay).
        if (scene.name != USceneManager.GetActiveScene().name) return;
        if (gm.IsGameplayScene()) return;
        // EXCEPTION: stag travel (Cinematic_Stag_travel) is a HK "Cinematic" (non-gameplay) scene, but the hero RIDES
        // THROUGH it — HK's Knight is DontDestroyOnLoad and is never deactivated there; StagTravel.Start just plays the
        // full-screen cinematic then BeginSceneTransition's onward to the destination (gate "door_stagExit"). Despawning
        // Hornet here forced a full respawn on arrival (fresh HeroController/FSMs -> sprint & other carry-through state
        // reset). Mirror HK: let her ride through (she's already deparented to DDOL) and enter the destination normally.
        if (gm.IsStagTravelScene()) return;
        HeroSwitch.SetActive(ActiveHero.Knight); // hand camera back before despawn (CameraTarget would deref her)
        Despawn();
        LogDebug($"non-gameplay scene '{scene.name}' -> despawned Hornet");
    }

    // Instantiate the FULL prefab ACTIVE (no stripping) so every component's Awake/Start runs against our prefixed
    // Silksong.* types. Unity swallows per-component Awake exceptions into Player.log — that log is the "what's
    // missing" list (e.g. GameManager.instance null, input/camera singletons absent).
    internal static bool Spawn() {
        var prefab = HeroPrefab;
        if (!prefab) return false;
        GlobalsBootstrap.Ensure();
        Despawn(); // tear down any previous spawn

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
        realHero = heroController;

        var vignette = hornetInstance.transform.Find("Vignette");
        if (vignette) {
            vignette.gameObject.SetActive(false);
            // Untag it, or HK's SceneManager.Start does FindGameObjectWithTag("Vignette") ->
            // LocateFSM(go,"Darkness Control").SendEvent("RESET"), which breaks.
            vignette.gameObject.tag = "Untagged";
        }

        // Re-arm the global hero-box gate. HeroBox.Inactive is a STATIC bool that Die() sets true (no damage during the
        // death sequence) and DeathModule.Revive clears. A death that didn't complete the revive (e.g. one before this
        // code existed, or a mid-death hot-reload) leaves it stuck true across reloads — the Silksong assembly's statics
        // aren't reset by the mod hot-reload — so CheckForDamage skips forever and Hornet takes no damage. Reset on spawn.
        Silksong::HeroBox.Inactive = false;

        // NOTE: do NOT auto-activate Hornet here — the spawn coincides with HK's scene entry, and inerting the Knight
        // mid-entry breaks HK's entry handshake (it never finishes -> Hornet ends in nirvana). A "reload stays on Hornet"
        // feature must DEFER the switch until the Knight's entry has completed (isHeroInPosition + grounded).
        HeroSwitch.SetActive(HeroSwitch.Active);

        // Sync BEFORE BringUpHud so the health masks / silk meter appear at the Knight's capacity (maxHealth/silkMax),
        // not the bootstrap defaults. HK PD is loaded by now (post scene-entry) -> authoritative over HornetSaveData.
        PlayerDataSyncModule.SyncHKToSS();

        try {
            GameCamerasBootstrap.BringUpHud(true);
        } catch (Exception e) {
            Log.Error($"[HornetSpawner] BringUpHud: {e}");
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
        realHero = null;
        return true;
    }
}
