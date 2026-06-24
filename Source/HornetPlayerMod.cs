extern alias Silksong;
using System;
using System.Collections;
using System.Reflection;
using HornetPlayer.DevServer;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Modules;
using HornetPlayer.HornetInHallownest.Validation;
using HornetPlayer.HornetInHallownest.Validation.Scenarios;
using HornetPlayer.Playground;
using Modding;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer;

public class HornetPlayerMod : Mod, ITogglableMod {
    // Distinct from Silksong's DevUtils server (8200) so both games can be debugged at once.
    private const int DebugServerPort = 8201;

    // The new lifecycle backbone (HornetInHallownest). Modules migrate into this ordered list one at a time; until a
    // system is migrated it keeps its old Install/Cleanup below. Initialize forward, Deinitialize reverse.
    private readonly ModuleHost moduleHost = new();

    private GameObject? playgroundHost;
    private ValidationRunner? validation;

    public static HornetPlayerMod? LoadedInstance { get; private set; }

    public override string GetVersion() {
        return Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    public void Unload() {
        // Unload-safe inventory: if the inventory was open at reload, its DisplayFrozenCamera.Freeze left HK's main
        // camera disabled (Camera.main.enabled=false, showing a frozen snapshot). A hot-reload despawns the inventory
        // before it can Unfreeze -> the main camera stays off -> black screen. Re-enable HK's main camera on teardown.
        try {
            var gc = GameCameras.instance;
            if (gc != null && gc.mainCamera != null && !gc.mainCamera.enabled) {
                gc.mainCamera.enabled = true;
                Playground.Log.Info(
                    $"[Unload] re-enabled HK mainCamera '{gc.mainCamera.name}' (left disabled by inventory DisplayFrozenCamera.Freeze)");
            }
        } catch (Exception e) {
            Playground.Log.Error($"[Unload] camera restore: {e.Message}");
        }

        // Hot-reload while inventory was open leaves isInventoryOpen=true on Silksong's PlayerData -> IsPaused()=true
        // -> CanInput()=false -> hero stuck. Clear it + restore timeScale.
        try {
            var pd = Silksong::PlayerData.instance;
            if (pd != null && pd.isInventoryOpen) {
                pd.isInventoryOpen = false;
                Playground.Log.Info("[Unload] cleared stuck isInventoryOpen");
            }

            if (Time.timeScale <= 0.0001f) Time.timeScale = 1f;
        } catch (Exception e) {
            Playground.Log.Error($"[Unload] inventory reset: {e.Message}");
        }

        finishedEnteringHook?.Dispose();
        finishedEnteringHook = null;
        returnToMenuHook?.Dispose();
        returnToMenuHook = null;

        // New lifecycle backbone: tear migrated modules down in reverse registration order, before the legacy systems.
        moduleHost.DeinitializeAll();

        ResourcesShim.Cleanup();
        GameObjectFindShim.Cleanup();
        AddressablesBootstrap.Cleanup();
        GameCamerasBootstrap.Cleanup();
        UIManagerBootstrap.Cleanup();
        SilksongBootstrap.Cleanup();
        DamageEnemyProxy.Cleanup();
        ToolItemManagerBootstrap.Cleanup();
        CollectableItemManagerBootstrap.Cleanup();
        ManagerSingletonBootstrap.Cleanup();
        GlobalSettingsBootstrap.Cleanup();
        PlayMakerFix.Cleanup();
        Stub.Cleanup();
        InputBridge.Cleanup();
        HornetEnvironmentAdapter.Cleanup();
        HeroSwitch.Cleanup();
        EnemyDamageBridge.Cleanup();
        DamagesEnemyFsmShim.Cleanup();
        PogoNonBounceShim.Cleanup();
        ContactDamageBridge.Cleanup();
        HornetDeath.Cleanup();
        RespawnBridge.Cleanup();
        CoroutineRedirect.Cleanup();
        HornetBench.Cleanup();
        HeroSfxShim.Cleanup();
        FreezeMomentFix.Cleanup();
        FsmTracer.Cleanup();
        GetComponentShim.Cleanup();
        HeroControllerProbe.Cleanup();
        EnemyTargetBridge.Cleanup();
        HeroProxy.Cleanup();
        Tk2dClipShim.Cleanup();
        InventoryPauseBridge.Cleanup();
        CallMethodProperFix.Cleanup();
        PlayMakerWarningContext.Cleanup();
        DebugServer.Stop();
        if (playgroundHost != null) Object.Destroy(playgroundHost);
        playgroundHost = null;
        LoadedInstance = null;
    }

    public override void Initialize() {
        if (LoadedInstance != null) return;
        LoadedInstance = this;

        Playground.Log.SinkInfo = Log;
        Playground.Log.SinkDebug = LogDebug;
        Playground.Log.SinkError = LogError;

        playgroundHost = new GameObject("HornetPlayer.Playground");
        Object.DontDestroyOnLoad(playgroundHost);
        var host = playgroundHost.AddComponent<PlaygroundHost>();

        validation = new ValidationRunner(moduleHost)
            .Register(new SpawnSanityScenario());

        PlaygroundRoutes.Register();
        DebugServer.MapPost("/spawn-real", _ => HornetSpawner.Spawn() ? new { ok = true } : new { ok = false });
        DebugServer.MapPost("/despawn-real", _ => HornetSpawner.Despawn()
            ? new { ok = true, despawned = true }
            : new { ok = true, note = "nothing to despawn" });
        DebugServer.MapGet("/scan-missing", _ => BundleSpike.ScanMissing());
        DebugServer.MapGet("/hero-state", _ => BundleSpike.HeroState());
        DebugServer.MapGet("/toolmgr", _ => ToolItemManagerBootstrap.Diag());
        DebugServer.MapGet("/colmgr", _ => CollectableItemManagerBootstrap.Ensure());
        DebugServer.MapGet("/diag-input", _ => BundleSpike.DiagInput());
        DebugServer.MapGet("/fsm-state", _ => BundleSpike.FsmState());
        DebugServer.MapGet("/fsm-dump", req => BundleSpike.FsmDump(req["name"] ?? "Sprint"));
        DebugServer.MapGet("/fsm-dump-any", req => BundleSpike.FsmDumpAny(req["name"] ?? "health_display"));
        DebugServer.MapGet("/fsm-dump-hk", req => BundleSpike.FsmDumpHk(req["name"] ?? "Bell Control"));
        DebugServer.MapPost("/fsm-event",
            req => BundleSpike.SendFsmEvent(req["name"] ?? "health_display", req["event"] ?? "HUD APPEAR RESET"));
        DebugServer.MapGet("/fsm-list", req => BundleSpike.FsmList(req["path"] ?? "Hud Canvas"));
        DebugServer.MapPost("/hud-health",
            (req, respond) => BundleSpike.DriveHealthHud(respond)); // drive the health-mask appear chain over frames
        DebugServer.MapGet("/find-event-senders", req => BundleSpike.FindEventSenders(req["event"] ?? "SHOW HP"));
        DebugServer.MapGet("/fsm-state-actions",
            req => BundleSpike.DumpStateActions(req["name"] ?? "health_display", req["state"] ?? "First Pause",
                req["go"]));
        DebugServer.MapGet("/fsm-vars", req => BundleSpike.FsmVars(req["name"] ?? "Bind", req["go"]));
        DebugServer.MapGet("/find-fsm-action", req => BundleSpike.FindFsmAction(req["needle"] ?? "SetSprint"));
        DebugServer.MapPost("/fsm-trace", req => FsmTracer.SetTargets(req["names"])); // live state/event trace
        DebugServer.MapGet("/probe-cameratarget", _ => BundleSpike.ProbeCameraTarget());
        DebugServer.MapGet("/probe-sprint-target", _ => BundleSpike.ProbeSprintTarget());
        DebugServer.MapGet("/dump-localization", _ => ResourcesShim.DumpLocalization());
        DebugServer.MapGet("/load-res", req => ResourcesShim.LoadRes(req["path"] ?? ""));
        DebugServer.MapPost("/reload-resbundle", _ => {
            ResourcesShim.Reload();
            return new { ok = true };
        });
        DebugServer.MapPost("/addr-init", _ => AddressablesBootstrap.Ensure());
        DebugServer.MapGet("/addr-load", req => AddressablesBootstrap.Load(req["key"] ?? "GlobalPool"));
        DebugServer.MapGet("/addr-load-hero", _ => AddressablesBootstrap.LoadHero());
        DebugServer.MapPost("/gamecameras-init", _ => GameCamerasBootstrap.Ensure());
        DebugServer.MapPost("/hud",
            req => GameCamerasBootstrap.BringUpHud((req["on"] ?? "true").ToLowerInvariant() != "false"));
        DebugServer.MapGet("/probe-actions", _ => BundleSpike.ProbeActions());
        DebugServer.MapGet("/probe-hero-fsms", _ => BundleSpike.ProbeHeroFsms());
        DebugServer.MapPost("/load-save", req => {
            var slot = int.TryParse(req["slot"], out var s) ? s : 0;
            GameManager.instance.LoadGameFromUI(slot); // HK's GameManager: full UI load (transition + scene)
            return new { ok = true, slot };
        });
        DebugServer.MapPost("/kill", _ => HornetDeath.Kill()); // debug: trigger Hornet death (real damage path)
        DebugServer.MapPost("/getup", _ => HornetDeath.ForceGetUp()); // debug: unstick from bench/no_input
        DebugServer.MapPost("/hazard",
            req => HornetDeath.Hazard(req["type"] ?? "3")); // debug: trigger hazard N (2=spikes,3=acid,4=lava,5=pit)
        DebugServer.MapPost("/validate",
            (req, respond) =>
                validation!.RunRoute(req, respond)); // run a validation scenario (optionally disable=ModuleId,...)
        DebugServer.MapGet("/validate-list", _ => validation!.List()); // list scenarios + module Ids
        DebugServer.MapGet("/respawn-state", _ => { // compare HK vs Silksong PlayerData respawn (hard-save split)
            var hk = GameManager.instance.playerData;
            var ss = Silksong::PlayerData.instance;
            return new {
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                hk = new { hk.respawnScene, hk.respawnMarkerName, hk.respawnType, hk.atBench },
                ss = ss != null ? new { ss.respawnScene, ss.respawnMarkerName, ss.respawnType, ss.atBench } : null
            };
        });
        DebugServer.MapPost("/mirror-respawn", _ => { // copy Silksong PD respawn -> HK PD (un-poison a pre-bridge save)
            var ss = Silksong::PlayerData.instance;
            var knight = HeroController.UnsafeInstance;
            if (ss == null || knight == null) return new { error = "no Silksong PD / HK hero" };
            knight.SetBenchRespawn(ss.respawnMarkerName, ss.respawnScene, ss.respawnType, false);
            return new { mirrored = new { ss.respawnScene, ss.respawnMarkerName, ss.respawnType } };
        });
        DebugServer.MapGet("/bench-state", _ => BundleSpike.BenchState()); // debug: atBench signal + Hornet sit clips
        DebugServer.MapGet("/hc-probe", req => {
            // which HK HeroController methods get called on the Knight while Hornet active
            if ((req["reset"] ?? "").ToLowerInvariant() == "true") return HeroControllerProbe.Reset();
            var on = req["on"];
            if (on != null) HeroControllerProbe.Enabled = on.ToLowerInvariant() != "false";
            return HeroControllerProbe.Dump();
        });
        DebugServer.MapGet("/audio-diag",
            _ => BundleSpike.AudioDiag()); // debug: which SpawnAndPlayOneShot gate kills SFX
        DebugServer.MapPost("/switch", req => {
            var who = (req["who"] ?? "").ToLowerInvariant();
            return who switch {
                "knight" => HeroSwitch.SetActive(ActiveHero.Knight),
                "hornet" => HeroSwitch.SetActive(ActiveHero.Hornet),
                _ => HeroSwitch.Toggle()
            };
        });
        DebugServer.MapPost("/scene-entry", req => {
            HornetSceneEntry.Enabled = (req["on"] ?? "true").ToLowerInvariant() != "false";
            return new { realSceneEntry = HornetSceneEntry.Enabled };
        });
        DebugServer.MapPost("/press", req => {
            var a = (req["a"] ?? "right").ToLowerInvariant(); // left/right/up/down/jump/attack/dash
            if (!int.TryParse(req["frames"], out var f) || f <= 0) f = 60;
            InputBridge.Press(a, f); // debug-drive an InControl action for f frames (no physical key needed)
            return new { action = a, frames = f };
        });
        DebugServer.MapPost("/log-once", req => {
            Playground.Log.DedupOnce = (req["dedup"] ?? "true").ToLowerInvariant() != "false";
            if (Playground.Log.DedupOnce) Playground.Log.ClearOnce();
            return new { dedup = Playground.Log.DedupOnce };
        });
        DebugServer.Start(host, DebugServerPort);

        // FIRST: register Silksong's Addressables catalog into HK's empty runtime, BEFORE any Silksong code triggers a
        // (failing) addressables access. Once init fails in a process it stays poisoned (hasStartedInitialization=true,
        // empty locators) and can't be re-init'd, so this must run at Initialize on a fresh process — a hot-reload of
        // our DLL won't undo a poisoned Addressables runtime (Addressables lives in the engine DLL, one per process).
        AddressablesBootstrap.Ensure();
        ResourcesShim.Install(); // serve Silksong's Resources.Load from silksong-resources.bundle; log unserved misses
        GameObjectFindShim.Install(); // LOG-ONLY: surface name/tag GameObject lookups (cross-game collision hazard)
        PlayMakerFix.Apply();
        Stub.Install();
        CustomPlayerLoopBootstrap
            .Ensure(); // install Silksong's real LateFixedUpdate phase (drives DamageEnemies + cycle-gated FSMs)
        InputBridge.Install();
        HornetEnvironmentAdapter.Install();
        HeroSwitch.Install();
        EnemyDamageBridge
            .Install(); // forward Hornet's Silksong nail damage onto HK enemies/breakables (cross-game responder bridge)
        DamagesEnemyFsmShim
            .Install(); // stand in for the HK "damages_enemy" FSM that HK breakables read off Hornet's slash
        PogoNonBounceShim
            .Install(); // honour HK's NonBouncer so Hornet doesn't pogo off HK-non-pogoable objects (bell, …)
        ContactDamageBridge
            .Install(); // reverse: HK enemies/hazards deal contact damage to Hornet (HeroBox reads HK DamageHero/FSM)
        HornetDeath.Install(); // Hornet death -> HK bench respawn (Die's gm.PlayerDead handoff retargeted to HK's world)
        RespawnBridge.Install(); // mirror Silksong SetBenchRespawn/SetHazardRespawn onto HK PlayerData (hard-save points)
        CoroutineRedirect
            .Install(); // redirect coroutines from inactive Silksong GM to active host (hazard respawn etc.)
        HornetBench.Install(); // mirror HK bench rest onto Hornet (sit anim + heal her Silksong HP)
        HeroSfxShim.Install(); // Hornet one-shot SFX (dash/attack/slash) via PlayClipAtPoint (bypass SS audio gates)
        FreezeMomentFix.Install();
        FsmTracer.Install(); // live FSM state/event tracer (armed via POST /fsm-trace?names=...)
        GetComponentShim
            .Install(); // cross-game GetComponent(string) name-collision fallback (fixes CallMethodProper bind/heal)
        HeroControllerProbe
            .Install(); // DIAGNOSTIC: log which HK HeroController methods are called on the Knight while Hornet active
        EnemyTargetBridge
            .Install(); // redirect HK enemy "where's the hero" queries (LineOfSightDetector LoS) to the active hero
        Tk2dClipShim.Install(); // log-once + skip missing tk2d clips (HK-Knight clip names absent on Hornet's animator)
        InventoryPauseBridge
            .Install(); // inventory open/close -> freeze/resume HK's world (SetIsInventoryOpen -> timeScale)
        CallMethodProperFix
            .Install(); // catch AmbiguousMatchException in CallMethodProper.DoCache when HeroProxy repoints to Hornet
        PlayMakerWarningContext
            .Install(); // add GO+scene context to "Could not find FSM" / dedup "Fsm not initialized" burst
        // NOTE: HeroProxy has no Install — its global-"Hero" -> active-hero sync is driven per-frame from CameraSwitchDriver.Update.
        // BundleSpike.Run();

        // New lifecycle backbone: register migrated modules in order, then init them. Spawn is the first module — its
        // Initialize is a no-op (spawn is lazy, via /spawn-real or AutoSpawn); its Deinitialize despawns Hornet.
        moduleHost.Add(new HornetSpawner());
        moduleHost.InitializeAll();

        // Hornet's presence tracks HK's scene state, trigger-based (no polling) via two hooks on HK's GameManager:
        // FinishedEnteringScene spawns her once a gameplay scene has placed the Knight; ReturnToMainMenu despawns her on
        // quit-to-menu. The Knight is HK's to manage; Hornet is a separate DontDestroyOnLoad body HK's teardown doesn't
        // know about, so without the despawn she'd linger (visible/active) on the menu.
        InstallSpawnLifecycle();
        // Hot-reload mid-game: FinishedEnteringScene already fired this scene, so spawn now if we're already placed.
        var knight = HeroController.UnsafeInstance;
        if (knight != null && knight.isHeroInPosition && HornetSpawner.HornetRoot == null) HornetSpawner.Spawn();
    }

    private Hook? finishedEnteringHook;
    private Hook? returnToMenuHook;

    private void InstallSpawnLifecycle() {
        var entered = typeof(GameManager).GetMethod("FinishedEnteringScene", BindingFlags.Public | BindingFlags.Instance);
        if (entered != null)
            finishedEnteringHook = new Hook(entered,
                (Action<Action<GameManager>, GameManager>)((orig, self) => {
                    orig(self);
                    if (HornetSpawner.HornetRoot != null) return;
                    try {
                        HornetSpawner.Spawn();
                        Playground.Log.Info("[SpawnLifecycle] entered gameplay scene -> spawned Hornet");
                    } catch (Exception e) {
                        Playground.Log.Error($"[SpawnLifecycle] {e}");
                    }
                }));
        else
            Playground.Log.Error("[SpawnLifecycle] GameManager.FinishedEnteringScene not found");

        var quit = typeof(GameManager).GetMethod("ReturnToMainMenu", BindingFlags.Public | BindingFlags.Instance);
        if (quit != null)
            returnToMenuHook = new Hook(quit,
                (Func<Func<GameManager, GameManager.ReturnToMainMenuSaveModes, Action<bool>, IEnumerator>, GameManager,
                    GameManager.ReturnToMainMenuSaveModes, Action<bool>, IEnumerator>)((orig, self, mode, cb) => {
                    if (HornetSpawner.HornetRoot != null) {
                        // Hand the camera back to the Knight first: HeroSwitch points HK's CameraTarget at the active
                        // hero, so despawning while Hornet is active would leave CameraTarget.Update dereferencing a
                        // destroyed transform every frame through the menu fade.
                        HeroSwitch.SetActive(ActiveHero.Knight);
                        HornetSpawner.Despawn();
                        Playground.Log.Info("[SpawnLifecycle] quit to menu -> despawned Hornet");
                    }

                    return orig(self, mode, cb);
                }));
        else
            Playground.Log.Error("[SpawnLifecycle] GameManager.ReturnToMainMenu not found");
    }
}
