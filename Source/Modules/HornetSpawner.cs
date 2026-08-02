extern alias Silksong;
using System;
using System.Collections;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using HornetInHallownest.Save;
using HornetInHallownest.Util;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using ToolItemManager = Silksong::ToolItemManager;
// A bare `SceneManager` binds to HK's Assembly-CSharp SceneManager (global namespace wins over the using); alias Unity's.
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HornetInHallownest.Modules;

// Instantiate Hero_Hornet via Addressables and fix up everything required for Hollow Knight interop.
public sealed class HornetSpawner : ModuleBase {
    private static Silksong::HeroController? hornet;

    internal static Silksong::HeroController? Hornet => hornet ? hornet : null;

    internal static GameObject? HornetRoot { get; private set; }

    // Load via Addressables (not manual LoadFromFile): it owns the full dependency closure, so the game's own runtime
    // loads (GameManager.EnsureGlobalPool -> "GlobalPool") reuse those bundles instead of double-loading.
    private static GameObject? HeroPrefab {
        get {
            if (field) return field;
            SilksongAddressables.EnsureMounted();
            field = Addressables.LoadAssetAsync<GameObject>("Hero_Hornet").WaitForCompletion();
            if (field) Log.Debug("[HornetSpawner] Hero_Hornet loaded via Addressables");
            return field;
        }
    }

    private static Vector3 SpawnPosition => HeroController.UnsafeInstance
        ? HeroController.UnsafeInstance.transform.position
        : Vector3.zero;

    public override string Id => "spawn";

    // ReturnToMainMenu despawns her: she's a separate DontDestroyOnLoad body HK's teardown doesn't know about, so
    // without this she'd linger active on the menu.
    public override void Initialize() {
        Detour(typeof(GameManager), "FinishedEnteringScene", OnEnteredScene);
        Detour(typeof(GameManager), "ReturnToMainMenu", OnReturnToMainMenu,
            typeof(GameManager.ReturnToMainMenuSaveModes), typeof(Action<bool>));

        // Hornet has no place in non-gameplay scenes (End_Credits, cinematics). Despawn her there so HK owns the
        // camera/scene, else it keeps following her and the credits render off-screen (blackscreen).
        USceneManager.sceneLoaded += DespawnOutsideGameplay;

        // Hot-reload mid-game: the scene-entry hook won't fire again, so spawn now if the Knight is already placed.
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
            } catch (Exception e) {
                LogError(e.ToString());
            }

        HornetSaveBridge.ApplyPending();

        // Scene setup re-grabbed camera/vignette/HUD/"Hero" var to HK's Knight; re-point them at the active hero.
        HeroSwitch.ReassertEnvironment();
    }

    private IEnumerator OnReturnToMainMenu(
        Func<GameManager, GameManager.ReturnToMainMenuSaveModes, Action<bool>, IEnumerator> orig, GameManager self,
        GameManager.ReturnToMainMenuSaveModes mode, Action<bool> cb) {
        if (HornetRoot) {
            // ReturnToMainMenu autosaves, but we force the Knight active below (camera handback), which would otherwise
            // record Knight and clobber the "was playing Hornet" state. Capture the real hero first.
            HornetSaveBridge.SaveActiveOverride = HeroSwitch.HornetActive;
            // Hand the camera back to the Knight before despawn, else CameraTarget.Update derefs a destroyed transform
            // every frame through the menu fade.
            HeroSwitch.SetActive(ActiveHero.Knight);
            Despawn();
            LogDebug("quit to menu -> despawned Hornet");
        }

        return orig(self, mode, cb);
    }

    private void DespawnOutsideGameplay(Scene scene, LoadSceneMode mode) {
        var gm = GameManager.UnsafeInstance;
        if (!HornetRoot || !gm) return;
        // sceneLoaded also fires for HK's additive gameplay loads (room-to-room, Pantheon bosses), which load the new
        // scene while the previous one stays active. gm.IsGameplayScene() reads the active scene, so an additive load
        // misclassifies the stale previous scene (e.g. entering a Pantheon boss from a non-gameplay door -> wrong
        // despawn). Non-gameplay scenes we target (credits/cinematics/menu) load single-mode, so guard on loaded == active.
        if (scene.name != USceneManager.GetActiveScene().name) return;
        if (gm.IsGameplayScene()) return;
        // Stag travel (Cinematic_Stag_travel) is a non-gameplay scene the hero rides through: HK's Knight isn't
        // deactivated there, StagTravel.Start plays the cinematic then transitions onward. Despawning here forced a full
        // respawn on arrival (fresh HeroController/FSMs reset sprint + carry-through state); let her ride through instead.
        if (gm.IsStagTravelScene()) return;
        HeroSwitch.SetActive(ActiveHero.Knight); // hand camera back before despawn (CameraTarget would deref her)
        Despawn();
        LogDebug($"non-gameplay scene '{scene.name}' -> despawned Hornet");
    }

    // Instantiate the full prefab active so every component's Awake/Start runs against our prefixed Silksong.* types.
    // Unity swallows per-component Awake exceptions into Player.log (the "what's missing" list).
    internal static bool Spawn() {
        var prefab = HeroPrefab;
        if (!prefab) return false;
        GlobalsBootstrap.Ensure();
        // Minimal instance so ManagerSingleton<InteractManager>.Instance stops FindAnyObjectByType-scanning the whole scene
        // on every access. HeroController.CanNailCharge reads it 3x/frame, which tanks performance.
        ManagerSingletonBootstrap.RegisterBare(typeof(Silksong::InteractManager), "Silksong_InteractManager");
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

        HornetRoot = hornetInstance;
        hornet = heroController;

        PlayerDataSyncModule.SyncHKToSS();

        // Tools and crests are granted up front; only silk skills are playthrough-gated (via PlayerDataSyncModule).
        ToolItemManagerBootstrap.UnlockAllToolsSilently();
        ToolItemManager.UnlockAllCrests();
        ToolItemManagerBootstrap.UnlockAllCrestSlots();

        using (SilksongContext.Enter()) {
            hornetInstance.SetActive(true);
        }

        try {
            GameCamerasBootstrap.BringUpHud(true);
        } catch (Exception e) {
            Log.Error($"[HornetSpawner] BringUpHud: {e}");
        }

        var vignette = hornetInstance.transform.Find("Vignette");
        if (vignette) {
            vignette.gameObject.SetActive(false);
            // Untag it, or HK's SceneManager.Start does FindGameObjectWithTag("Vignette") ->
            // LocateFSM(go,"Darkness Control").SendEvent("RESET"), which breaks.
            vignette.gameObject.tag = "Untagged";
        }

        // Re-arm the global hero-box gate. HeroBox.Inactive is a static bool Die() sets true (no damage during death)
        // and DeathModule.Revive clears. An incomplete revive (mid-death hot-reload) leaves it stuck true across reloads
        // (the Silksong assembly's statics survive a mod hot-reload), so CheckForDamage skips forever and Hornet takes no
        // damage. Reset on spawn.
        Silksong::HeroBox.Inactive = false;

        // Do not auto-activate Hornet here: the spawn coincides with HK's scene entry, and inerting the Knight mid-entry
        // breaks HK's entry handshake (never finishes -> Hornet ends in nirvana). A "reload stays on Hornet" feature
        // must defer the switch until the Knight's entry completed (isHeroInPosition + grounded).
        HeroSwitch.SetActive(HeroSwitch.Active);

        // Wire gm.hero_ctrl + bare CameraController.camTarget so Silksong's hazard respawn flow
        // (PlayerDeadFromHazard -> HazardRespawn) runs without NullRefs.
        SilksongBootstrap.SetHeroCtrl(heroController!);

        ApplyColliderHeight();

        Log.Info("[HornetSpawner] instantiated");
        return true;
    }

    // HK geometry is built for the Knight's collider (0.50 x 1.28); Hornet's is 0.50 x 2.08 (0.80 taller), so low
    // passages the Knight clears block her. When true, shrink her terrain collider (col2d) to the Knight's height.
    // Safe as a one-time set: HeroController never resizes col2d at runtime and it's separate from the HeroBox hurtbox
    // (combat untouched). Cost is cosmetic (tall sprite clips low ceilings).
    internal static bool KnightHeightCollider = true;

    // The two terrain-collider configs, feet-anchored. Width and feet-bottom are shared; only the height differs, and
    // the offset is computed from them (feetBottom + height/2) so there's no second hardcoded offset to keep in sync.
    private const float ColliderWidth = 0.50f; // both games' hero terrain collider width (identical, so gaps match)
    private const float KnightHeight = 1.28f; // HK Knight terrain-collider height
    private const float HornetHeight = 2.08f; // Hornet's native Silksong terrain-collider height
    private const float FeetBottomLocalY = -1.55f; // collider bottom in hero-local Y (her sprite feet); both anchor here

    // Hornet's terrain collider (col2d), cached at spawn. Single source of truth for her body height (read live so
    // hazard checks respect the current height instead of duplicating the magic number).
    internal static BoxCollider2D? TerrainCollider { get; private set; }

    // Feet-anchored: keep the collider bottom at her feet (FeetBottomLocalY) and only change the height, so ground-snap
    // stays correct.
    internal static void ApplyColliderHeight() {
        var root = HornetRoot;
        if (!root) return;
        TerrainCollider = root.GetComponent<BoxCollider2D>();
        if (!TerrainCollider) {
            Log.Error("[HornetSpawner] no terrain BoxCollider2D on Hornet_Real - collider height not applied");
            return;
        }

        var height = KnightHeightCollider ? KnightHeight : HornetHeight;
        TerrainCollider.size = new Vector2(ColliderWidth, height);
        TerrainCollider.offset = new Vector2(0f, FeetBottomLocalY + height / 2f);
    }

    internal static bool ToggleColliderHeight() {
        KnightHeightCollider = !KnightHeightCollider;
        ApplyColliderHeight();
        return KnightHeightCollider;
    }

    internal static bool Despawn() {
        if (!HornetRoot) return false;
        Object.DestroyImmediate(HornetRoot);
        HornetRoot = null;
        hornet = null;
        return true;
    }
}
