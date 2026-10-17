extern alias Silksong;
using System;
using System.Collections;
using System.Reflection;
using GlobalEnums;
using HornetInHallownest.Core;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using SGate = Silksong::GlobalEnums.GatePosition;
using SHeroTransition = Silksong::GlobalEnums.HeroTransitionState;
using SCollisionSide = Silksong::GlobalEnums.CollisionSide;
using SHazardType = Silksong::GlobalEnums.HazardType;
using SDamageFlags = Silksong::GlobalEnums.DamagePropertyFlags;

namespace HornetInHallownest.Modules;

// Move hornet in HK-driven scene transition.
// Defer to real silksong LeaveScene / EnterScene / FinishedEnteringScene.
public sealed class SceneTransitionModule : ModuleBase {
    private GameObject? driverGo;

    private bool pendingSnap, dreamReturnPending, hkEntryFixed, dreamGateEntryPending;
    private bool cameraCaughtUp;

    // Big-lift arrival (PlayerData.liftArrival): the "Lift Move" FSM carries the hero by parenting her to the moving
    // elev_main, so our normal gate entry must not run (it deparents her mid-ride and strands her in the shaft).
    private bool liftArrivalPending;

    // A dream-visualization arrival is in flight. HK warps dream entries in via EnterSceneDreamGate (gravity off, no door
    // walk-out); route Hornet the same way even when HK drove the Knight through a normal gate, else RunEntry's door
    // walk-out walks/falls her off the arena platform.
    private bool dreamArrivalPending;
    private bool dreamHeroPlaced;
    private bool arrivalInvulnerable; // block hazard damage through the dream-arrival window (park + placement settle)
    private bool dreamPending;
    private bool playingDreamArrival;
    private bool lieDownArrival;
    private bool fromMaskBreak;

    private string? dreamGate;

    public override string Id => "scene-transition";

    public override void Initialize() {
        InstallLeaveHooks();
        InstallEnterHooks();
        USceneManager.activeSceneChanged += OnActiveSceneChanged;

        driverGo = new GameObject("HornetInHallownest.SceneTransitionDriver");
        driverGo.AddComponent<SceneTransitionDriver>().Module = this;
        driverGo.AddComponent<SceneEntryCameraGlue>().Module = this;
        Object.DontDestroyOnLoad(driverGo);
    }

    protected override void OnDeinitialize() {
        USceneManager.activeSceneChanged -= OnActiveSceneChanged;
#if HK1578
        if (knightAwaitingPosition != null) knightAwaitingPosition.heroInPosition -= OnKnightInPosition;
        knightAwaitingPosition = null;
#endif
        if (driverGo) {
            Object.Destroy(driverGo);
            driverGo = null;
        }
    }

    #region leave

    private void InstallLeaveHooks() {
        // Fires at the start of a transition, before the unload. Silksong's GM never fires its own UnloadingLevel
        // (inactive), so after orig we relay the leave handshake and deparent her ourselves.
        Detour(typeof(GameManager), "BeginSceneTransition", OnBeginSceneTransition, typeof(GameManager.SceneLoadInfo));

        // Direct SceneManager.LoadScene[Async] is the path HK FSMs take via PlayMaker's LoadLevel (e.g. Stag), bypassing
        // BeginSceneTransition. LoadSceneParameters is a struct the Detour helper's GetMethod mis-binds on some Unity
        // versions, so resolve the overload manually and Track directly.
        foreach (var m in typeof(USceneManager).GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            var ps = m.GetParameters();
            if (ps.Length != 2 || ps[0].ParameterType != typeof(string) ||
                ps[1].ParameterType != typeof(LoadSceneParameters)) continue;
            if (m.Name == "LoadScene")
                Track(new Hook(m,
                    (Func<Func<string, LoadSceneParameters, Scene>, string, LoadSceneParameters, Scene>)OnLoadScene));
            else if (m.Name == "LoadSceneAsync")
                Track(new Hook(m,
                    (Func<Func<string, LoadSceneParameters, AsyncOperation>, string, LoadSceneParameters,
                        AsyncOperation>)
                    OnLoadSceneAsync));
        }
    }

    private void OnBeginSceneTransition(Action<GameManager, GameManager.SceneLoadInfo> orig, GameManager self,
        GameManager.SceneLoadInfo info) {
        orig(self, info);
        RelayLeaveScene(info);
        ArmDreamArrival(info);
        DeparentHero("scene transition");
    }

    private Scene OnLoadScene(Func<string, LoadSceneParameters, Scene> orig, string name, LoadSceneParameters p) {
        DeparentHero($"LoadScene({name})");
        return orig(name, p);
    }

    private AsyncOperation OnLoadSceneAsync(Func<string, LoadSceneParameters, AsyncOperation> orig, string name,
        LoadSceneParameters p) {
        DeparentHero($"LoadSceneAsync({name})");
        return orig(name, p);
    }

    // HK drives the transition on its Knight (LeaveScene then LeavingScene); Hornet's HeroController gets neither.
    // LeaveScene sets no_input + no-damage + a scripted walk-out (else she runs/falls out the gate with gravity on and
    // takes hazard damage during the fade); RecordLeaveSceneCState captures exitedSprinting/-SuperDashing/-Quake that
    // EnterScene reads to carry the move across. Directional gate exits only; null-gate loads have their own paths.
    private void RelayLeaveScene(GameManager.SceneLoadInfo info) {
        if (!HeroSwitch.HornetActive || !info.HeroLeaveDirection.HasValue) return;
        var hero = HornetSpawner.Hornet;
        if (!hero) return;
        hero.LeaveScene((SGate)(int)info.HeroLeaveDirection.Value);
        hero.RecordLeaveSceneCState();
        LogDebug($"relayed LeaveScene + RecordLeaveSceneCState (exitedSprinting={hero.exitedSprinting})");
    }

    // Mirror Silksong's OnLevelUnload -> SetHeroParent(null) -> DontDestroyOnLoad, but keyed on scene not parent:
    // SetHeroParent(null) skips DDOL when transform.parent is already null even if the GO is still in a scene.
    private void DeparentHero(string reason) {
        var hero = HornetSpawner.Hornet;
        if (!hero || hero.gameObject.scene.name == "DontDestroyOnLoad") return;
        LogDebug($"deparenting hero before {reason} (was in scene={hero.gameObject.scene.name})");
        hero.SetHeroParent(null);
        if (hero.gameObject.scene.name != "DontDestroyOnLoad") Object.DontDestroyOnLoad(hero.gameObject);
    }

    #endregion

    #region enter

    private void InstallEnterHooks() {
        // HK calls SetHazardRespawn from FinishedEnteringScene and from HazardRespawnTrigger (any Layer-9 collider, incl.
        // Hornet). Mirror onto Silksong's PlayerData so her HazardRespawn lands at the right spot.
        Detour(typeof(PlayerData), "SetHazardRespawn", OnSetHazardRespawn, typeof(Vector3), typeof(bool));
        Detour(typeof(PlayerData), "SetHazardRespawn", OnSetHazardRespawnMarker, typeof(HazardRespawnMarker));

        // enterWithoutInput entries rely on an HK arrival FSM to RegainControl/AcceptInput; Hornet has none, leaving her
        // frozen after FinishedEnteringScene, so do the close.
        Detour(typeof(Silksong::HeroController), "FinishedEnteringScene", OnFinishedEnteringScene,
            typeof(bool), typeof(bool));

        // Dream entries warp in via a direct hero_ctrl.EnterSceneDreamGate() on the Knight (typed field call, no shim
        // redirects it), so Hornet's never runs. Flag it; Tick positions her and runs hers.
        Detour(typeof(HeroController), "EnterSceneDreamGate", OnEnterSceneDreamGate);

#if HK1512620
        // Scene FSMs waiting on heroInPosition (WaitForHeroInPosition) read the hero's position right in the handler,
        // before Tick walks Hornet in: Ruins1_31's lift wall keeps its room masked unless she's already left of it.
        Detour(typeof(HeroController), "SendHeroInPosition", OnSendHeroInPosition, typeof(bool));
#else
        // Same for HK 1.5.78, which raises heroInPosition inline.
        Detour(typeof(HeroController), "LeaveScene", OnKnightLeaveScene, typeof(GatePosition?));
#endif

        // Scene FSMs ask the "Hero" var (Hornet) which gate it entered by, e.g. the Hall of Gods statue doors that slide
        // the HUD back in after a boss. HK enters the scene on the Knight; Hornet's own entry gate is stale or unset.
        Detour(typeof(Silksong::HeroController), "GetEntryGateName", OnGetEntryGateName);

        Detour(typeof(GameManager), "LevelActivated", OnLevelActivated, typeof(Scene), typeof(Scene));

        // Arrival i-frames: block damage through the dream-arrival window (see arrivalInvulnerable). Needed because on a
        // cross-scene dream warp she briefly sits at the carried-over position, which in Radiance's arena is inside the
        // Abyss Pit spike; the damage box is HeroBox (a child GO with its own layer) so root-layer tricks don't stop it.
        Detour(typeof(Silksong::HeroController), "TakeDamage", OnTakeDamage,
            typeof(GameObject), typeof(SCollisionSide), typeof(int), typeof(SHazardType), typeof(SDamageFlags));
    }

    private void OnTakeDamage(
        Action<Silksong::HeroController, GameObject, SCollisionSide, int, SHazardType, SDamageFlags> orig,
        Silksong::HeroController self, GameObject go, SCollisionSide side, int dmg, SHazardType hazard,
        SDamageFlags flags) {
        if (arrivalInvulnerable) return;
        orig(self, go, side, dmg, hazard, flags);
    }

    private void OnSetHazardRespawn(Action<PlayerData, Vector3, bool> orig, PlayerData self, Vector3 pos, bool facing) {
        orig(self, pos, facing);
        Silksong::PlayerData.instance.SetHazardRespawn(pos, facing);
    }

    private void OnSetHazardRespawnMarker(Action<PlayerData, HazardRespawnMarker> orig, PlayerData self,
        HazardRespawnMarker marker) {
        orig(self, marker);
        if (marker)
            Silksong::PlayerData.instance.SetHazardRespawn(marker.transform.position, marker.respawnFacingRight);
    }

#if HK1512620
    private void OnSendHeroInPosition(Action<HeroController, bool> orig, HeroController self, bool forceDirect) {
        if (HeroSwitch.HornetActive && !liftArrivalPending) SnapHornetToKnight(self);
        orig(self, forceDirect);
    }
#else
    private HeroController? knightAwaitingPosition;

    private void OnKnightLeaveScene(Action<HeroController, GatePosition?> orig, HeroController self, GatePosition? gate) {
        orig(self, gate);
        self.heroInPosition -= OnKnightInPosition;
        self.heroInPosition += OnKnightInPosition;
        knightAwaitingPosition = self;
    }

    private void OnKnightInPosition(bool forceDirect) {
        var knight = knightAwaitingPosition!;
        knight.heroInPosition -= OnKnightInPosition;
        knightAwaitingPosition = null;
        if (HeroSwitch.HornetActive && !liftArrivalPending) SnapHornetToKnight(knight);
    }
#endif

    private static string OnGetEntryGateName(Func<Silksong::HeroController, string> orig,
        Silksong::HeroController self) {
        var knight = HeroController.UnsafeInstance;
        return HeroSwitch.HornetActive && knight ? knight.GetEntryGateName() : orig(self);
    }

    private void OnEnterSceneDreamGate(Action<HeroController> orig, HeroController self) {
        orig(self);
        if (HeroSwitch.HornetActive) dreamGateEntryPending = true;
    }

    // Per-frame from the driver: the two deferred waits with no clean event, HK placing the Knight (isHeroInPosition) and
    // the inert-Knight bottom-gate entry. Scene-change detection is event-driven (OnActiveSceneChanged -> ArmEntry).
    internal void Tick() {
        if (liftArrivalPending) return; // the lift owns the hero for this arrival

        var knight = HeroController.UnsafeInstance;
        CompleteStuckHkVerticalEntry(knight);
        HoldDuringMaskBreak();
        if (!pendingSnap || !knight || !knight.isHeroInPosition) return;

        if (HeroSwitch.HornetActive && (dreamGateEntryPending || (dreamArrivalPending && !dreamReturnPending))) {
            // Dream arrival: warp her to the Knight's placed position + EnterSceneDreamGate (gravity off, no_input, no
            // door walk-out), HK's dream mechanism. The arrival-layer coroutine held her non-colliding until now;
            // dreamHeroPlaced flips it to restore the Player layer so she collides with the platform and stays.
            SnapHornetToKnight(knight);
            var hornet = HornetSpawner.Hornet;
            if (hornet) {
                // A death in Godhome returns through "door_dreamReturn", which EnterHero swaps for the statue door.
                lieDownArrival = fromMaskBreak ||
                                 knight.GetEntryGateName().StartsWith("door_dreamReturnGG", StringComparison.Ordinal);
                hornet.EnterSceneDreamGate();
                StartCoroutine(lieDownArrival ? PlayLieDownArrival(hornet) : PlayDreamArrival(hornet));
            }

            dreamHeroPlaced = true;
        }
        else if (HeroSwitch.HornetActive && knight.sceneEntryGate) {
            StartCoroutine(dreamReturnPending ? DreamReturnEntry(knight) : RunEntry(knight));
            // A dream return walks in through this gate entry, not the dream-gate branch. End the non-colliding park now:
            // the entry's FinishedEnteringScene re-enables gravity, and on Ignore Raycast she'd fall through the floor.
            dreamHeroPlaced = true;
        }
        else {
            SnapHornetToKnight(knight); // Knight active: Hornet is an inert prop, just relocate her
        }

        pendingSnap = false;
        dreamReturnPending = false;
        dreamGateEntryPending = false;
        dreamArrivalPending = false;
    }

    // The dreamer mask-break cutscene keeps the Knight without input until it transitions back out. Hornet's entry and
    // arrival hand her control on the way in, so hold her every frame instead.
    private static void HoldDuringMaskBreak() {
        var hornet = HornetSpawner.Hornet;
        if (!HeroSwitch.HornetActive || !hornet ||
            hornet is { controlReqlinquished: true, acceptingInput: false }) return;
        if (USceneManager.GetActiveScene().name == "Cutscene_Boss_Door") hornet.RelinquishControl();
    }

    // Arm the entry on a scene change (from = scene we're leaving). The definitive walk-in runs from Tick; the
    // entrance park runs in OnLevelActivated, after HK placed the Knight.
    private void ArmEntry(Scene from) {
        pendingSnap = true;
        // from.name is null when the previous scene was already unloaded (single-mode load, e.g. quit-to-menu); it's
        // only a real dream exit when the departed scene is still loaded and named. Null -> not a dream.
        dreamReturnPending = from.name?.StartsWith("Dream", StringComparison.Ordinal) ?? false;
        fromMaskBreak = from.name == "Cutscene_Boss_Door";
        hkEntryFixed = false;
        cameraCaughtUp = false;
        // Leaving a dream scene leaves a white blanker faded in that HK's Knight-only Dream Return FSM would fade out.
        if (dreamReturnPending) ClearDreamWhiteBlanker();
    }

    // orig moves the knight to scene entry. Move hornet as well, otherwise scene FSMs read her old position.
    // TEST: insta soul master phase 2 in godhome (checks position to start scene 2)
    private void OnLevelActivated(Action<GameManager, Scene, Scene> orig, GameManager self, Scene from, Scene to) {
        orig(self, from, to);
        if (from.name == to.name || liftArrivalPending) return;
        var knight = HeroController.UnsafeInstance;
        if (!knight) return;
        SnapHornetToKnight(knight);
        LogDebug($"set hornet entry to position {knight.transform.position}");
    }

    // Relinquish control and let the "Lift Move" FSM carry the hero: it parents the global "Hero" var to elev_main
    // (Parent Hero -> ... -> Arrive deparents), so running our own gate entry would fight it. Returns false when it's
    // not a lift arrival.
    private bool TryArmLiftArrival() {
        var gm = GameManager.UnsafeInstance;
        if (!HeroSwitch.HornetActive || !gm || gm.playerData == null || !gm.playerData.liftArrival) return false;
        var hero = HornetSpawner.Hornet;
        if (!hero) return false;

        hero.RelinquishControl();
        liftArrivalPending = true;
        StartCoroutine(CompleteLiftArrival());
        LogDebug("lift arrival: relinquished control, letting the lift carry Hornet");
        return true;
    }

    private IEnumerator CompleteLiftArrival() {
        var hero = HornetSpawner.Hornet;
        if (!hero) {
            liftArrivalPending = false;
            yield break;
        }

        var grabbed = false;
        for (var i = 0; i < 600 && hero; i++) {
            var parent = hero.transform.parent;
            if (parent && parent.name == "elev_main") {
                if (!grabbed) {
                    // The lift parented her at her stale position (huge frozen offset) and its own SetPosition(Hero) is
                    // disabled, so seat her ourselves: elev_entrance is a child of elev_main marking the on-platform
                    // stand point, so its localPosition rides her correctly. Read off her actual parent, not a global
                    // Find that would race the departing scene's copy.
                    var entrance = parent.Find("elev_entrance");
                    if (entrance) hero.transform.localPosition = entrance.localPosition;
                    grabbed = true;
                }
            }
            else if (grabbed && !parent) break; // lift finished the ride and released her at the top

            yield return null;
        }

        if (hero) {
            hero.RegainControl();
            hero.AcceptInput();
            var gm = GameManager.UnsafeInstance;
            if (gm && gm.gameState == GameState.ENTERING_LEVEL) gm.SetState(GameState.PLAYING);
        }

        liftArrivalPending = false;
        LogDebug($"lift arrival complete: lift released hero, control returned (grabbed={grabbed})");
    }

    // HK's EnterScene ends by calling gm.FinishedEnteringScene() (ENTERING_LEVEL -> PLAYING). A bottom gate ("up"
    // transition) ends at DROPPING_DOWN and only completes once the Knight physically lands, which the inert Knight
    // (physics off) never does, so gameState sticks at ENTERING_LEVEL and every later gate's TryDoTransition bails. We
    // finish HK's half and settle transitionState (HK camera reads the Knight's as the proxy; a stuck DROPPING_DOWN makes
    // CameraTarget.ExitLockZone drop to FREE). Once per scene.
    private void CompleteStuckHkVerticalEntry(HeroController? knight) {
        if (hkEntryFixed || !HeroSwitch.HornetActive || !knight || knight.enabled) return; // only the inert Knight
        var gm = GameManager.UnsafeInstance;
        if (!gm || gm.gameState != GameState.ENTERING_LEVEL) return;
        if (knight.transitionState != HeroTransitionState.DROPPING_DOWN) return;
        var gate = knight.sceneEntryGate;
        if (!gate || gate.GetGatePosition() != GatePosition.bottom) return;
        gm.FinishedEnteringScene();
        knight.transitionState = HeroTransitionState.WAITING_TO_TRANSITION;
        hkEntryFixed = true;
        LogDebug("completed inert-Knight bottom-gate entry (FinishedEnteringScene + settled WAITING_TO_TRANSITION)");
    }

    // Manage the hero's collision layer across a cross-scene dream arrival. Two windows, both racy in our setup:
    //  1. park (scene loaded, not yet placed): she sits at the carried-over position, which in Radiance's arena is inside
    //     the wide Abyss Pit spike -> keep her on Ignore Raycast so she collides with nothing (like HK's warp).
    //  2. placed (dream-gate branch set dreamHeroPlaced): restore Player so she collides with the arena platform and stays
    //     (HK restores it ~0.7s late, during which she'd otherwise fall through the floor into the pit).
    private static readonly int playerLayer = LayerMask.NameToLayer("Player");
    private static readonly int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

    private IEnumerator ManageArrivalLayer() {
        var hero = HornetSpawner.Hornet;
        if (!hero) yield break;
        // see EnterSceneDreamGate
        hero.AffectedByGravity(false);
        for (var i = 0; i < 120 && !dreamHeroPlaced && hero; i++) {
            // park: non-colliding until placed (cap ~2.4s)
            hero.gameObject.layer = ignoreRaycastLayer;
            yield return new WaitForFixedUpdate();
        }

        if (hero && !dreamHeroPlaced) hero.AffectedByGravity(true);

        for (var i = 0; i < 45 && hero; i++) {
            // placed: hold Player through HK's late restore so there's no gap
            if (hero.gameObject.layer != playerLayer) hero.gameObject.layer = playerLayer;
            yield return new WaitForFixedUpdate();
        }

        arrivalInvulnerable = false;
    }

    private static void SnapHornetToKnight(HeroController knight) {
        var hornet = HornetSpawner.HornetRoot;
        if (!hornet) return;
        hornet.transform.position = knight.transform.position;
        var rb = hornet.GetComponent<Rigidbody2D>();
        if (rb && rb.simulated) rb.velocity = Vector2.zero;
    }

    // Catch the render camera up to the hero on entry. HK's DoPositionToHero should snap it, but races our out-of-band
    // placement.
    // TODO: check if this can be removed somehow. Reproducible in transition out of greenpath stag.
    internal void GlueCameraDuringEntry() {
        if (cameraCaughtUp || !HeroSwitch.HornetActive) return;
        var gm = GameManager.UnsafeInstance;
        if (!gm || gm.gameState != GameState.ENTERING_LEVEL) return;
        var gameCameras = GameCameras.instance;
        var cameraController = gameCameras ? gameCameras.cameraController : null;
        var ct = gameCameras ? gameCameras.cameraTarget : null;
        if (!cameraController || !ct) return;
        var cam = cameraController.transform.position;
        var cty = ct.transform.position.y;
        // Fix only the stale vertical carry-over (the "camera glides in from below"), once per entry. Y-only: HK's
        // lock-zone X clamp legitimately sits far from camTarget.x (e.g. xLockMin at a room edge), so touching X fights
        // the clamp -> oscillation. Latch: once the camera Y is on camTarget, hand everything back to HK's follow.
        if (Mathf.Abs(cam.y - cty) <= 5f) return;
        cameraController.transform.position = new Vector3(cam.x, cty, cam.z);
        cameraCaughtUp = true;
    }

    // For boss scene entry there's a Dream Entry FSM which leaves enterWithoutInput.
    // For the Hall of Gods return, the Knight uses its Dream Return FSM, we have to do it here.
    private void OnFinishedEnteringScene(Action<Silksong::HeroController, bool, bool> orig,
        Silksong::HeroController self,
        bool setHazardMarker, bool preventRunBob) {
        var wasEnterWithoutInput = self.enterWithoutInput;
        var isMoveResume = self.exitedSuperDashing || self.exitedQuake || self.exitedSprinting;
        orig(self, setHazardMarker, preventRunBob);

        // HK's gameState is driven by the HK EnterScene, which finishes after Hornet. During that window she has control
        // but gates are disabled during ENTERING_LEVEL, so dashing back through the transition keeps her out of bounds.
        if (HeroSwitch.HornetActive) {
            var hkGm = GameManager.UnsafeInstance;
            if (hkGm != null && hkGm.gameState == GameState.ENTERING_LEVEL) {
                hkGm.SetState(GameState.PLAYING);
            }
        }

        if (!wasEnterWithoutInput || isMoveResume || !HeroSwitch.HornetActive) return;
        if (BossSceneController.IsBossScene) {
            LogDebug("skipped enterWithoutInput close: boss scene, its own Dream Entry FSM owns control");
            return;
        }

        self.RegainControl();
        self.StartAnimationControl();
        self.AcceptInput();
        // The white blanker stays in its "Fade In" state (opaque, no auto-exit) until a "FADE OUT" event. On a cutscene
        // arrival (e.g. the White Palace half-Kingsoul dream) HK's Knight arrival FSM sends it; Hornet has none, so a
        // no-op here left a permanent whitescreen. Send it ourselves (same call the dream-return path uses). Harmless
        // no-op if the blanker isn't faded in (Idle/Faded Out ignore FADE OUT).
        if (!lieDownArrival) FadeBlankerOut("Blanker White");
        LogDebug("closed enterWithoutInput entry (RegainControl+AcceptInput+FADE OUT white; no HK arrival FSM here)");
    }

    // Run Hornet's real EnterScene from a Silksong TransitionPoint fabricated to mirror HK's gate, so she walks/drops in
    // with her own entry FSMs. Mirrors GameManager.OnNextLevelReady's regain-then-enter (our GM is inactive, never fires):
    // without the RegainControl a door up-interact's earlier RelinquishControl sticks across the transition and gates
    // CanSprint/mantle. SuppressRegainControl honored as OnNextLevelReady does.
    private IEnumerator RunEntry(HeroController knight) {
        var hc = HornetSpawner.Hornet;
        var hkGate = knight.sceneEntryGate;
        if (!hc || !hkGate) yield break;

        if (Silksong::GameManager.SuppressRegainControl) Silksong::GameManager.SuppressRegainControl = false;
        else hc.RegainControl(false);
        hc.StartAnimationControl();

        var gateGo = BuildGate(hkGate);
        entryInProgress = true;
        yield return hc.StartCoroutine(hc.EnterScene(gateGo.GetComponent<Silksong::TransitionPoint>(), 0f));
        entryInProgress = false;
        Object.Destroy(gateGo);
    }

    private bool entryInProgress;

    // Inactive Silksong TransitionPoint mirroring HK's gate: GetGatePosition parses the name for the side, the rest are
    // fields identical in both games. Inactive so its Awake (needs the Silksong scene-setup env) never runs; EnterScene
    // only reads fields + GetGatePosition. customEntryFSM stays null (entry hooks no-op).
    private static GameObject BuildGate(TransitionPoint hk) {
        var go = new GameObject(hk.name);
        go.SetActive(false);
        go.transform.position = hk.transform.position;
        var tp = go.AddComponent<Silksong::TransitionPoint>();
        tp.entryOffset = hk.entryOffset;
        tp.isADoor = hk.isADoor;
        tp.entryDelay = hk.entryDelay;
        tp.alwaysEnterRight = hk.alwaysEnterRight;
        tp.alwaysEnterLeft = hk.alwaysEnterLeft;
        tp.hardLandOnExit = hk.hardLandOnExit;
        tp.nonHazardGate = hk.nonHazardGate;
        tp.customFade = hk.customFade;
        return go;
    }

    // HK's "Dream Entry" FSM hides the hero and plays a Knight sprite ("Knight Dream Arrival") before handing control
    // back, but Hornet's own EnterSceneDreamGate already accepts input, so she'd walk around unseen. Play Silksong's
    // memory wake-up (Prostrate, then Prostrate Rise) on the same beats instead and hold her control until she's up.
    // Hall of Gods statue fights kneel the Knight instead, so she kneels and stands up from that.
    private static readonly string[] dreamEntryKneelStates =
        ["Start Kneeling", "Pause Kneeling", "Wait for Transition"];

    private static readonly string[] dreamEntryFadeStates =
        ["Start Fade", "Arrive Anim", "Wait for Anim", "Return Control"];

    private static readonly string[] dreamEntryArriveStates =
        ["Arrive Anim", "Wait for Anim", "Return from Kneeling", "Return Control"];

    private IEnumerator PlayDreamArrival(Silksong::HeroController hero) {
        var entry = GameObject.Find("Dream Entry");
        var fsm = entry ? Array.Find(entry.GetComponents<PlayMakerFSM>(), f => f.FsmName == "Control") : null;
        var knightSprite = entry ? entry.transform.Find("Knight Dream Arrival") : null;
        if (!fsm || !knightSprite) yield break;

        var knightRenderers = knightSprite.GetComponentsInChildren<MeshRenderer>(true);
        playingDreamArrival = true;
        hero.RelinquishControl();
        try {
            var deadline = Time.unscaledTime + 10f;
            var kneeling = false;
            while (Array.IndexOf(dreamEntryFadeStates, fsm.ActiveStateName) < 0 && Time.unscaledTime < deadline) {
                kneeling = Array.IndexOf(dreamEntryKneelStates, fsm.ActiveStateName) >= 0;
                if (kneeling) break;
                yield return null;
            }

            hero.StopAnimationControl();
            hero.GetComponent<MeshRenderer>().enabled = true;
            hero.AnimCtrl.PlayClipForced(kneeling ? "Abyss Kneel Idle" : "Prostrate");
            while (Array.IndexOf(dreamEntryArriveStates, fsm.ActiveStateName) < 0 && Time.unscaledTime < deadline)
                yield return null;

            hero.AnimCtrl.PlayClipForced(kneeling ? "Abyss Kneel to Stand" : "Prostrate Rise");
            var rise = hero.AnimCtrl.animator.CurrentClip;
            while (rise != null && hero.AnimCtrl.animator.IsPlaying(rise) && Time.unscaledTime < deadline) {
                foreach (var knightRenderer in knightRenderers) knightRenderer.enabled = false;
                yield return null;
            }

            foreach (var knightRenderer in knightRenderers) knightRenderer.enabled = false;
        } finally {
            playingDreamArrival = false;
            if (hero) {
                hero.RegainControl();
                hero.StartAnimationControl();
            }
        }
    }

    // Back at a Hall of Gods statue or from a dreamer's mask-break cutscene, HK's Knight-only Dream Return FSM lays the
    // Knight down and gets him up on the first input, broadcasting DREAM WAKE: the statue door waits for it to finish its
    // transition. Do the same with Hornet. The blankers fade out on the Knight's beat, once the door's own transition
    // already covers the screen. She's laid back down every frame: after a death, DeathModule's revive hands her control
    // and the idle clip right after this starts.
    private IEnumerator PlayLieDownArrival(Silksong::HeroController hero) {
        playingDreamArrival = true;
        try {
            var start = Time.time;
            var faded = false;
            var input = InputHandler.Instance.inputActions;
            while (Time.time < start + 1f || !(input.up.WasPressed || input.down.WasPressed || input.left.WasPressed ||
                                               input.right.WasPressed || input.attack.WasPressed ||
                                               input.jump.WasPressed)) {
                if (!hero.controlReqlinquished || hero.acceptingInput) hero.RelinquishControl();
                if (hero.AnimCtrl.animator.CurrentClip?.name != "Prostrate") {
                    hero.StopAnimationControl();
                    hero.AnimCtrl.PlayClipForced("Prostrate");
                }

                if (!faded && Time.time >= start + 0.5f) {
                    FadeBlankerOut("Blanker White");
                    FadeBlankerOut("Blanker");
                    faded = true;
                }

                yield return null;
            }

            PlayMakerFSM.BroadcastEvent("DREAM WAKE");
            hero.AnimCtrl.PlayClipForced("Prostrate Rise");
            var rise = hero.AnimCtrl.animator.CurrentClip;
            while (rise != null && hero.AnimCtrl.animator.IsPlaying(rise)) yield return null;
        } finally {
            playingDreamArrival = false;
            lieDownArrival = false;
            if (hero) {
                hero.RegainControl();
                hero.StartAnimationControl();
            }
        }
    }

    // Dream-return arrival: normal entry, then force the idle clip once grounded ("door_dreamReturn" is a door path with
    // no real door, leaving her animator on the warp clip; HK's Dream Return get-up would do this).
    private IEnumerator DreamReturnEntry(HeroController knight) {
        yield return RunEntry(knight);
        var hero = HornetSpawner.Hornet;
        if (!hero) yield break;
        for (var i = 0; i < 60 && (hero.cState == null || !hero.cState.onGround); i++) yield return null;
        hero.StartAnimationControlToIdle();
    }

    #endregion

    #region dream white-blanker

    // Dream / GrimmDream / GodsAndGlory entries fade a white blanker in and rely on the Knight-only Dream Return FSM to
    // fade it out + return control on arrival. Hornet lacks it; arm the fade-out (and capture the gate for a same-scene
    // re-entry HeroSwitch's name-change snap misses).
    private void ArmDreamArrival(GameManager.SceneLoadInfo info) {
        if (!HeroSwitch.HornetActive) return;
        var vis = info.Visualization;
        if (vis != GameManager.SceneLoadVisualizations.Dream &&
            vis != GameManager.SceneLoadVisualizations.GrimmDream &&
            vis != GameManager.SceneLoadVisualizations.GodsAndGlory) return;
        dreamPending = true;
        dreamArrivalPending = true;
        dreamGate = info.EntryGateName;
    }

    // Silksong's SetupGameRefs subscribes the pooled-object recyclers to NextSceneWillActivate; our GM is inactive and
    // never raises it, so thrown tools / one-shot audio linger across HK transitions (a tool ticking against a destroyed
    // camera -> per-frame NullRef). Mirror that single firing here (one per real room change).
    // Subscriber on the shared SceneManager.activeSceneChanged multicast: must never throw, or it aborts the remaining
    // subscribers, including HK's own LevelActivated/scene-setup that populates e.g. the menu. Guard the whole body.
    private void OnActiveSceneChanged(Scene from, Scene to) {
        try {
            RecycleSilksongPooledObjects();
            if (TryArmLiftArrival()) return; // the lift owns the hero this arrival; skip our gate entry entirely

            ArmEntry(from);
            if (!dreamPending) return;
            dreamPending = false;
            // Keep her non-colliding from now until the dream-gate placement (below): on scene load she sits at the
            // carried-over position, which in Radiance's arena is inside the Abyss Pit spike.
            dreamHeroPlaced = false;
            arrivalInvulnerable = true;
            StartCoroutine(ManageArrivalLayer());
            // Same-scene re-entry (Dream Fall Catcher caught a fall): place her at the gate now, before the Fall
            // Catcher's per-frame Hero-Y<0 test re-fires into a respawn loop.
            if (from.name == to.name) PlaceHornetAtGate();
            StartCoroutine(FadeInAfterSettle());
        } catch (Exception e) {
            LogError($"OnActiveSceneChanged: {e}");
        }
    }

    private void RecycleSilksongPooledObjects() {
        try {
            Silksong::AutoRecycleSelf.RecycleActiveRecyclers();
            Silksong::PlayAudioAndRecycle.RecycleActiveRecyclers();
            Silksong::ResetDynamicHierarchy.ForceReconnectAll();
        } catch (Exception e) {
            LogError($"RecycleActiveRecyclers: {e.Message}");
        }
    }

    private void PlaceHornetAtGate() {
        var hornet = HornetSpawner.HornetRoot;
        if (!hornet || string.IsNullOrEmpty(dreamGate)) return;
        var gate = GameObject.Find(dreamGate);
        if (!gate) {
            LogDebug($"re-entry gate '{dreamGate}' not found in scene");
            return;
        }

        var pos = gate.transform.position;
        var tp = gate.GetComponent<TransitionPoint>();
        if (tp) pos += (Vector3)tp.entryOffset;
        hornet.transform.position = pos;
        var rb = hornet.GetComponent<Rigidbody2D>();
        if (rb && rb.simulated) rb.velocity = Vector2.zero;
        LogDebug($"same-scene re-entry: placed Hornet at gate '{dreamGate}' {pos}");
    }

    private IEnumerator FadeInAfterSettle() {
        yield return new WaitForSeconds(0.5f); // let the new scene's blanker FSMs (re)init before we send the event
        if (!lieDownArrival) {
            FadeBlankerOut("Blanker White");
            FadeBlankerOut("Blanker");
        }

        CompleteArrival();
    }

    // The cutscene RelinquishControl'd + StopAnimationControl'd her and enters via "door_dreamReturn" (door path, no door)
    // -> stuck in WAITING_TO_ENTER_LEVEL with a frozen animator. Apply HK's Dream Return get-up: finish the entry, return
    // control, resume idle, broadcast DREAM WAKE (dream-scene directors wait on it, else she's on the bare dais and the
    // Fall Catcher loops her).
    private void CompleteArrival() {
        var hero = HornetSpawner.Hornet;
        if (!hero) return;
        // With a real gate, RunEntry (EnterScene -> FinishedEnteringScene) owns the control-return; doing it here mid-entry
        // breaks its no_input door walk (she gets input+gravity and falls out of the arena, non-deterministically).
        // Boss scene: control return is owned by its own Dream Entry FSM; only the blanker fade + DREAM WAKE run.
        // PlayDreamArrival returns control itself once she's up.
        if (!entryInProgress && !playingDreamArrival && !BossSceneController.IsBossScene) {
            if (hero.transitionState == SHeroTransition.WAITING_TO_ENTER_LEVEL)
                hero.transitionState = SHeroTransition.WAITING_TO_TRANSITION;
            hero.enterWithoutInput = false;
            hero.RegainControl();
            hero.AcceptInput();
            try {
                hero.StartAnimationControl();
            } catch (Exception e) {
                LogError($"StartAnimationControl threw: {e.Message}");
            }
        }

        PlayMakerFSM.BroadcastEvent("DREAM WAKE");
        LogDebug($"completed dream arrival + DREAM WAKE (transitionState={hero.transitionState})");
    }

    private void ClearDreamWhiteBlanker() {
        var blanker = FindBlanker("Blanker White");
        if (blanker) blanker.SetActive(false);
    }

    // Send "FADE OUT" to every PlayMakerFSM on the blanker GO (PlayMaker ignores events a state doesn't handle, so we
    // needn't disambiguate the two FSMs on "Blanker White").
    private static void FadeBlankerOut(string childName) {
        var go = FindBlanker(childName);
        if (!go) return;
        foreach (var fsm in go.GetComponents<PlayMakerFSM>()) fsm.SendEvent("FADE OUT");
    }

    internal static GameObject? FindBlanker(string childName) {
        var gc = GameCameras.instance;
        if (gc && gc.hudCamera) {
            var t = gc.hudCamera.transform.Find(childName);
            if (t) return t.gameObject;
        }

        return GameObject.Find($"_GameCameras/HudCamera/{childName}");
    }

    #endregion
}

// Thin per-frame ticker for SceneTransitionModule: own MonoBehaviour (not ModuleBase.HornetActiveUpdate) so it runs
// Knight-active too. Early order so a scene-change snap lands before HeroController/CameraTarget (order 0) read position.
[DefaultExecutionOrder(-8001)]
internal sealed class SceneTransitionDriver : MonoBehaviour {
    internal SceneTransitionModule Module = null!;

    private void Update() {
        Module.Tick();
    }
}

// after HK's CameraController.LateUpdate at default 0
[DefaultExecutionOrder(10000)]
internal sealed class SceneEntryCameraGlue : MonoBehaviour {
    internal SceneTransitionModule Module = null!;

    private void LateUpdate() {
        Module.GlueCameraDuringEntry();
    }
}
