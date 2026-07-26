extern alias Silksong;
using System;
using System.Reflection;
using HornetInHallownest.Core;
using HornetInHallownest.Modules;
using HornetInHallownest.Save;
using HornetInHallownest.Playground;
using Modding;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest;

public class HornetInHallownest() : Mod("HornetInHallownest"), ITogglableMod, ILocalSettings<HornetSaveData>,
    IGlobalSettings<HornetGlobalSettings> {
    // The new lifecycle backbone (HornetInHallownest). Modules migrate into this ordered list one at a time; until a
    // system is migrated it keeps its old Install/Cleanup below. Initialize forward, Deinitialize reverse.
    private readonly ModuleHost moduleHost = new();

    internal ModuleHost Modules => moduleHost;

    private GameObject? playgroundHost;
    private HornetGlobalSettings globalSettings = new();

    private bool initialized;

    public static HornetInHallownest? LoadedInstance { get; private set; }

    // Persist Hornet's PlayerData inside HK's save file (per slot). The modding API invokes these at HK's native
    // save/load points (GameManager.SaveGame on bench/autosave; LoadGame on load) — see HornetSaveBridge.
    // Don't overwrite save data when the mod failed to initialize (null return -> API skips writing this slot).
    public HornetSaveData OnSaveLocal() {
        return (initialized ? HornetSaveBridge.Snapshot() : null)!;
    }

    public void OnLoadLocal(HornetSaveData s) {
        if (initialized) HornetSaveBridge.Stash(s);
    }

    public void OnLoadGlobal(HornetGlobalSettings s) {
        globalSettings = s;
    }

    public HornetGlobalSettings OnSaveGlobal() {
        return globalSettings; // Controls stay in sync: InputModule.Settings is the same object we set in Bootstrap.
    }

    public override string GetVersion() {
        return Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    public void Unload() {
        // Inventory-open-at-reload safety (frozen world / HK camera off / stuck isInventoryOpen) lives in
        // InventoryModule.OnDeinitialize (run by moduleHost.DeinitializeAll below). The spawn/despawn lifecycle hooks
        // (FinishedEnteringScene/ReturnToMainMenu/sceneLoaded) live in HornetSpawner, torn down by DeinitializeAll too.

        // Before anything despawns Hornet: if the player was on her, TP the Knight onto her spot. Cleanup below hands
        // control back to the Knight (un-mod safety) and a hot-reload re-activates it — otherwise it reactivates at its
        // stale pre-switch position and control snaps there (flew OOB on build). Must run here, BEFORE the module host
        // despawns Hornet (HornetRoot goes null), so it can't live in HeroSwitch.Cleanup.
        try {
            HeroSwitch.TpKnightToActiveHornet();
        } catch (Exception e) {
            Util.Log.Error($"[Unload] Knight TP: {e.Message}");
        }

        // New lifecycle backbone: tear migrated modules down in reverse registration order, before the legacy systems.
        moduleHost.DeinitializeAll();

        SilksongResources.Cleanup();
        GameCamerasBootstrap.Cleanup();
        UIManagerBootstrap.Cleanup();
        SilksongBootstrap.Cleanup();
        ToolItemManagerBootstrap.Cleanup();
        CollectableItemManagerBootstrap.Cleanup();
        ManagerSingletonBootstrap.Destroy("Silksong_InteractManager");
        ManagerSingletonBootstrap.Cleanup();
        GlobalSettingsBootstrap.Cleanup();
        SilksongPlayMaker.Cleanup();
        Stub.Cleanup();
        HeroSwitch.Cleanup();
        if (playgroundHost != null) Object.Destroy(playgroundHost);
        playgroundHost = null;
        LoadedInstance = null;
    }

    public override void Initialize() {
        if (LoadedInstance != null) return;
        LoadedInstance = this;

        Util.Log.SinkInfo = Log;
        Util.Log.SinkDebug = LogDebug;
        Util.Log.SinkError = LogError;

        Paths.SilksongInstallOverride = globalSettings.SilksongPath;

        // False means it changed startup-bound state this session already ran without; bail with a restart notice
        // instead of half-initializing.
        if (!SilksongSetup.EnsureInstalled()) {
            throw new Exception("Installed missing Silksong support files. Please restart Hollow Knight to finish loading HornetInHallownest.");
        }

        Bootstrap();
    }

    // The Silksong-referencing setup, split out so the install branch above never JITs a Silksong type: on the run that
    // installs the support files they aren't in Managed yet, so touching one would TypeLoad-fail before the copy runs.
    // Reached only once EnsureInstalled confirms they're present. Keep Initialize (and OnLoadGlobal) Silksong-free.
    private void Bootstrap() {
        // Must run before any MonoMod Hook is created (it locks MonoMod's platform detection).
        RosettaPlatformFix.Apply();

        playgroundHost = new GameObject("HornetInHallownest.Playground");
        Object.DontDestroyOnLoad(playgroundHost);
        var host = playgroundHost.AddComponent<HornetRuntime>();
        ModuleBase.Runtime = host;

        // FIRST: register Silksong's Addressables catalog into HK's empty runtime, BEFORE any Silksong code triggers a
        // (failing) addressables access. Once init fails in a process it stays poisoned (hasStartedInitialization=true,
        // empty locators) and can't be re-init'd, so this must run at Initialize on a fresh process — a hot-reload of
        // our DLL won't undo a poisoned Addressables runtime (Addressables lives in the engine DLL, one per process).
        SilksongAddressables.EnsureMounted();
        SilksongResources.Install(); // serve Silksong's Resources.Load from silksong-resources.bundle; log unserved misses
        SilksongPlayMaker.Apply();
        Stub.Install();
        HeroSwitch.Install();

        // New lifecycle backbone: register migrated modules in order, then init them. Initialize runs forward,
        // Deinitialize reverse — HornetSpawner is last so it despawns Hornet first on teardown.
        InputModule.Settings = globalSettings.Controls; // apply the loaded key bindings before the module reads them
        moduleHost.Add(new InputModule()); // FIRST: commit input before any module/HeroController reads inputActions
        moduleHost.Add(new PlayerLoopModule());
        moduleHost.Add(new BreakableFloorModule());
        moduleHost.Add(new DashGeoPickupModule());
        moduleHost.Add(new PogoModule());
        moduleHost.Add(new AnimationRemapModule());
        moduleHost.Add(new MinorFixesModule());
        moduleHost.Add(new PlayerDataSyncModule());
        moduleHost.Add(new CurrencyModule());
        moduleHost.Add(new FsmLookupModule());
        moduleHost.Add(new HeroSfxModule());
        moduleHost.Add(new DamageEnemiesModule());
        moduleHost.Add(new GameObjectLookupModule());
        moduleHost.Add(new ConveyorModule());
        moduleHost.Add(new FsmMethodCallRemapModule());
        moduleHost.Add(new HeroTargetModule());
        moduleHost.Add(new InventoryModule());
        moduleHost.Add(new DeathModule());
        moduleHost.Add(new HeroBroadcastModule());
        moduleHost.Add(new SceneFixesModule());
        moduleHost.Add(new ContactDamageModule());
        moduleHost.Add(new SwimModule());
        moduleHost.Add(new ShadowDashModule());
        moduleHost.Add(new NeedolinDreamNailModule());
        moduleHost.Add(new BenchModule());
        moduleHost.Add(new SceneTransitionModule());
        moduleHost.Add(new HornetSpawner());
        moduleHost.InitializeAll();

        initialized = true;
    }
}
