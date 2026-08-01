extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Runtime.Serialization;
using HornetInHallownest.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Bootstrap;

// Minimal bootstrap of the Silksong singletons HeroController derefs, without their heavy Awake: the GameManager GO
// stays inactive (no Awake fires), and we set _instance + the specific fields/singletons the hero + FSMs read. Each
// block below names the field and the NullRef it prevents.
internal static class SilksongBootstrap {
    private static Silksong::GameManager? bootstrapGm;
    private static Silksong::CameraController? bootstrapCamCtrl;
    private static GameObject? gmGo;
    private static GameObject? poolGo;
    private static bool done;

    // Pass-through to the live Handler.inputActions, not a cached snapshot: if InputHandler.OnAwake/Setup ever re-runs
    // it reassigns inputActions, and a snapshot would drift (InputDriver on the old set, hero on the new -> move_input=0).
    internal static Silksong::HeroActions? InputActions => Handler?.inputActions;

    // The bootstrap InputHandler. Exposed so InputBridge can run the per-frame InputHandler bookkeeping that never
    // happens (its GO is inactive, so InputHandler.Update doesn't run) — e.g. clearing ForceDreamNailRePress.
    internal static Silksong::InputHandler? Handler { get; private set; }

    internal static void Ensure() {
        if (done) return;
        done = true;
        try {
            // Abilities/nailUpgrades are seeded from the Knight's progression at spawn (PlayerDataSync); Hornet-only bits here.
            var pd = Silksong::PlayerData.instance;
            pd.silkMax = 9; // silk resource so silk-cost abilities can fire
            pd.silk = 9;
            pd.IsSilkSpoolBroken = false;
            pd.silkSpecialLevel = 1;
            // The HUD Health FSM enables one mask renderer per health; 0 default -> no masks shown. Grant a full bar.
            pd.maxHealth = 5;
            pd.health = 5;
            // health_display FSM self-shows HP only once bindCutscenePlayed is set (the game sets it after the intro we skip).
            pd.bindCutscenePlayed = true;
            // Equip a valid crest so GetCrestByName resolves (-> nail-art crestConfig). Fresh PD skips [DefaultValue("Hunter")].
            if (string.IsNullOrEmpty(pd.CurrentCrestID)) pd.CurrentCrestID = "Hunter";

            gmGo = new GameObject("Silksong_GameManager");
            gmGo.SetActive(false); // inactive => GameManager/InputHandler Awake never runs
            var gm = gmGo.AddComponent<Silksong::GameManager>();
            var ih = gmGo.AddComponent<Silksong::InputHandler>(); // so gm.GetComponent<InputHandler>() resolves
            // Awake never runs (inactive GO) so inputActions stays null -> every input check NullRefs. Construct it.
            ih.inputActions = new Silksong::HeroActions();
            Handler = ih;

            // OnAwake (allocates buttonQueueTimers) never runs -> GetWasButtonPressedQueued NullRefs (many FSMs + the
            // hero read queued input). Allocate the array (sized by HeroActionButton) so it returns "nothing queued".
            var habType = typeof(Silksong::GlobalEnums.HeroActionButton);
            var habLen = 0;
            foreach (int v in Enum.GetValues(habType)) habLen = Math.Max(habLen, v + 1);
            ih.SetFieldValue("buttonQueueTimers", new float[habLen]);

            // FSM input actions reach the InputHandler via ManagerSingleton<InputHandler>.Instance, whose
            // FindAnyObjectByType fallback excludes inactive objects -> never finds our ih. Register it as the singleton.
            // (HeroController reaches ih directly via gm.GetComponent, which is why basic input worked but FSM actions didn't.)
            typeof(Silksong::ManagerSingleton<Silksong::InputHandler>).SetPropertyValue("UnsafeInstance", ih);

            // gm.inputHandler (set in the skipped SetupGameRefs) -> null makes UIButtonSkins log "...before the Input
            // Handler is ready" (inventory prompts). Assign the backing field to our bootstrap ih.
            gm.SetFieldValue("<inputHandler>k__BackingField", ih);

            // gm.cameraCtrl null -> SendHeroInPosition's ResetStartTimer() NullRefs. Bare CameraController (inactive GO,
            // no heavy Awake). Its camTarget is wired later (SetHeroCtrl, after the rig is up) since FreezeInPlace /
            // HazardRespawn deref it.
            var camCtrl = gmGo.AddComponent<Silksong::CameraController>();
            gm.SetFieldValue("<cameraCtrl>k__BackingField", camCtrl);

            // gm.hero_ctrl (SetupGameRefs, skipped) -> PlayerDeadFromHazard's hero_ctrl.cState.dead NullRefs. Hero isn't
            // spawned yet, so SetHeroCtrl (from SpawnReal) wires it. camCtrl stored for its later camTarget wiring too.
            bootstrapCamCtrl = camCtrl;

            // gm.sm null -> GetTotalFrostSpeed's gm.sm.FrostSpeed (+ other per-scene reads) NullRef. Bare instance => defaults.
            var sm = gmGo.AddComponent<Silksong::CustomSceneManager>();
            gm.SetFieldValue("<sm>k__BackingField", sm);

            // SilkSpool.Instance (set in Awake, skipped): silk-cost FSM actions (AddUsingSilk/…) deref it, and being
            // PlayMaker actions never Finish() on NullRef -> FSM state hangs -> hero stuck. Bare instance suffices.
            var spool = gmGo.AddComponent<Silksong::SilkSpool>();
            typeof(Silksong::SilkSpool).SetPropertyValue("Instance", spool);

            // UpdateBatcher: batched HUD components (JitterSelf, …) do gm.GetComponent<UpdateBatcher>().Add(this) -> NullRef.
            gmGo.AddComponent<Silksong::UpdateBatcher>();

            // TMP_Settings.instance does an untyped Resources.Load -> HK's same-named asset wins, `as Silksong.TMP_Settings`
            // -> null -> the HUD's TextMeshPro Awakes NullRef on defaultStyleSheet (18×). Force-load Silksong's from the
            // bundle (SilksongContext window). accepted cost: 6 transient "missing script" warnings on TMP's default assets
            // (0 persistent, no runtime effect); can't drop TMP, can't avoid the 6. See CLAUDE.md #7.
            try {
                using (SilksongContext.Enter()) {
                    Silksong::TMProOld.TMP_Settings.LoadDefaultSettings();
                }
            } catch (Exception e) {
                Log.Error($"[Bootstrap] TMP_Settings load: {e.Message}");
            }

            // Platform.Current null (set only in Silksong's boot) -> the hero's EnterScene derefs Platform.Current.
            // EnterSceneWait. Assign an uninitialized DesktopPlatform (no ctor/Steam init; EnterSceneWait reads base 0f).
            typeof(Silksong::Platform)
                .SetFieldValue("current", FormatterServices.GetUninitializedObject(typeof(Silksong::DesktopPlatform)));
            Silksong::CheatManager.SceneEntryWait = 0f;

            Object.DontDestroyOnLoad(gmGo);

            // ObjectPool.instance getter does FindObjectOfType, so the pool needs an active GO (Awake sets _instance).
            // Separate GO since gmGo is inactive.
            poolGo = new GameObject("Silksong_ObjectPool");
            poolGo.AddComponent<Silksong::ObjectPool>();
            Object.DontDestroyOnLoad(poolGo);

            Silksong::GameManager._instance = gm;
            bootstrapGm = gm;
            gm.isPaused = false;
            gm.playerData = pd;

            // Two cheap SetupGameRefs/Awake bits worth replicating: gameSettings (~40 readers deref gm.gameSettings.*)
            // as a default safety net, and clearing LogPerformanceWarnings (Awake's first line; clean-log hygiene).
            try {
                gm.gameSettings ??= new Silksong::GameSettings();
                SilksongPM::PlayMakerPrefs.LogPerformanceWarnings = false;
            } catch (Exception e) {
                Log.Error($"[Bootstrap] gameSettings/PlayMakerPrefs: {e.Message}");
            }

            // GameManager.Awake:502 (after SetupGameRefs) sets the PlayMaker global "GameManager" var to the GM GO. FSMs
            // resolve a CallMethodProper target via it (FsmOwnerDefault -> this global); unset -> target null -> silent
            // no-op (e.g. Inventory Control's SetIsInventoryOpen). On the isolated Silksong.PlayMaker globals.
            try {
                var gmVar = SilksongPM::PlayMakerGlobals.Instance?.Variables
                    ?.FindFsmGameObject("GameManager");
                if (gmVar != null) gmVar.Value = gmGo;
                else Log.Error("[Bootstrap] PlayMaker global 'GameManager' var not found");
            } catch (Exception e) {
                Log.Error($"[Bootstrap] PlayMaker global GameManager: {e.Message}");
            }
            // gm.GameState is maintained per-frame by HornetRuntime (mirrors HK's pause), not set here.

            Log.Debug($"[Bootstrap] GameManager.instance={(Silksong::GameManager.instance != null)}, " +
                     $"inputHandler={(gm.GetComponent<Silksong::InputHandler>() != null)}");
        } catch (Exception e) {
            Log.Error($"[Bootstrap] FAILED: {e}");
        }
    }

    // Called from SpawnReal (hero spawned + rig up): wires gm.hero_ctrl (PlayerDeadFromHazard.cState.dead), the bare
    // CameraController's camTarget (FreezeInPlace / HazardRespawn), and gm.screenFader_fsm (hazard-respawn fade).
    internal static void SetHeroCtrl(Silksong::HeroController hero) {
        if (bootstrapGm == null) return;
        try {
            bootstrapGm.SetFieldValue("<hero_ctrl>k__BackingField", hero);

            if (bootstrapCamCtrl != null) {
                var ct = GameCamerasBootstrap.CameraTargetGo?.GetComponent<Silksong::CameraTarget>();
                if (ct != null) bootstrapCamCtrl.camTarget = ct;
            }

            // screenFader_fsm: SetupGameRefs finds it under HudCamera/In-game; the rig is up, so locate + wire it.
            var hudCam = GameCamerasBootstrap.HudCameraGo;
            if (hudCam != null) {
                var hudCamComp = hudCam.GetComponent<Silksong::HUDCamera>();
                var gameplayChild = hudCamComp != null ? hudCamComp.GetPropertyValue<GameObject>("GameplayChild") : null;
                var sf = gameplayChild != null ? Silksong::FSMUtility.LocateFSM(gameplayChild, "Screen Fader") : null;
                if (sf != null) bootstrapGm.SetFieldValue("<screenFader_fsm>k__BackingField", sf);
            }

            Log.Debug("[Bootstrap] hero_ctrl + camTarget + screenFader wired");
        } catch (Exception e) {
            Log.Error($"[Bootstrap] SetHeroCtrl: {e.Message}");
        }
    }

    internal static void Cleanup() {
        // DestroyImmediate (not Destroy): Unload->Initialize runs synchronously, so a deferred Destroy would leave the
        // old GM/InputHandler alive during rebuild -> the new hero/FSM lookups bind to stale instances. Sync teardown.
        if (gmGo != null) {
            Object.DestroyImmediate(gmGo);
            gmGo = null;
        }

        if (poolGo != null) {
            Object.DestroyImmediate(poolGo);
            poolGo = null;
        }

        Silksong::GameManager._instance = null;
        Handler = null; // InputActions is computed from Handler, so this clears it too
        done = false;
    }
}
