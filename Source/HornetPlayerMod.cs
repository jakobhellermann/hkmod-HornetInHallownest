extern alias Silksong;
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using HornetPlayer.DevServer;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Modules;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.HornetInHallownest.Validation;
using HornetPlayer.HornetInHallownest.Validation.Scenarios;
using HornetPlayer.Playground;
using Modding;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer;

public class HornetPlayerMod : Mod, ITogglableMod, ILocalSettings<HornetSaveData> {
    // Distinct from Silksong's DevUtils server (8200) so both games can be debugged at once.
    private const int DebugServerPort = 8201;

    // The new lifecycle backbone (HornetInHallownest). Modules migrate into this ordered list one at a time; until a
    // system is migrated it keeps its old Install/Cleanup below. Initialize forward, Deinitialize reverse.
    private readonly ModuleHost moduleHost = new();

    internal ModuleHost Modules => moduleHost;

    private Hook? finishedEnteringHook;

    private GameObject? playgroundHost;
    private Hook? returnToMenuHook;
    private ValidationRunner? validation;

    public static HornetPlayerMod? LoadedInstance { get; private set; }

    // Persist Hornet's PlayerData inside HK's save file (per slot). The modding API invokes these at HK's native
    // save/load points (GameManager.SaveGame on bench/autosave; LoadGame on load) — see HornetSaveBridge.
    public HornetSaveData OnSaveLocal() {
        return HornetSaveBridge.Snapshot();
    }

    public void OnLoadLocal(HornetSaveData s) {
        HornetSaveBridge.Stash(s);
    }

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
                Playground.Log.Debug(
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
                Playground.Log.Debug("[Unload] cleared stuck isInventoryOpen");
            }

            if (Time.timeScale <= 0.0001f) Time.timeScale = 1f;
        } catch (Exception e) {
            Playground.Log.Error($"[Unload] inventory reset: {e.Message}");
        }

        finishedEnteringHook?.Dispose();
        finishedEnteringHook = null;
        returnToMenuHook?.Dispose();
        returnToMenuHook = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= DespawnOutsideGameplay;

        // Before anything despawns Hornet: if the player was on her, TP the Knight onto her spot. Cleanup below hands
        // control back to the Knight (un-mod safety) and a hot-reload re-activates it — otherwise it reactivates at its
        // stale pre-switch position and control snaps there (flew OOB on build). Must run here, BEFORE the module host
        // despawns Hornet (HornetRoot goes null), so it can't live in HeroSwitch.Cleanup.
        try {
            HeroSwitch.TpKnightToActiveHornet();
        } catch (Exception e) {
            Playground.Log.Error($"[Unload] Knight TP: {e.Message}");
        }

        // New lifecycle backbone: tear migrated modules down in reverse registration order, before the legacy systems.
        moduleHost.DeinitializeAll();

        ResourcesShim.Cleanup();
        SilksongCatalog.Cleanup();
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
        FsmLookupShim.Cleanup();
        PogoNonBounceShim.Cleanup();
        AcidSwimBridge.Cleanup();
        HornetDeath.Cleanup();
        RespawnBridge.Cleanup();
        CoroutineRedirect.Cleanup();
        SoulOrbBridge.Cleanup();
        GeoDashBridge.Cleanup();
        RoarLockBridge.Cleanup();
        SpiderTrapBenchFix.Cleanup();
        PlayerDataSync.Cleanup();
        HeroEventBridge.Cleanup();
        HeroSfxShim.Cleanup();
        FreezeMomentFix.Cleanup();
        FsmTracer.Cleanup();
        HkFsmTracer.Cleanup();
        DreamReturnBridge.Cleanup();
        HeroControllerProbe.Cleanup();
        ShroomBounceBridge.Cleanup();
        EnemyTargetBridge.Cleanup();
        HeroProxy.Cleanup();
        Tk2dClipShim.Cleanup();
        InventoryPauseBridge.Cleanup();
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

        // Where our DLL + shipped data files live. From the Modding API's Mod.ModDirectory (correct even on hot-reload,
        // where Assembly.Location is empty). Must precede anything reading Paths.ModFile (e.g. ResourcesShim.Install).
        Paths.ModDir = ModDirectory;

        // Must run before any MonoMod Hook is created (it locks MonoMod's platform detection).
        RosettaPlatformFix.Apply();

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
        DebugServer.MapPost("/unlock-crest-slots", _ => ToolItemManagerBootstrap.UnlockAllCrestSlots());
        DebugServer.MapPost("/dbg-recoil", req => {
            // validate recoil/bounce mechanics directly (kind=bounce|dash)
            var hero = HornetSpawner.RealHero;
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
            var hero = HornetSpawner.RealHero;
            hero?.ResetAllCrestState();
            return new {
                equipped = Silksong::PlayerData.instance != null ? Silksong::PlayerData.instance.CurrentCrestID : null,
                crestConfigSet = hero != null && hero.GetFieldValue<object>("crestConfig") != null,
                recoilHorVelocity = hero != null ? hero.RECOIL_HOR_VELOCITY : -1f
            };
        });
        DebugServer.MapPost("/set-pd", req => {
            // debug: set a Silksong PlayerData field (bool/int/float/string) by name
            var pd = Silksong::PlayerData.instance;
            if (pd == null) return new { error = "no PlayerData" };
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
        DebugServer.MapGet("/find-event-senders", req => BundleSpike.FindEventSenders(req["event"] ?? "SHOW HP"));
        DebugServer.MapGet("/fsm-state-actions",
            req => BundleSpike.DumpStateActions(req["name"] ?? "health_display", req["state"] ?? "First Pause",
                req["go"]));
        DebugServer.MapGet("/fsm-vars", req => BundleSpike.FsmVars(req["name"] ?? "Bind", req["go"]));
        DebugServer.MapGet("/fsm-vars-hk", req => BundleSpike.FsmVarsHk(req["name"] ?? "Fade", req["go"]));
        DebugServer.MapPost("/grant-kit", _ => PlayerDataSync.GrantFullKit()); // debug: full ability kit (bypass HK sync)
        DebugServer.MapGet("/find-fsm-action", req => BundleSpike.FindFsmAction(req["needle"] ?? "SetSprint"));
        DebugServer.MapPost("/fsm-trace", req => FsmTracer.SetTargets(req["names"])); // live state/event trace
        DebugServer.MapPost("/hk-fsm-trace", req => HkFsmTracer.SetTargets(req["names"])); // HK-side FSM trace
        DebugServer.MapGet("/probe-cameratarget", _ => BundleSpike.ProbeCameraTarget());
        DebugServer.MapGet("/probe-sprint-target", _ => BundleSpike.ProbeSprintTarget());
        DebugServer.MapGet("/dump-localization", _ => ResourcesShim.DumpLocalization());
        DebugServer.MapGet("/load-res", req => ResourcesShim.LoadRes(req["path"] ?? ""));
        DebugServer.MapPost("/reload-resbundle", _ => {
            ResourcesShim.Reload();
            return new { ok = true };
        });
        DebugServer.MapGet("/addr-load", req => SilksongCatalog.Load(req["key"] ?? "GlobalPool"));
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
        DebugServer.MapPost("/kill", _ => HornetDeath.Kill()); // debug: trigger Hornet death (real damage path)
        DebugServer.MapPost("/getup", _ => HornetDeath.ForceGetUp()); // debug: unstick from bench/no_input
        DebugServer.MapPost("/hazard",
            req => HornetDeath.Hazard(req["type"] ?? "3")); // debug: trigger hazard N (2=spikes,3=acid,4=lava,5=pit)
        DebugServer.MapPost("/acid-offset", req => {
            if (float.TryParse(req["value"], out var v)) AcidSwimBridge.SurfaceOffset = v;
            return new { AcidSwimBridge.SurfaceOffset }; // tune where Hornet's origin sits vs the acid surface line
        });
        DebugServer.MapPost("/validate",
            (req, respond) =>
                validation!.RunRoute(req, respond)); // run a validation scenario (optionally disable=ModuleId,...)
        DebugServer.MapGet("/validate-list", _ => validation!.List()); // list scenarios + module Ids
        DebugServer.MapGet("/respawn-state", _ => {
            // compare HK vs Silksong PlayerData respawn (hard-save split)
            var hk = GameManager.instance.playerData;
            var ss = Silksong::PlayerData.instance;
            return new {
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                hk = new { hk.respawnScene, hk.respawnMarkerName, hk.respawnType, hk.atBench },
                ss = ss != null ? new { ss.respawnScene, ss.respawnMarkerName, ss.respawnType, ss.atBench } : null
            };
        });
        DebugServer.MapPost("/mirror-respawn", _ => {
            // copy Silksong PD respawn -> HK PD (un-poison a pre-bridge save)
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
            if (a == null || !InputBridge.IsKnownAction(a))
                return new { error = $"unknown action '{a}'; known: {string.Join(",", InputBridge.KnownActions)}" };
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
        SilksongCatalog.EnsureMounted();
        ResourcesShim.Install(); // serve Silksong's Resources.Load from silksong-resources.bundle; log unserved misses
        PlayMakerFix.Apply();
        Stub.Install();
        InputBridge.Install();
        HornetEnvironmentAdapter.Install();
        HeroSwitch.Install();
        EnemyDamageBridge
            .Install(); // forward Hornet's Silksong nail damage onto HK enemies/breakables (cross-game responder bridge)
        FsmLookupShim
            .Install(); // one place for HK "find FSM on a cross-game object": hero-owned inert (Dream Return/ProxyFSM)
        // + the slash's "damages_enemy" dummy HK breakables/bells read (folds in the old Hero/DamagesEnemy shims)
        PogoNonBounceShim
            .Install(); // honour HK's NonBouncer so Hornet doesn't pogo off HK-non-pogoable objects (bell, …)
        HornetDeath.Install(); // Hornet death -> HK bench respawn (Die's gm.PlayerDead handoff retargeted to HK's world)
        RespawnBridge
            .Install(); // mirror Silksong SetBenchRespawn/SetHazardRespawn onto HK PlayerData (hard-save points)
        CoroutineRedirect
            .Install(); // redirect coroutines from inactive Silksong GM to active host (hazard respawn etc.)
        SoulOrbBridge.Install(); // HK soul (SoulOrb homing + AddMPCharge) -> Hornet silk (orbs fly to her, grant silk)
        HeroSfxShim.Install(); // Hornet one-shot SFX (dash/attack/slash) via PlayClipAtPoint (bypass SS audio gates)
        FreezeMomentFix.Install();
        FsmTracer.Install(); // live FSM state/event tracer (armed via POST /fsm-trace?names=...)
        HkFsmTracer.Install(); // HK-side FSM tracer (armed via POST /hk-fsm-trace?names=...)
        HeroEventBridge.Install(); // forward HK FSM events aimed at the "Hero" GO onto Hornet's isolated Silksong FSMs
        RoarLockBridge.Install(); // roar-specific facing on top of HeroEventBridge (subscribes to its callback)
        ShroomBounceBridge
            .Install(); // HK BounceShroom/BigBouncer -> Hornet ShroomBounce/BounceHigh on down-attack pogo
        HeroControllerProbe
            .Install(); // DIAGNOSTIC: log which HK HeroController methods are called on the Knight while Hornet active
        EnemyTargetBridge
            .Install(); // redirect HK enemy "where's the hero" queries (LineOfSightDetector LoS) to the active hero
        Tk2dClipShim.Install(); // log-once + skip missing tk2d clips (HK-Knight clip names absent on Hornet's animator)
        InventoryPauseBridge
            .Install(); // inventory open/close -> freeze/resume HK's world (SetIsInventoryOpen -> timeScale)
        PlayMakerWarningContext
            .Install(); // add GO+scene context to "Could not find FSM" / dedup "Fsm not initialized" burst
        SpiderTrapBenchFix
            .Install(); // Deepnest trap bench: patch the Fade FSM's Knight-calibrated 'Hero Land Y' into Hornet's frame
        PlayerDataSync
            .Install(); // mirror the Knight's HK progression onto Hornet's Silksong PlayerData (hooks; seed runs at spawn)
        // NOTE: HeroProxy has no Install — its global-"Hero" -> active-hero sync is driven per-frame from CameraSwitchDriver.Update.
        // BundleSpike.Run();

        // New lifecycle backbone: register migrated modules in order, then init them. Spawn is the first module — its
        // Initialize is a no-op (spawn is lazy, via /spawn-real or AutoSpawn); its Deinitialize despawns Hornet.
        moduleHost.Add(new PlayerLoopModule());
        moduleHost.Add(new BreakableFloorModule());
        moduleHost.Add(new GameObjectLookupModule());
        moduleHost.Add(new ConveyorModule());
        moduleHost.Add(new FsmMethodCallRemapModule());
        moduleHost.Add(new ContactDamageModule());
        moduleHost.Add(new NeedolinDreamNailModule());
        moduleHost.Add(new BenchModule());
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

    private void InstallSpawnLifecycle() {
        var entered =
            typeof(GameManager).GetMethod("FinishedEnteringScene", BindingFlags.Public | BindingFlags.Instance);
        if (entered != null)
            finishedEnteringHook = new Hook(entered,
                (Action<Action<GameManager>, GameManager>)((orig, self) => {
                    orig(self);
                    if (HornetSpawner.HornetRoot == null)
                        try {
                            HornetSpawner.Spawn();
                            Playground.Log.Debug("[SpawnLifecycle] entered gameplay scene -> spawned Hornet");
                            // Dev convenience (like the ability-kit grant): unlock every crest's tool slots so all
                            // tools are equippable without hunting memory lockets. Runs per spawn (idempotent).
                            ToolItemManagerBootstrap.UnlockAllCrestSlots();
                        } catch (Exception e) {
                            Playground.Log.Error($"[SpawnLifecycle] {e}");
                        }

                    // After spawn (PlayerData.instance exists), apply a save loaded earlier this LoadGame. No-op unless a
                    // load stashed data; clears itself so it applies exactly once.
                    HornetSaveBridge.ApplyPending();
                }));
        else
            Playground.Log.Error("[SpawnLifecycle] GameManager.FinishedEnteringScene not found");

        var quit = typeof(GameManager).GetMethod("ReturnToMainMenu", BindingFlags.Public | BindingFlags.Instance);
        if (quit != null)
            returnToMenuHook = new Hook(quit,
                (Func<Func<GameManager, GameManager.ReturnToMainMenuSaveModes, Action<bool>, IEnumerator>, GameManager,
                    GameManager.ReturnToMainMenuSaveModes, Action<bool>, IEnumerator>)((orig, self, mode, cb) => {
                    if (HornetSpawner.HornetRoot != null) {
                        // Record the hero the player was actually on: ReturnToMainMenu autosaves, but we must force the
                        // Knight active below (camera handback), which would otherwise make that save record Knight and
                        // clobber the "was playing Hornet" state. The override is consumed by the next Snapshot.
                        HornetSaveBridge.SaveActiveOverride = HeroSwitch.HornetActive;
                        // Hand the camera back to the Knight first: HeroSwitch points HK's CameraTarget at the active
                        // hero, so despawning while Hornet is active would leave CameraTarget.Update dereferencing a
                        // destroyed transform every frame through the menu fade.
                        HeroSwitch.SetActive(ActiveHero.Knight);
                        HornetSpawner.Despawn();
                        Playground.Log.Debug("[SpawnLifecycle] quit to menu -> despawned Hornet");
                    }

                    return orig(self, mode, cb);
                }));
        else
            Playground.Log.Error("[SpawnLifecycle] GameManager.ReturnToMainMenu not found");

        // Like HK's own hero, Hornet has no place in non-gameplay scenes (End_Credits, cinematics). Despawn her there so
        // HK owns the camera/scene — else the camera keeps following her and the credits render off-screen (blackscreen).
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += DespawnOutsideGameplay;
    }

    private void DespawnOutsideGameplay(UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode) {
        var gm = GameManager.UnsafeInstance;
        if (HornetSpawner.HornetRoot == null || gm == null || gm.IsGameplayScene()) return;
        // EXCEPTION: stag travel (Cinematic_Stag_travel) is a HK "Cinematic" (non-gameplay) scene, but the hero RIDES
        // THROUGH it — HK's Knight is DontDestroyOnLoad and is never deactivated there; StagTravel.Start just plays the
        // full-screen cinematic then BeginSceneTransition's onward to the destination (gate "door_stagExit"). Despawning
        // Hornet here forced a full respawn on arrival (fresh HeroController/FSMs -> sprint & other carry-through state
        // reset). Mirror HK: let her ride through (she's already deparented to DDOL) and enter the destination normally.
        if (gm.IsStagTravelScene()) return;
        HeroSwitch.SetActive(ActiveHero.Knight); // hand camera back before despawn (CameraTarget would deref her)
        HornetSpawner.Despawn();
        Playground.Log.Debug($"[SpawnLifecycle] non-gameplay scene '{scene.name}' -> despawned Hornet");
    }
}
