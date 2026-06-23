using System;
using System.Collections;
using System.Reflection;
using HornetPlayer.DevServer;
using HornetPlayer.Playground;
using Modding;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer;

public class HornetPlayerMod : Mod, ITogglableMod {
    // Distinct from Silksong's DevUtils server (8200) so both games can be debugged at once.
    private const int DebugServerPort = 8201;

    private GameObject? playgroundHost;

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

        ResourcesShim.Cleanup();
        GameObjectFindShim.Cleanup();
        AddressablesBootstrap.Cleanup();
        GameCamerasBootstrap.Cleanup();
        UIManagerBootstrap.Cleanup();
        BundleSpike.Cleanup();
        SilksongBootstrap.Cleanup();
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

        Log("Initializing");

        playgroundHost = new GameObject("HornetPlayer.Playground");
        Object.DontDestroyOnLoad(playgroundHost);
        var host = playgroundHost.AddComponent<PlaygroundHost>();

        PlaygroundRoutes.Register();
        DebugServer.MapPost("/spawn-real", _ => BundleSpike.SpawnReal());
        DebugServer.MapPost("/despawn-real", _ => BundleSpike.DespawnReal());
        DebugServer.MapPost("/scan-serializable", _ => BundleSpike.ScanSerializable());
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
            req => BundleSpike.DumpStateActions(req["name"] ?? "health_display", req["state"] ?? "First Pause"));
        DebugServer.MapGet("/fsm-vars", req => BundleSpike.FsmVars(req["name"] ?? "Bind"));
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
        // NOTE: HeroProxy has no Install — its global-"Hero" -> active-hero sync is driven per-frame from CameraSwitchDriver.Update.
        // BundleSpike.Run();

        // Auto-spawn Hornet once we're in a gameplay scene and she's absent. A hot-reload despawns her in Unload, so
        // this brings her back without a manual /spawn-real (and on a fresh boot, fires the first time you load in).
        // One-shot: it stops after the first auto-spawn, so a later manual /despawn-real stays despawned.
        host.StartCoroutine(AutoSpawnWhenInGame());
    }

    private static IEnumerator AutoSpawnWhenInGame() {
        while (true) {
            // HK's hero: null at the menu, set + isHeroInPosition once a gameplay scene has placed it. Gate on
            // isHeroInPosition so we don't spawn mid-transition (before the scene/entry is ready).
            var knight =
                HeroController.UnsafeInstance; // UnsafeInstance: no "Couldn't find a Hero" log spam at the menu
            if (knight != null && knight.isHeroInPosition) {
                if (BundleSpike.HornetRoot == null)
                    try {
                        BundleSpike.SpawnReal();
                        Playground.Log.Info("[AutoSpawn] in gameplay scene + Hornet absent -> spawned");
                    } catch (Exception e) {
                        Playground.Log.Error($"[AutoSpawn] {e}");
                    }

                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
