using System.Collections;
using System.Reflection;
using HornetPlayer.Playground;
using Modding;
using UnityEngine;

using HornetPlayer.DevServer;
namespace HornetPlayer;

public class HornetPlayerMod : Mod, ITogglableMod {
    // Distinct from Silksong's DevUtils server (8200) so both games can be debugged at once.
    private const int DebugServerPort = 8201;

    public static HornetPlayerMod? LoadedInstance { get; private set; }

    private GameObject? playgroundHost;

    public override string GetVersion() {
        return Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    public override void Initialize() {
        if (LoadedInstance != null) return;
        LoadedInstance = this;

        Playground.Log.Sink = msg => Log(msg);
        
        Log("Initializing");

        playgroundHost = new GameObject("HornetPlayer.Playground");
        Object.DontDestroyOnLoad(playgroundHost);
        var host = playgroundHost.AddComponent<PlaygroundHost>();

        PlaygroundRoutes.Register();
        /*DebugServer.MapPost("/respawn-hornet", _ => {
            BundleSpike.Cleanup();
            BundleSpike.Run();
            return new { ok = true };
        });*/
        DebugServer.MapPost("/spawn-real", _ => BundleSpike.SpawnReal());
        DebugServer.MapPost("/despawn-real", _ => BundleSpike.DespawnReal());
        DebugServer.MapPost("/diagnose-awake", _ => BundleSpike.DiagnoseAwake());
        DebugServer.MapPost("/reload-all-deps", req => BundleSpike.ReloadWithAllDeps(
            req["list"] ?? "/home/jakob/dev/hk/mods/HornetPlayer/Source/lib/hornet-deps.txt"));
        DebugServer.MapPost("/scan-serializable", _ => BundleSpike.ScanSerializable());
        DebugServer.MapGet("/hero-state", _ => BundleSpike.HeroState());
        DebugServer.MapGet("/diag-input", _ => BundleSpike.DiagInput());
        DebugServer.MapGet("/fsm-state", _ => BundleSpike.FsmState());
        DebugServer.MapPost("/load-gamecameras", (req, respond) => BundleSpike.LoadGameCamerasCo(respond, req["bundle"]));
        DebugServer.MapGet("/fsm-dump", req => BundleSpike.FsmDump(req["name"] ?? "Sprint"));
        DebugServer.MapGet("/find-fsm-action", req => BundleSpike.FindFsmAction(req["needle"] ?? "SetSprint"));
        DebugServer.MapGet("/probe-cameratarget", _ => BundleSpike.ProbeCameraTarget());
        DebugServer.MapGet("/probe-sprint-target", _ => BundleSpike.ProbeSprintTarget());
        DebugServer.MapGet("/find-trace", req => { GameObjectFindShim.TraceKey = req["key"]; return new { traceKey = GameObjectFindShim.TraceKey }; });
        DebugServer.MapGet("/gc-dump", _ => BundleSpike.GcDump());
        DebugServer.MapGet("/probe-types", req => BundleSpike.ProbeTypes(req["name"] ?? "GameCameras"));
        DebugServer.MapGet("/test-addcomponent", _ => BundleSpike.TestAddComponent());
        DebugServer.MapPost("/load-gamecameras-asset", req => BundleSpike.LoadGameCamerasAsset(req["instantiate"] == "true"));
        DebugServer.MapPost("/test-minimal-binding", _ => BundleSpike.LoadMinimalBindingTest());
        DebugServer.MapPost("/activate-gamecameras", _ => BundleSpike.ActivateGameCameras());
        DebugServer.MapPost("/restore-camera", _ => BundleSpike.RestoreCamera());
        DebugServer.MapGet("/dump-localization", _ => ResourcesShim.DumpLocalization());
        DebugServer.MapGet("/load-res", req => ResourcesShim.LoadRes(req["path"] ?? ""));
        DebugServer.MapPost("/reload-resbundle", _ => { ResourcesShim.Reload(); return new { ok = true }; });
        DebugServer.MapPost("/addr-init", _ => AddressablesBootstrap.Ensure());
        DebugServer.MapGet("/addr-load", req => AddressablesBootstrap.Load(req["key"] ?? "GlobalPool"));
        DebugServer.MapGet("/addr-load-hero", _ => AddressablesBootstrap.LoadHero());
        DebugServer.MapPost("/gamecameras-init", _ => GameCamerasBootstrap.Ensure());
        DebugServer.MapGet("/probe-actions", _ => BundleSpike.ProbeActions());
        DebugServer.MapGet("/probe-hero-fsms", _ => BundleSpike.ProbeHeroFsms());
        DebugServer.MapPost("/load-save", req => {
            var slot = int.TryParse(req["slot"], out var s) ? s : 0;
            GameManager.instance.LoadGameFromUI(slot); // HK's GameManager: full UI load (transition + scene)
            return new { ok = true, slot };
        });
        DebugServer.MapPost("/switch", req => {
            var who = (req["who"] ?? "").ToLowerInvariant();
            return who switch {
                "knight" => HeroSwitch.SetActive(ActiveHero.Knight),
                "hornet" => HeroSwitch.SetActive(ActiveHero.Hornet),
                _ => HeroSwitch.Toggle(),
            };
        });
        DebugServer.MapPost("/press", req => {
            var a = (req["a"] ?? "right").ToLowerInvariant(); // left/right/up/down/jump/attack/dash
            if (!int.TryParse(req["frames"], out var f) || f <= 0) f = 60;
            InputBridge.Press(a, f); // debug-drive an InControl action for f frames (no physical key needed)
            return new { action = a, frames = f };
        });
        DebugServer.Start(host, DebugServerPort);

        // SilksongLoadSpike.Run();   // touches Silksong types -> assembly is now in the AppDomain
        // FIRST: register Silksong's Addressables catalog into HK's empty runtime, BEFORE any Silksong code triggers a
        // (failing) addressables access. Once init fails in a process it stays poisoned (hasStartedInitialization=true,
        // empty locators) and can't be re-init'd, so this must run at Initialize on a fresh process — a hot-reload of
        // our DLL won't undo a poisoned Addressables runtime (Addressables lives in the engine DLL, one per process).
        AddressablesBootstrap.Ensure();
        ResourcesShim.Install();       // serve Silksong's Resources.Load from silksong-resources.bundle; log unserved misses
        GameObjectFindShim.Install();  // LOG-ONLY: surface name/tag GameObject lookups (cross-game collision hazard)
        PlayMakerFix.Apply();
        Stub.Install();
        InputBridge.Install();
        HornetEnvironmentAdapter.Install();
        HeroSwitch.Install();
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
            var knight = HeroController.instance;
            if (knight != null && knight.isHeroInPosition) {
                if (BundleSpike.HornetRoot == null) {
                    try {
                        BundleSpike.SpawnReal();
                        Playground.Log.Info("[AutoSpawn] in gameplay scene + Hornet absent -> spawned");
                    } catch (System.Exception e) { Playground.Log.Error($"[AutoSpawn] {e}"); }
                }
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    public void Unload() {
        SilksongLoadSpike.Cleanup();
        ResourcesShim.Cleanup();
        GameObjectFindShim.Cleanup();
        AddressablesBootstrap.Cleanup();
        GameCamerasBootstrap.Cleanup();
        BundleSpike.Cleanup();
        SilksongBootstrap.Cleanup();
        GlobalSettingsBootstrap.Cleanup();
        PlayMakerFix.Cleanup();
        Stub.Cleanup();
        InputBridge.Cleanup();
        HornetEnvironmentAdapter.Cleanup();
        HeroSwitch.Cleanup();
        DebugServer.Stop();
        if (playgroundHost != null) Object.Destroy(playgroundHost);
        playgroundHost = null;
        LoadedInstance = null;
    }
}
