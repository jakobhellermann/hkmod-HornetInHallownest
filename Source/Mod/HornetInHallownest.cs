extern alias Silksong;
using System;
using System.Reflection;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using HornetInHallownest.Modules;
using HornetInHallownest.Playground;
using HornetInHallownest.Save;
using System.Globalization;
using HornetInHallownest.DevServer;
using HornetInHallownest.Util;
using HornetInHallownest.Validation;
using HornetInHallownest.Validation.Scenarios;
using Modding;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest;

public class HornetInHallownest() : Mod("HornetInHallownest"), ITogglableMod, ILocalSettings<HornetSaveData>,
    IGlobalSettings<HornetGlobalSettings> {
    private const int DebugServerPort = 8201;

    private readonly ModuleHost moduleHost = new();

    internal ModuleHost Modules => moduleHost;

    private GameObject? playgroundHost;
    private ValidationRunner? validation;
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
        DebugKeybinds.Cleanup(); // InputModule tears down via moduleHost.DeinitializeAll above
        InputDebug.Cleanup();
        FsmTracer.Cleanup();
        HkFsmTracer.Cleanup();
        HeroControllerProbe.Cleanup();
        PlayMakerWarningContext.Cleanup();
        DebugServer.Stop();
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

        validation = new ValidationRunner()
            .Register(new SpawnSanityScenario());

        PlaygroundRoutes.Register();
        DebugServer.MapPost("/spawn-real", _ => HornetSpawner.Spawn() ? new { ok = true } : new { ok = false });
        DebugServer.MapPost("/despawn-real", _ => HornetSpawner.Despawn()
            ? new { ok = true, despawned = true }
            : new { ok = true, note = "nothing to despawn" });
        DebugServer.MapGet("/scan-missing", _ => BundleSpike.ScanMissing());
        DebugServer.MapGet("/hero-state", _ => BundleSpike.HeroState());
        DebugServer.MapGet("/toolmgr", _ => ToolItemManagerBootstrap.Diag());
        DebugServer.MapPost("/unlock-crest-slots", _ => ToolItemManagerBootstrap.UnlockAllCrestSlots());
        DebugServer.MapPost("/dbg-recoil", req => {
            // validate recoil/bounce mechanics directly (kind=bounce|dash)
            var hero = HornetSpawner.Hornet;
            if (hero == null) return new { error = "no hero" };
            var kind = req["kind"] ?? "bounce";
            if (kind == "dash") hero.sprintFSM?.SendEvent("DASH RECOIL");
            else hero.DownspikeBounce(true);
            return new { fired = kind };
        });
        DebugServer.MapGet("/equip-crest", req => {
            // equip a crest by id + apply its HeroConfig (ResetAllCrestState)
            var id = req["id"] ?? "";
            Silksong::ToolItemManager.SetEquippedCrest(id);
            Silksong::ToolItemManager.SendEquippedChangedEvent(true);
            var hero = HornetSpawner.Hornet;
            hero?.ResetAllCrestState();
            return new {
                equipped = Silksong::PlayerData.instance.CurrentCrestID,
                crestConfigSet = hero != null && hero.GetFieldValue<object>("crestConfig") != null,
                recoilHorVelocity = hero != null ? hero.RECOIL_HOR_VELOCITY : -1f
            };
        });
        DebugServer.MapPost("/set-pd", req => {
            // debug: set a Silksong PlayerData field (bool/int/float/string) by name
            var pd = Silksong::PlayerData.instance;
            var name = req["field"] ?? "";
            var fi = typeof(Silksong::PlayerData).GetField(name);
            if (fi == null) return new { error = $"field '{name}' not found" };
            var val = req["value"] ?? "";
            var t = fi.FieldType;
            object parsed;
            if (t == typeof(bool)) parsed = val.ToLowerInvariant() == "true";
            else if (t == typeof(int)) parsed = int.Parse(val, CultureInfo.InvariantCulture);
            else if (t == typeof(float)) parsed = float.Parse(val, CultureInfo.InvariantCulture);
            else if (t == typeof(string)) parsed = val;
            else return new { error = $"unsupported type {t.Name}" };
            var old = fi.GetValue(pd);
            fi.SetValue(pd, parsed);
            return new { field = name, old = old?.ToString(), set = parsed.ToString() };
        });
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
        DebugServer.MapGet("/perf", PerfProbe.Measure); // A/B fps: /perf?disable=fsm,animator,tk2d,render,hero,go,...
        DebugServer.MapPost("/eval-cs", req => EvalCs.Run(req.Body)); // runtime C# via UnityExplorer's evaluator
        DebugServer.MapGet("/find-event-senders", req => BundleSpike.FindEventSenders(req["event"] ?? "SHOW HP"));
        DebugServer.MapGet("/fsm-state-actions",
            req => BundleSpike.DumpStateActions(req["name"] ?? "health_display", req["state"] ?? "First Pause",
                req["go"]));
        DebugServer.MapGet("/fsm-vars", req => BundleSpike.FsmVars(req["name"] ?? "Bind", req["go"]));
        DebugServer.MapGet("/fsm-vars-hk", req => BundleSpike.FsmVarsHk(req["name"] ?? "Fade", req["go"]));
        DebugServer.MapPost("/grant-kit", _ => PlayerDataSyncModule.GrantFullKit()); 
        DebugServer.MapGet("/find-fsm-action", req => BundleSpike.FindFsmAction(req["needle"] ?? "SetSprint"));
        DebugServer.MapPost("/fsm-trace", req => FsmTracer.SetTargets(req["names"])); // live state/event trace
        DebugServer.MapPost("/hk-fsm-trace", req => HkFsmTracer.SetTargets(req["names"])); // HK-side FSM trace
        DebugServer.MapGet("/probe-cameratarget", _ => BundleSpike.ProbeCameraTarget());
        DebugServer.MapGet("/probe-sprint-target", _ => BundleSpike.ProbeSprintTarget());
        DebugServer.MapPost("/gamecameras-init", _ => GameCamerasBootstrap.Ensure());
        DebugServer.MapPost("/hud",
            req => GameCamerasBootstrap.BringUpHud((req["on"] ?? "true").ToLowerInvariant() != "false"));
        DebugServer.MapGet("/probe-actions", _ => BundleSpike.ProbeActions());
        DebugServer.MapGet("/probe-hero-fsms", _ => BundleSpike.ProbeHeroFsms());
        DebugServer.MapGet("/hero-clips", req => BundleSpike.ListHeroClips(req["filter"])); // list Hornet's tk2d clips
        DebugServer.MapPost("/play-clip",
            req => BundleSpike.PlayHeroClip(req["name"])); // play a Hornet clip (anim-control off)
        DebugServer.MapPost("/hero-anim-resume", _ => BundleSpike.ResumeHeroAnim()); // restore normal animation control
        DebugServer.MapPost("/load-save", req => {
            var slot = int.TryParse(req["slot"], out var s) ? s : 0;
            GameManager.instance.LoadGameFromUI(slot); // HK's GameManager: full UI load (transition + scene)
            return new { ok = true, slot };
        });
        DebugServer.MapPost("/kill", _ => DeathModule.Kill()); // debug: trigger Hornet death (real damage path)
        DebugServer.MapPost("/getup", _ => DeathModule.ForceGetUp()); // debug: unstick from bench/no_input
        DebugServer.MapPost("/hazard",
            req => DeathModule.Hazard(req["type"] ?? "3")); // debug: trigger hazard N (2=spikes,3=acid,4=lava,5=pit)
        DebugServer.MapPost("/validate",
            (req, respond) =>
                validation!.RunRoute(req, respond)); // run a validation scenario (optionally disable=ModuleId,...)
        DebugServer.MapGet("/validate-list", _ => validation!.List()); // list scenarios + module Ids
        DebugServer.MapGet("/respawn-state", _ => {
            // compare HK vs Silksong PlayerData respawn (hard-save split)
            var hk = GameManager.instance.playerData;
            var ss = Silksong::PlayerData.HasInstance ? Silksong::PlayerData.instance : null; // don't auto-create just to probe
            return new {
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                hk = new { hk.respawnScene, hk.respawnMarkerName, hk.respawnType, hk.atBench },
                ss = ss != null ? new { ss.respawnScene, ss.respawnMarkerName, ss.respawnType, ss.atBench } : null
            };
        });
        DebugServer.MapPost("/mirror-respawn", _ => {
            // copy Silksong PD respawn -> HK PD (un-poison a pre-bridge save)
            var ss = Silksong::PlayerData.HasInstance ? Silksong::PlayerData.instance : null; // don't auto-create just to probe
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
        DebugServer.MapPost("/warp", req => {
            var scene = req["scene"];
            if (string.IsNullOrEmpty(scene))
                return new { error = "scene required (e.g. /warp?scene=RestingGrounds_04&x=46.25&y=7.57)" };
            return DebugWarp.Warp(scene!, DebugWarp.ParseFloat(req["x"]), DebugWarp.ParseFloat(req["y"]));
        });
        DebugServer.MapPost("/press", req => {
            // Accept `a` or `action`; reject unknown names instead of silently pressing `right` (a debug footgun: a
            // typo used to drive movement and stick move_input). Bounded by `frames` (default 60 ≈ 1s) — never forever.
            var a = (req["a"] ?? req["action"])?.ToLowerInvariant();
            if (a == null || !InputDebug.IsKnownAction(a))
                return new { error = $"unknown action '{a}'; known: {string.Join(",", InputDebug.KnownActions)}" };
            if (!int.TryParse(req["frames"], out var f) || f <= 0) f = 60;
            InputDebug.Press(a, f); // debug-drive an InControl action for f frames (no physical key needed)
            return new { action = a, frames = f };
        });
        DebugServer.MapPost("/log-once", req => {
            Util.Log.DedupOnce = (req["dedup"] ?? "true").ToLowerInvariant() != "false";
            if (Util.Log.DedupOnce) Util.Log.ClearOnce();
            return new { dedup = Util.Log.DedupOnce };
        });
        DebugServer.Start(host, DebugServerPort);

        // must run before any silksong code attempts to access addressables, since one failure permanently poisons the addressables somehow
        SilksongAddressables.EnsureMounted();
        SilksongResources.Install();
        SilksongPlayMaker.Apply();
        Stub.Install();
        HeroSwitch.Install();
        DebugKeybinds.Install(); // dev-only hotkeys (T/B/Digit8); input itself is InputModule in the module host
        InputDebug.Install(); // dev-only /press action driver
        FsmTracer.Install(); // live FSM state/event tracer (armed via POST /fsm-trace?names=...)
        HkFsmTracer.Install(); // HK-side FSM tracer (armed via POST /hk-fsm-trace?names=...)
        HeroControllerProbe
            .Install(); // DIAGNOSTIC: log which HK HeroController methods are called on the Knight while Hornet active
        PlayMakerWarningContext
            .Install(); // add GO+scene context to "Could not find FSM" / dedup "Fsm not initialized" burst

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
