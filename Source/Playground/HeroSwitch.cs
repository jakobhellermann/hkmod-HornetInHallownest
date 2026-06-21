extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GlobalEnums;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

internal enum ActiveHero {
    Knight,
    Hornet
}

// Switch which character the player controls (Knight = HK's hero, Hornet = the spawned Silksong hero). The OTHER stays
// visible (its renderer keeps drawing) but inert: HeroController disabled (stops input/movement) + Rigidbody2D.simulated
// off (so it doesn't fall away under gravity while frozen).
//
// Camera: HK owns the single rendering camera. The chain is GameCameras.instance.cameraController -> follows
// cameraTarget (CameraTarget), which in FOLLOW_HERO/LOCK_ZONE follows its private `heroTransform` (normally
// HeroController.instance.transform == the Knight). We retarget by pointing `heroTransform` at the active hero — so ALL
// of HK's native camera behaviour (damping, lock zones, scene bounds) is reused for free, no per-frame position driving.
// hero_ctrl is left as the Knight (non-null; CameraTarget only null-checks it, CameraController uses it for the
// cosmetic look-up/down offset) so we don't disturb HK's camera bring-up.
internal static class HeroSwitch {
    // When true, the inactive hero is hidden (its sprite MeshRenderer disabled) instead of left visible-but-frozen, so a
    // switch shows only the active character. Toggle off to see both (the old debug behaviour).
    private const bool HideInactiveHero = true;

    private static GameObject? go;
    private static FieldInfo? heroTransformField;
    private static Hook? canInteractHook;
    private static Hook? canInputHook;
    private static Hook? getStateHook;

    // The Knight's standalone input-listening ability FSMs: they fire independently of HeroController.enabled, so they
    // must be disabled to stop the inert Knight from cdashing/casting/nail-arting on shared input. (NOT controlReqlinquished
    // — that's coupled to HK's door-entry; see SetInert.)
    private static readonly string[] AbilityFsms = { "Superdash", "Spell Control", "Nail Arts" };

    internal static ActiveHero Active { get; private set; } = ActiveHero.Knight;
    internal static bool HornetActive => Active == ActiveHero.Hornet;

    // The GameObject of the hero the player currently controls: the spawned Hornet while she's active, else HK's Knight.
    // SINGLE SOURCE OF TRUTH for the "redirect HK's hero reference to the active hero" consumers — HeroProxy (PlayMaker
    // global "Hero" var), EnemyTargetBridge (the GetHero action), GameObjectFindShim ("Player" tag). To add a new such
    // consumer, resolve the hero through this (don't re-derive HornetActive ? RealHero : Knight). Null only before the
    // Knight exists. See [[hero-fsm-real-hornet-strategy]].
    internal static GameObject? ActiveHeroGameObject =>
        HornetActive && BundleSpike.RealHero != null ? BundleSpike.RealHero.gameObject :
        HeroController.UnsafeInstance != null ? HeroController.UnsafeInstance.gameObject : null;

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.HeroSwitch");
        go.AddComponent<CameraSwitchDriver>();
        Object.DontDestroyOnLoad(go);

        // Seam consumer redirect (Policy B): HK's interaction FSMs (doors/NPCs/benches) gate on the hero's state by
        // calling HeroController methods on the "Player"-tagged GameObject == the KNIGHT. While Hornet is active the
        // Knight is correctly inert (HeroController disabled, controlReqlinquished, acceptingInput=false), so those
        // checks return "no" and the interaction's "Enter"/bench prompt flashes but nothing happens. The Knight's inert
        // state is RIGHT; the consumer is wrong to read it instead of the active hero's. Redirect each such query to
        // Hornet when she's active, else the Knight. One spot covers all interactables (catches FSM/reflection callers
        // too, since the detour replaces the method).
        //
        // Three methods, found empirically by tracing which door FSMs cancel (see CLAUDE.md): simple shop doors
        // (sly/bretta) gate on CanInteract(); the map-shop door's "Can Enter?" instead calls CanInput() + GetState()x6;
        // benches read state the same way. All exist on both HeroControllers with identical signatures+semantics
        // (GetState -> cState.GetState(name); the cState names are shared across the two games), so redirecting to
        // RealHero is safe. The HK detours only ever see HK HeroController instances (the Knight) — Hornet is a
        // Silksong.HeroController, a different type — so "redirect while HornetActive" can't accidentally recurse.
        canInteractHook = new Hook(
            typeof(HeroController).GetMethod(nameof(HeroController.CanInteract)),
            (Func<Func<HeroController, bool>, HeroController, bool>)((orig, self) =>
                HornetActive && BundleSpike.RealHero != null ? BundleSpike.RealHero.CanInteract() : orig(self)));
        canInputHook = new Hook(
            typeof(HeroController).GetMethod(nameof(HeroController.CanInput)),
            (Func<Func<HeroController, bool>, HeroController, bool>)((orig, self) =>
                HornetActive && BundleSpike.RealHero != null ? BundleSpike.RealHero.CanInput() : orig(self)));
        getStateHook = new Hook(
            typeof(HeroController).GetMethod(nameof(HeroController.GetState)),
            (Func<Func<HeroController, string, bool>, HeroController, string, bool>)((orig, self, s) =>
                HornetActive && BundleSpike.RealHero != null ? BundleSpike.RealHero.GetState(s) : orig(self, s)));

        Log.Info("[HeroSwitch] installed (Tab toggles Knight<->Hornet; /switch route)");
    }

    internal static void Cleanup() {
        canInteractHook?.Dispose();
        canInteractHook = null;
        canInputHook?.Dispose();
        canInputHook = null;
        getStateHook?.Dispose();
        getStateHook = null;
        if (go != null) {
            Object.Destroy(go);
            go = null;
        }

        Active = ActiveHero.Knight; // leave HK's Knight controllable after unload
        SetInert(HeroController.UnsafeInstance != null ? HeroController.UnsafeInstance.gameObject : null, false);
        RetargetCamera(HeroController.UnsafeInstance != null ? HeroController.UnsafeInstance.transform : null);
    }

    internal static object Toggle() {
        return SetActive(HornetActive ? ActiveHero.Knight : ActiveHero.Hornet);
    }

    internal static object SetActive(ActiveHero who) {
        var hornet = BundleSpike.RealHero;
        if (who == ActiveHero.Hornet && hornet == null)
            return new { error = "Hornet not spawned (POST /spawn-real first)", active = Active.ToString() };

        var prev = Active;
        Active = who;
        var knightGo = HeroController.UnsafeInstance != null ? HeroController.UnsafeInstance.gameObject : null;
        var hornetGo = BundleSpike.HornetRoot;

        // Knight inert when Hornet is active, and vice-versa. Hornet's enabled state is owned per-frame by
        // HornetEnvironmentAdapter (gated on HornetActive) — here we only flip her Rigidbody so she doesn't fall.
        SetInert(knightGo, who != ActiveHero.Knight);
        SetInert(hornetGo, who != ActiveHero.Hornet);
        if (hornet != null) hornet.enabled = who == ActiveHero.Hornet; // adapter re-asserts true while HornetActive

        // Hand off in place: move the newly-active hero to where the previously-active one stood, so control + camera
        // stay on the same spot (only the character changes). Skip when re-applying the same hero (e.g. at spawn).
        if (who != prev) {
            var newT = who == ActiveHero.Hornet ? hornetGo?.transform : knightGo?.transform;
            var oldT = prev == ActiveHero.Hornet ? hornetGo?.transform : knightGo?.transform;
            if (newT != null && oldT != null && newT != oldT) {
                newT.position = oldT.position;
                var rb = newT.GetComponent<Rigidbody2D>();
                if (rb != null && rb.simulated) rb.linearVelocity = Vector2.zero;
            }
        }

        var follow = who == ActiveHero.Hornet ? hornetGo?.transform : knightGo?.transform;
        RetargetCamera(follow);

        // Log.Info($"[HeroSwitch] active={Active} following={(follow != null ? follow.name : "?")}");
        return new { active = Active.ToString(), following = follow != null ? follow.name : null };
    }

    // Visible-but-frozen: keep the renderer, stop physics so it doesn't drift/fall. HeroController of the Knight is
    // toggled here (HK's hero); Hornet's HeroController is left to the adapter.
    private static void SetInert(GameObject? hero, bool inert) {
        if (hero == null) return;
        // GetComponent<global::HeroController> only matches HK's Knight (Hornet is a Silksong.HeroController), so the
        // vignette handling below applies exclusively to the Knight.
        var hk = hero.GetComponent<HeroController>();
        if (hk != null) {
            hk.enabled = !inert;
            // Seam policy B (Hornet is the real hero): turn the inert Knight genuinely OFF. enabled=false stops its
            // Update, but its ability FSMs (Superdash/cdash, Spell Control, Nail Arts) are SEPARATE components that
            // listen for HK input independently and fire -> cdash leak. Disable them directly. We deliberately do NOT
            // touch controlReqlinquished: it's coupled to HK's door-entry chain (which still reads the Knight) and would
            // break Hornet's door transitions — that consumer gets patched on the Hornet side instead.
            SetAbilityFsms(hk, !inert);
            // A Hornet-started transition relinquishes the Knight (HK sets controlReqlinquished at transition start), but
            // with the Knight inert its EnterScene never reaches the RegainControl at the end -> the flag sticks and
            // blocks the Knight's double jump / abilities (CanDoubleJump etc. gate on !controlReqlinquished) until a bench
            // RegainControl. When the Knight becomes active again, restore control (RegainControl is a near-no-op if it
            // already has control).
            if (!inert) {
                hk.RegainControl();
                // Anim equivalent: HK's transition StopControl's the HeroAnimationController (controlEnabled=false) and
                // re-StartControl's it at the end — but on the inert Knight that closing StartControl never ran, so HAC
                // stays controlEnabled=false and freezes on the entry clip ("Exit Door To Idle" stuck mid-frame). Restore
                // it so HAC drives normal locomotion again. (No-op if already enabled.)
                hk.GetComponent<HeroAnimationController>()?.StartControl();
            }

            // The Knight's screen-edge vignette would otherwise keep darkening the view while she's the inactive hero;
            // kill the renderer + its FSM (so nothing re-enables it), restore when she's active again.
            if (hk.vignette != null) hk.vignette.enabled = !inert;
            if (hk.vignetteFSM != null) hk.vignetteFSM.enabled = !inert;
        }
        else {
            // Hornet's "Vignette": the soft radial darkening (its own SpriteRenderer) follows her active state — show it
            // only while she's the active, camera-centred hero.
            var v = hero.transform.Find("Vignette");
            if (v != null) {
                v.gameObject.SetActive(!inert);
                // "Darkness Border" (hard black frame, black_solid*) is meant to be positioned by Silksong's camera rig;
                // standalone it's pinned to Hornet and blacks out a chunk of HK's screen wherever she stands. It never
                // works here -> kill it outright, keep only the radial vignette.
                var border = v.Find("Darkness Border");
                if (border != null) border.gameObject.SetActive(false);
            }
        }

        var rb = hero.GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = !inert;

        // Hide the inactive hero so a switch shows only the active character (tk2d heroes are a single sprite/MeshRenderer
        // on the root — toggling it hides/shows the whole body). The death-revive's own renderer re-enable still wins when
        // she's the active hero. Gated on the const so the visible-both debug behaviour is one flip away.
        if (HideInactiveHero) {
            var mr = hero.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = !inert;
        }

        // Freeze animation while inert. The DRIVER (HeroAnimationController, both games) must go first: it runs every
        // frame independently of HeroController and calls Play() -> dereferences animator.CurrentClip.name. We then
        // PAUSE the tk2d animator instead of disabling it — disabling runs OnDisable->Stop which nulls CurrentClip, so
        // the driver's Play() NullRefs (while inert AND on reactivation). Pause keeps the current clip + frame.
        foreach (var d in hero.GetComponentsInChildren<MonoBehaviour>(true))
            if (d != null && d.GetType().Name == "HeroAnimationController")
                d.enabled = !inert;
        foreach (var a in hero.GetComponentsInChildren<tk2dSpriteAnimator>(true))
            if (inert) a.Pause();
            else a.Resume();
        foreach (var a in hero.GetComponentsInChildren<Animator>(true)) a.enabled = !inert;
    }

    private static void SetAbilityFsms(HeroController hk, bool enabled) {
        foreach (var fsm in hk.GetComponents<PlayMakerFSM>())
            if (Array.IndexOf(AbilityFsms, fsm.FsmName) >= 0)
                fsm.enabled = enabled;
    }

    // Point HK's CameraTarget at `t` so HK's native camera chain follows it. Idempotent + cheap (one reflected set).
    internal static void RetargetCamera(Transform? t) {
        if (t == null) return;
        var gc = GameCameras.instance;
        var camTarget = gc != null ? gc.cameraTarget : null;
        if (camTarget == null) return;
        heroTransformField ??= typeof(CameraTarget)
            .GetField("heroTransform", BindingFlags.Instance | BindingFlags.NonPublic);
        heroTransformField?.SetValue(camTarget, t);
    }
}

// Drives the active-hero switch: Tab toggles; re-asserts the camera target each frame (cheap) so it survives HK
// re-grabbing HeroController.instance on scene init. Early execution order so the retarget lands before
// CameraTarget.Update (order 0) reads heroTransform the same frame.
//
// Scene transitions: only HK's Knight is HK's transition vehicle — it gets relocated to the new scene's entry gate.
// Hornet is DontDestroyOnLoad and keeps her old world coordinates, so after a transition she's stranded in random
// geometry / off in nirvana, and the camera (following her, or her after a Tab) points there. So on every scene change
// we snap Hornet onto the Knight once HK reports the Knight is positioned (isHeroInPosition), which keeps both heroes in
// the playable area of the new scene.
[DefaultExecutionOrder(-8000)]
internal sealed class CameraSwitchDriver : MonoBehaviour {
    // --- Keybind trace recorder (F9 toggles) --- captures Hornet's per-FRAME state (finer than the 12Hz HTTP poll) so a
    // repro doesn't have to race a fixed poll window. Writes a TSV on stop; read /tmp/hornet_trace_live.tsv afterwards.
    private const string TracePath = "/tmp/hornet_trace_live.tsv";
    private bool hkEntryFixed;
    private string? lastScene;
    private bool pendingSnap;
    private List<string>? traceBuf;
    private float traceT0;
    private bool tracing;

    private void Update() {
        var knight = HeroController.UnsafeInstance;

        // Detect a scene change; defer the Hornet snap until the Knight has actually been placed at the new entry.
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != lastScene) {
            lastScene = scene;
            pendingSnap = true;
            hkEntryFixed = false;
        }

        CompleteStuckHkVerticalEntry(knight);
        if (pendingSnap && knight != null && knight.isHeroInPosition) {
            // When Hornet is the active hero, run her REAL Silksong scene-entry (walk/drop-in animation + entry FSMs)
            // from HK's mirrored gate. When the Knight is active, Hornet is an inert prop -> just relocate her.
            if (HeroSwitch.HornetActive && HornetSceneEntry.Enabled && knight.sceneEntryGate != null)
                StartCoroutine(HornetSceneEntry.Run(knight));
            else
                SnapHornetToKnight(knight);
            pendingSnap = false;
        }

        // Tab = switch hero. Forbid it while the inventory is open: switching heroes mid-inventory leaves a broken state
        // (the inventory belongs to the active hero + freezes the world via DisplayFrozenCamera). Detect "inventory open"
        // by the side effect we already know — DisplayFrozenCamera.Freeze disabled HK's main camera — so block Tab while
        // the world is frozen rather than half-switch. (Close-then-switch would be nicer but risks the close not running.)
        if (Input.GetKeyDown(KeyCode.Tab)) {
            var gcam = global::GameCameras.instance;
            if (gcam != null && gcam.mainCamera != null && !gcam.mainCamera.enabled)
                Log.Info("[HeroSwitch] Tab ignored — world frozen (inventory open); close it first");
            else
                HeroSwitch.Toggle();
        }
        TraceTick();

        // The inert, off-camera Knight's Vignette must be fully off (its black plates otherwise frame HK's screen
        // centered on the off-screen Knight). Toggle the whole Vignette GAMEOBJECT, not just hk.vignette.enabled: the
        // hard border is the `black_solid` plates under "Darkness Border", which are SEPARATE child SpriteRenderers the
        // component-level disable misses (deactivating the GO also stops the vignetteFSM that re-enables them). Sync to
        // the active hero each frame — HK re-enables it on every scene entry, and the Knight needs it back when active.
        if (knight != null && knight.vignette != null) {
            var vigGo = knight.vignette.gameObject;
            var shouldBeOn = !HeroSwitch.HornetActive;
            if (vigGo.activeSelf != shouldBeOn) vigGo.SetActive(shouldBeOn);
        }

        var follow = HeroSwitch.HornetActive
            ? BundleSpike.HornetRoot?.transform
            : knight != null
                ? knight.transform
                : null;
        HeroSwitch.RetargetCamera(follow);

        // Keep Silksong's neutered camera on HK's camera so Hornet's 3D SFX aren't distance-culled (see SyncAudioCamera).
        GameCamerasBootstrap.SyncAudioCamera();

        // Point HK's PlayMaker global "Hero" at the active hero (enemy chase/face track her; hero method calls go to her).
        // Re-asserted per frame because HK re-binds it on scene entry — same reason the vignette/HUD are synced here.
        HeroProxy.SyncGlobal();

        // HUD follows the active hero: Hornet's HUD (if brought up) while she's active, HK's Knight HUD otherwise. Synced
        // per-frame (cheap, SetActive-on-change) because HK re-enables its hudCanvas on every scene entry — same reason
        // the vignette is synced here. When Hornet's HUD isn't up yet, leave HK's alone (don't blank the screen).
        if (GameCamerasBootstrap.HornetHudReady) {
            GameCamerasBootstrap.SetHornetHudVisible(HeroSwitch.HornetActive);
            GameCamerasBootstrap.SetHkHudVisible(!HeroSwitch.HornetActive);
        }
        else {
            GameCamerasBootstrap.SetHkHudVisible(true); // no Hornet HUD -> always show HK's
        }
    }

    // Principled completion of HK's scene-entry handshake that the inert Knight can no longer finish. HK's GameManager
    // delegates entry to hero_ctrl (the Knight); its HeroController.EnterScene calls gm.FinishedEnteringScene()
    // (-> gameState ENTERING_LEVEL -> PLAYING) at the end. TOP-gate / horizontal entries complete on a fixed timer, but
    // the BOTTOM-gate branch (an "up" transition) ends at transitionState=DROPPING_DOWN and only completes once the
    // Knight physically LANDS (its OnCollisionEnter2D). While Hornet is active the Knight is inert (HeroController
    // disabled + Rigidbody2D.simulated off), so it never falls/lands -> gameState stays ENTERING_LEVEL forever -> HK's
    // TransitionPoint.TryDoTransition bails (`gm.gameState != PLAYING`) for EVERY later gate -> Hornet falls through
    // wells (this also strands her on the NEXT transition, not just the up one). Since we took the hero role from the
    // Knight, we own its half of HK's handshake: call HK's own public FinishedEnteringScene() (which also fires
    // OnFinishedEnteringScene that HK scene-setup hangs off, not just the gameState flip). Deterministic on state — no
    // timer/threshold — gated to the one branch the inert Knight breaks; self-guarding (gameState flips to PLAYING) plus
    // a once-per-scene flag. NOTE: Hornet's OWN movement freeze on up-entries (she lands but loses no_input so her
    // OnCollisionEnter2D completion is missed) is a sibling bug, logged by HornetEnvironmentAdapter.StuckEntryWatch.
    private void CompleteStuckHkVerticalEntry(HeroController? knight) {
        if (hkEntryFixed || !HeroSwitch.HornetActive || knight == null || knight.enabled)
            return; // only the INERT Knight
        var gm = GameManager.UnsafeInstance;
        if (gm == null || gm.gameState != GameState.ENTERING_LEVEL) return;
        if (knight.transitionState != HeroTransitionState.DROPPING_DOWN) return; // reached the terminal landing-wait
        var gate = knight.sceneEntryGate;
        if (gate == null || gate.GetGatePosition() != GatePosition.bottom)
            return; // only the non-self-completing branch
        gm.FinishedEnteringScene();
        hkEntryFixed = true;
        Log.Info("[CameraSwitch] inert Knight stuck in bottom-gate entry (DROPPING_DOWN + gameState=ENTERING_LEVEL) "
                 + "-> called gm.FinishedEnteringScene() to complete HK's handshake (unblocks transitions)");
    }

    private static void SnapHornetToKnight(HeroController knight) {
        var hornet = BundleSpike.HornetRoot;
        if (hornet == null) return;
        hornet.transform.position = knight.transform.position;
        var rb = hornet.GetComponent<Rigidbody2D>();
        if (rb != null && rb.simulated) rb.linearVelocity = Vector2.zero; // don't carry pre-transition momentum
    }

    private void TraceTick() {
        if (Input.GetKeyDown(KeyCode.F9)) {
            if (!tracing) {
                tracing = true;
                traceT0 = Time.realtimeSinceStartup;
                traceBuf = new List<string> {
                    "t\tscene\ttransState\theroState\tonGround\tcReq\tvx\tvy\tx\ty"
                };
                Log.Info("[Trace] recording started (F9 to stop)");
            }
            else {
                tracing = false;
                try {
                    File.WriteAllLines(TracePath, traceBuf!);
                    Log.Info($"[Trace] wrote {traceBuf!.Count - 1} frames -> {TracePath}");
                } catch (Exception e) {
                    Log.Error($"[Trace] write failed: {e.Message}");
                }

                traceBuf = null;
            }
        }

        if (!tracing) return;
        var hc = BundleSpike.RealHero;
        if (hc == null) return;
        var p = hc.transform.position;
        var rb = hc.GetComponent<Rigidbody2D>();
        var v = rb != null ? rb.linearVelocity : Vector2.zero;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        traceBuf!.Add(string.Format(CultureInfo.InvariantCulture,
            "{0:F2}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6:F1}\t{7:F1}\t{8:F1}\t{9:F1}",
            Time.realtimeSinceStartup - traceT0, scene, hc.transitionState, hc.hero_state,
            hc.cState.onGround, hc.controlReqlinquished, v.x, v.y, p.x, p.y));
    }
}
