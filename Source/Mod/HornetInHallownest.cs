﻿extern alias Silksong;
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
    private readonly ModuleHost moduleHost = new();

    internal ModuleHost Modules => moduleHost;

    private GameObject? playgroundHost;
    private HornetGlobalSettings globalSettings = new();

    internal HornetGlobalSettings GlobalSettings => globalSettings;

    private bool initialized;

    public static HornetInHallownest? LoadedInstance { get; private set; }

    // Persist Hornet's PlayerData inside HK's save file (per slot).
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
        // TP Knight to hornet if hornet was active during hot reload. Must run before the module host despawns hornet,
        // otherwise HornetRool gets nullreference exceptions.
        try {
            HeroSwitch.TpKnightToActiveHornet();
        } catch (Exception e) {
            Util.Log.Error($"[Unload] Knight TP: {e.Message}");
        }

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

        if (!SilksongSetup.EnsureInstalled()) {
            throw new Exception("Installed missing Silksong support files. Please restart Hollow Knight to finish loading HornetInHallownest.");
        }

        Bootstrap();
    }

    // Initialization steps taken only after the initial restart was performed.
    private void Bootstrap() {
        // Must run before any MonoMod Hook is created
        RosettaPlatformFix.Apply();

        playgroundHost = new GameObject("HornetInHallownest.Playground");
        Object.DontDestroyOnLoad(playgroundHost);
        var host = playgroundHost.AddComponent<HornetRuntime>();
        ModuleBase.Runtime = host;

        // must run before any silksong code attempts to access addressables, since one failure permanently poisons the addressables somehow
        SilksongAddressables.EnsureMounted();
        SilksongResources.Install();
        SilksongPlayMaker.Apply();
        Stub.Install();
        HeroSwitch.Install();

        InputModule.Settings = globalSettings.Controls; // apply the loaded key bindings before the module reads them
        moduleHost.Add(new InputModule()); // first: before any module reads input actions
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
        moduleHost.Add(new HitBridgeModule());
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
