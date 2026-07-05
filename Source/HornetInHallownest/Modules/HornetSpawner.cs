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

        try {
            GameCamerasBootstrap.BringUpHud(true);
        } catch (Exception e) {
            Log.Error($"[SpawnReal] BringUpHud: {e}");
        }

        // Wire gm.hero_ctrl + bare CameraController.camTarget so Silksong's hazard respawn flow
        // (PlayerDeadFromHazard → HazardRespawn) runs without NullRefs.
        SilksongBootstrap.SetHeroCtrl(heroController!);

        DamageEnemyProxy.Install();
        RoarLockBridge.Attach(heroController!); // HK boss roars lock Hornet via her Roar and Wound States FSM

        Log.Info("[HornetSpawner] instantiated");
        return true;
    }

    internal static bool Despawn() {
        if (!HornetRoot) return false;
        Object.DestroyImmediate(HornetRoot);
        HornetRoot = null;
        return true;
    }
}
