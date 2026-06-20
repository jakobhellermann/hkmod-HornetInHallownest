extern alias Silksong;
using System;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Minimal bootstrap of the Silksong runtime singletons that HeroController dereferences, WITHOUT running their real
// (heavy, environment-dependent) Awake. The GameManager GO stays inactive so no Awake fires; we set the public static
// _instance + the few fields HeroController reads (isPaused, playerData, an InputHandler component for
// gm.GetComponent<InputHandler>()). Grown iteratively as spawn-real reveals the next missing field.
internal static class SilksongBootstrap {
    private static GameObject? gmGo;
    private static GameObject? poolGo;
    private static bool done;

    // The HeroActions InControl set InputBridge drives each frame. Pass-through to the live Handler.inputActions (NOT a
    // cached snapshot): if anything ever re-runs InputHandler.OnAwake/Setup it reassigns inputActions, and a snapshot
    // would silently drift — InputDriver driving the old set while the hero reads the new one (move_input=0,
    // ia_same=false). Always dereferencing Handler keeps driver and hero on the same object by construction.
    internal static Silksong::HeroActions? InputActions => Handler?.inputActions;

    // The bootstrap InputHandler. Exposed so InputBridge can run the per-frame InputHandler bookkeeping that never
    // happens (its GO is inactive, so InputHandler.Update doesn't run) — e.g. clearing ForceDreamNailRePress.
    internal static Silksong::InputHandler? Handler { get; private set; }

    internal static void Ensure() {
        if (done) return;
        done = true;
        try {
            var pd = Silksong::PlayerData.instance; // create/get the PlayerData singleton
            // Fresh PlayerData has every ability locked (e.g. HeroController.CanDash() gates on playerData.hasDash).
            // Grant Hornet's full movement + combat kit so every move is usable in the playground.
            pd.hasDash = true;          // dash (Swift Step)
            pd.hasWalljump = true;      // wall jump
            pd.hasDoubleJump = true;    // double jump
            pd.hasBrolly = true;        // float / glide (umbrella)
            pd.hasSuperJump = true;     // needle super-jump
            pd.hasHarpoonDash = true;   // silk harpoon dash
            pd.hasChargeSlash = true;   // charge slash (nail art)
            pd.hasQuill = true;
            pd.hasParry = true;
            pd.hasNeedolin = true;
            pd.hasNeedleThrow = true;
            pd.hasThreadSphere = true;
            pd.hasSilkSpecial = true;   // silk special / arts
            pd.hasSilkCharge = true;
            pd.hasSilkBomb = true;
            pd.hasSilkBossNeedle = true;
            pd.hasNeedolinMemoryPowerup = true;
            // Silk resource so silk-cost abilities can actually fire.
            pd.silkMax = 9;
            pd.silk = 9;
            pd.silkSpecialLevel = 1;
            // Health: the HUD's Health FSM enables N mask MeshRenderers for N health; with health/maxHealth at their
            // default 0 every mask renderer stays disabled (masks active-in-hierarchy but Renderer.enabled=false) ->
            // no health visible. Grant a full bar so the FSM enables the masks on HUD bring-up.
            pd.maxHealth = 5;
            pd.health = 5;
            // The health-mask FSM (health_display) waits in "First Pause" and self-sends "SHOW HP" (-> appear) gated on
            // PlayerData.bindCutscenePlayed — the game sets this after the intro bind cutscene; until then the HUD health
            // stays hidden. We skip the intro, so set it true -> masks self-appear on HUD bring-up (no manual drive).
            pd.bindCutscenePlayed = true;
            // Equip a valid crest so ToolItemManager.GetCrestByName(CurrentCrestID) resolves (-> nail arts get a
            // crestConfig, and the inventory crest carousel has a selection). Fresh PlayerData doesn't apply the
            // [DefaultValue("Hunter")] attribute, so set it explicitly. "Hunter" is the starting crest.
            if (string.IsNullOrEmpty(pd.CurrentCrestID)) pd.CurrentCrestID = "Hunter";

            gmGo = new GameObject("Silksong_GameManager");
            gmGo.SetActive(false); // inactive => GameManager/InputHandler Awake never runs
            var gm = gmGo.AddComponent<Silksong::GameManager>();
            var ih = gmGo.AddComponent<Silksong::InputHandler>(); // so gm.GetComponent<InputHandler>() resolves
            // GO is inactive => InputHandler.Awake never runs, so inputActions stays null and every input check
            // (CanAttackAction, ListenForAttack/Dash/... FSM actions) NullRefs. Construct it like InputHandler does.
            ih.inputActions = new Silksong::HeroActions();
            Handler = ih;

            // InputHandler.OnAwake (which allocates buttonQueueTimers) never runs on our inactive GO, so
            // GetWasButtonPressedQueued NullRefs. Many of Hornet's FSMs (sprint, brolly/float, …) and HeroController
            // read queued input that way, stalling FSM-driven moves the moment they hand off to that path. Allocate the
            // array (sized by the HeroActionButton enum) so it returns "nothing queued" instead of throwing.
            var habType = typeof(Silksong::GlobalEnums.HeroActionButton);
            var habLen = 0;
            foreach (int v in Enum.GetValues(habType)) habLen = Math.Max(habLen, v + 1);
            typeof(Silksong::InputHandler).GetField("buttonQueueTimers", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(ih, new float[habLen]);

            // FSM input actions (GetWasButtonPressedQueued, …) read the InputHandler via
            // ManagerSingleton<InputHandler>.Instance, whose fallback is FindAnyObjectByType<T>() — which EXCLUDES
            // inactive objects, so it never finds our ih on the inactive bootstrap GO (-> NullRef in the action).
            // HeroController reaches ih directly (gm.GetComponent), which is why basic input worked but FSM actions
            // didn't. Register ih as the singleton so the FSM path resolves too.
            typeof(Silksong::ManagerSingleton<Silksong::InputHandler>)
                .GetProperty("UnsafeInstance", BindingFlags.Public | BindingFlags.Static)
                ?.GetSetMethod(true)?.Invoke(null, new object[] { ih });

            // gm.inputHandler (private-set property) is assigned in SetupGameRefs (GetComponent<InputHandler>), which we
            // don't run -> null. UIButtonSkins reads it (ih = GameManager.instance.inputHandler) and logs "Attempting to
            // get button skins before the Input Handler is ready" when null (inventory button prompts). Assign the
            // backing field to our bootstrap ih.
            typeof(Silksong::GameManager).GetField("<inputHandler>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(gm, ih);

            // gm.cameraCtrl (private-set property) is null -> SendHeroInPosition's gm.cameraCtrl.ResetStartTimer()
            // NullRefs. Provide a bare CameraController (on the inactive GO => no heavy Awake); ResetStartTimer just
            // sets a float. Assign via the property's backing field.
            var camCtrl = gmGo.AddComponent<Silksong::CameraController>();
            typeof(Silksong::GameManager).GetField("<cameraCtrl>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(gm, camCtrl);

            // gm.sm (CustomSceneManager, private-set) is null -> GetTotalFrostSpeed's gm.sm.FrostSpeed NullRefs (and
            // other per-scene env reads). Bare instance => default scene settings (FrostSpeed 0 etc.).
            var sm = gmGo.AddComponent<Silksong::CustomSceneManager>();
            typeof(Silksong::GameManager).GetField("<sm>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(gm, sm);

            // SilkSpool singleton: silk-cost FSM actions (AddUsingSilk/RemoveUsingSilk in Superjump etc.) deref
            // SilkSpool.Instance, which is set in Awake — never runs on the inactive GO. Without it AddUsingSilk.OnEnter
            // NullRefs and, being a PlayMaker action, never calls Finish() -> the FSM state hangs -> hero stuck until
            // respawn. A bare instance with Instance set manually is enough: usingSilk is inline-initialized, RefreshSilk
            // early-returns while the HUD spool is undrawn (hasDrawnSpool=false), RefreshBindNotch no-ops (bindNotch null).
            var spool = gmGo.AddComponent<Silksong::SilkSpool>();
            typeof(Silksong::SilkSpool).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetSetMethod(true)?.Invoke(null, new object[] { spool });

            // HUD bring-up needs: UpdateBatcher (batched HUD components like JitterSelf do
            // GameManager.instance.GetComponent<UpdateBatcher>().Add(this) -> NullRef without it) + TMP_Settings.
            gmGo.AddComponent<Silksong::UpdateBatcher>();

            // TMProOld.TMP_Settings.instance does untyped Resources.Load("TMP Settings") -> HK's same-named asset wins,
            // `as Silksong.TMP_Settings` -> null -> defaultStyleSheet NullRefs from HUD text. Force-load Silksong's from
            // the bundle (PreferBundle window, same trick as localization) so s_Instance caches the correct-typed asset.
            // TMP_Settings is REQUIRED: the in-game HUD (under Anchor TL) has TextMeshPro components whose Awake reads
            // TMP_Settings.defaultStyleSheet -> 18 NullRefs (TextMeshPro.Awake) without it, which also break the health-
            // mask setup. TMProOld.TMP_Settings.instance does an UNTYPED Resources.Load("TMP Settings") -> HK's same-named
            // asset wins, `as Silksong.TMP_Settings` -> null. Force-load Silksong's from the bundle (PreferBundle window,
            // same trick as localization) so s_Instance caches the correct-typed asset.
            // ACCEPTED COST (bisected via Player.log markers): loading TMP's default assets from the bundle emits exactly
            // 6 transient "missing script" warnings (3 "(Unknown)" + 3 "(Game Object '<null>')") — unresolved-script
            // components on TMP default assets, discarded immediately (/scan-missing = 0 persistent, no runtime effect).
            // Can't drop TMP (-> 18 NullRefs) and can't surgically avoid the 6; understood + accepted. See CLAUDE.md #7.
            try {
                ResourcesShim.PreferBundle = true;
                Silksong::TMProOld.TMP_Settings.LoadDefaultSettings();
            } catch (Exception e) { Log.Error($"[Bootstrap] TMP_Settings load: {e.Message}"); }
            finally { ResourcesShim.PreferBundle = false; }

            // Platform.Current (private static `current`, set only during Silksong's boot which we never run) is null ->
            // the hero's EnterScene prologue derefs Platform.Current.EnterSceneWait -> NullRef. Assign an UNINITIALIZED
            // DesktopPlatform (no Awake/ctor, so no Steam/save-system init): EnterScene only reads EnterSceneWait
            // (base => 0f, no field access), so an empty instance suffices. Also force SceneEntryWait >= 0 (default is
            // -0.1f) so the entry has zero startup delay.
            typeof(Silksong::Platform).GetField("current", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, FormatterServices.GetUninitializedObject(typeof(Silksong::DesktopPlatform)));
            Silksong::CheatManager.SceneEntryWait = 0f;

            Object.DontDestroyOnLoad(gmGo);

            // ObjectPool singleton: ObjectPool.CountPooled derefs ObjectPool.instance. Its getter does
            // FindObjectOfType<ObjectPool>(), so the pool must be on an ACTIVE GO (Awake sets _instance, and
            // pooledObjects is inline-initialized). Separate GO since gmGo is inactive.
            poolGo = new GameObject("Silksong_ObjectPool");
            poolGo.AddComponent<Silksong::ObjectPool>();
            Object.DontDestroyOnLoad(poolGo);

            Silksong::GameManager._instance = gm;
            gm.isPaused = false;
            gm.playerData = pd;
            // gm.GameState (LookForInput gates on == PLAYING) is maintained per-frame by HornetEnvironmentAdapter,
            // which also mirrors HK's pause onto it — so it's not set here.

            Log.Info($"[Bootstrap] GameManager.instance={(Silksong::GameManager.instance != null)}, " +
                     $"playerData={pd != null}, inputHandler={(gm.GetComponent<Silksong::InputHandler>() != null)}");
        } catch (Exception e) {
            Log.Error($"[Bootstrap] FAILED: {e}");
        }
    }

    internal static void Cleanup() {
        // DestroyImmediate (not Destroy): Unload->Initialize runs synchronously in one frame, so deferred end-of-frame
        // Destroys would leave the old GM/InputHandler alive while Initialize rebuilds — the spawned hero (and FSM
        // singleton lookups) could then bind to the stale instances. Tear down synchronously so a hot-reload state
        // equals a clean startup. Also null the static refs (InputActions/Handler) so nothing dangles into the dead GO.
        if (gmGo != null) { Object.DestroyImmediate(gmGo); gmGo = null; }
        if (poolGo != null) { Object.DestroyImmediate(poolGo); poolGo = null; }
        Silksong::GameManager._instance = null;
        Handler = null; // InputActions is computed from Handler, so this clears it too
        done = false;
    }
}
