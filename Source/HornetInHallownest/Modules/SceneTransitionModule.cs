extern alias Silksong;
using System;
using System.Collections;
using System.Reflection;
using GlobalEnums;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
// A bare `SceneManager` binds to HK's Assembly-CSharp SceneManager (global namespace wins over the using); alias Unity's.
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using SGate = Silksong::GlobalEnums.GatePosition;
using SHeroTransition = Silksong::GlobalEnums.HeroTransitionState;
using SCollisionSide = Silksong::GlobalEnums.CollisionSide;
using SHazardType = Silksong::GlobalEnums.HazardType;
using SDamageFlags = Silksong::GlobalEnums.DamagePropertyFlags;

namespace HornetPlayer.HornetInHallownest.Modules;

// Move hornet in HK-driven scene transition.
// Defer to real silksong LeaveScene / EnterScene / FinishedEnteringScene.
public sealed class SceneTransitionModule : ModuleBase {
    private GameObject? driverGo;

    private bool pendingSnap, dreamReturnPending, hkEntryFixed, dreamGateEntryPending;

    // A Dream-visualization arrival is in flight (set at BeginSceneTransition, consumed by Tick). HK warps dream entries
    // in via EnterSceneDreamGate (gravity off, no door walk-out); route Hornet the same way even when HK drove the Knight
    // through a normal gate instead — RunEntry's door walk-out otherwise walks/falls her off the arena platform.
    private bool dreamArrivalPending;
    private bool dreamHeroPlaced;
    private bool arrivalInvulnerable; // block hazard damage through the dream-arrival window (park + placement settle)
    private bool dreamPending;
    private string? dreamGate;

    public override string Id => "scene-transition";

    public override void Initialize() {
        InstallLeaveHooks();
        InstallEnterHooks();
        USceneManager.activeSceneChanged += OnActiveSceneChanged;

        driverGo = new GameObject("HornetPlayer.SceneTransitionDriver");
        driverGo.AddComponent<SceneTransitionDriver>().Module = this;
        Object.DontDestroyOnLoad(driverGo);
    }

    protected override void OnDeinitialize() {
        USceneManager.activeSceneChanged -= OnActiveSceneChanged;
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
                    (Func<Func<string, LoadSceneParameters, AsyncOperation>, string, LoadSceneParameters, AsyncOperation>)
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
    // LeaveScene sets no_input + NO_DAMAGE + a scripted walk-out (else she runs/falls out the gate with gravity on and can
    // take hazard damage during the fade); RecordLeaveSceneCState captures exitedSprinting/-SuperDashing/-Quake that
    // EnterScene reads to carry the move across. Directional gate exits only — null-gate loads have their own paths.
    private void RelayLeaveScene(GameManager.SceneLoadInfo info) {
        if (!HeroSwitch.HornetActive || !info.HeroLeaveDirection.HasValue) return;
        var hero = HornetSpawner.RealHero;
        if (!hero) return;
        hero.LeaveScene((SGate)(int)info.HeroLeaveDirection.Value);
        hero.RecordLeaveSceneCState();
        LogDebug($"relayed LeaveScene + RecordLeaveSceneCState (exitedSprinting={hero.exitedSprinting})");
    }

    // Mirror Silksong's OnLevelUnload -> SetHeroParent(null) -> DontDestroyOnLoad, but keyed on scene not parent:
    // SetHeroParent(null) skips DDOL when transform.parent is already null even if the GO is still in a scene.
    private void DeparentHero(string reason) {
        var hero = HornetSpawner.RealHero;
        if (!hero || hero.gameObject.scene.name == "DontDestroyOnLoad") return;
        LogDebug($"deparenting hero before {reason} (was in scene={hero.gameObject.scene.name})");
        hero.SetHeroParent(null);
        if (hero.gameObject.scene.name != "DontDestroyOnLoad") Object.DontDestroyOnLoad(hero.gameObject);
    }

    #endregion

    #region enter

    private void InstallEnterHooks() {
        // HK calls SetHazardRespawn from FinishedEnteringScene AND from HazardRespawnTrigger (any Layer-9 collider, incl.
        // Hornet). Mirror onto Silksong's PlayerData so her HazardRespawn lands at the right spot.
        Detour(typeof(PlayerData), "SetHazardRespawn", OnSetHazardRespawn, typeof(Vector3), typeof(bool));
        Detour(typeof(PlayerData), "SetHazardRespawn", OnSetHazardRespawnMarker, typeof(HazardRespawnMarker));

        // enterWithoutInput entries rely on an HK arrival FSM to RegainControl/AcceptInput; Hornet has none, so
        // FinishedEnteringScene leaves her frozen — do the close.
        Detour(typeof(Silksong::HeroController), "FinishedEnteringScene", OnFinishedEnteringScene,
            typeof(bool), typeof(bool));

        // Dream entries warp in via a DIRECT hero_ctrl.EnterSceneDreamGate() on the Knight (typed field call, no shim
        // redirects it), so Hornet's never runs. Flag it; Tick positions her and runs hers.
        Detour(typeof(HeroController), "EnterSceneDreamGate", OnEnterSceneDreamGate);

        // Arrival i-frames: block damage through the dream-arrival window (see arrivalInvulnerable). Needed because on a
        // cross-scene dream warp she briefly sits at the carried-over position, which in Radiance's arena is inside the
        // Abyss Pit spike; the damage box is HeroBox (a child GO with its own layer) so root-layer tricks don't stop it.
        Detour(typeof(Silksong::HeroController), "TakeDamage", OnTakeDamage,
            typeof(GameObject), typeof(SCollisionSide), typeof(int), typeof(SHazardType), typeof(SDamageFlags));
    }

    private void OnTakeDamage(
        Action<Silksong::HeroController, GameObject, SCollisionSide, int, SHazardType, SDamageFlags> orig,
        Silksong::HeroController self, GameObject go, SCollisionSide side, int dmg, SHazardType hazard, SDamageFlags flags) {
        if (arrivalInvulnerable) return;
        orig(self, go!, side, dmg, hazard, flags);
    }

    private void OnSetHazardRespawn(Action<PlayerData, Vector3, bool> orig, PlayerData self, Vector3 pos, bool facing) {
        orig(self, pos, facing);
        var pd = Silksong::PlayerData.instance;
        if (pd != null) pd.SetHazardRespawn(pos, facing);
    }

    private void OnSetHazardRespawnMarker(Action<PlayerData, HazardRespawnMarker> orig, PlayerData self,
        HazardRespawnMarker marker) {
        orig(self, marker);
        var pd = Silksong::PlayerData.instance;
        if (pd != null && marker) pd.SetHazardRespawn(marker.transform.position, marker.respawnFacingRight);
    }

    private void OnEnterSceneDreamGate(Action<HeroController> orig, HeroController self) {
        orig(self);
        if (HeroSwitch.HornetActive) dreamGateEntryPending = true;
    }

    // Per-frame from the driver: the two deferred waits with no clean event — HK placing the Knight (isHeroInPosition) and
    // the inert-Knight bottom-gate entry. Scene-change detection is event-driven (OnActiveSceneChanged -> ArmEntry).
    internal void Tick() {
        var knight = HeroController.UnsafeInstance;
        CompleteStuckHkVerticalEntry(knight);
        if (!pendingSnap || !knight || !knight.isHeroInPosition) return;

        if (HeroSwitch.HornetActive && (dreamGateEntryPending || (dreamArrivalPending && !dreamReturnPending))) {
            // Dream arrival: warp her to the Knight's placed position + EnterSceneDreamGate (gravity off, no_input, no door
            // walk-out) — HK's dream mechanism. The arrival-layer coroutine (started at scene change) held her non-colliding
            // until now; dreamHeroPlaced flips it to restore the Player layer so she collides with the platform and stays.
            SnapHornetToKnight(knight);
            HornetSpawner.RealHero?.EnterSceneDreamGate();
            dreamHeroPlaced = true;
        }
        else if (HeroSwitch.HornetActive && knight.sceneEntryGate) {
            StartCoroutine(dreamReturnPending ? DreamReturnEntry(knight) : RunEntry(knight));
        }
        else {
            SnapHornetToKnight(knight); // Knight active: Hornet is an inert prop, just relocate her
        }

        pendingSnap = false;
        dreamReturnPending = false;
        dreamGateEntryPending = false;
        dreamArrivalPending = false;
    }

    // Arm the entry on a scene change (from = scene we're leaving). Pre-place her at the gate now — HK moved the Knight
    // there but Hornet still holds old coords, and enemy FSMs sampling the entry position fire before Tick's
    // isHeroInPosition walk-in moves her. The definitive walk-in runs from Tick.
    private void ArmEntry(Scene from) {
        pendingSnap = true;
        // from.name is null when the previous scene was already unloaded (single-mode load, e.g. quit-to-menu); it's
        // only a real dream exit when the departed scene is still loaded and named. Null -> not a dream.
        dreamReturnPending = from.name?.StartsWith("Dream", StringComparison.Ordinal) ?? false;
        hkEntryFixed = false;
        // Leaving a dream scene leaves a white blanker faded in that HK's Knight-only Dream Return FSM would fade out.
        if (dreamReturnPending) ClearDreamWhiteBlanker();
        var knight = HeroController.UnsafeInstance;
        if (knight) SnapHornetToKnight(knight);
    }

    // HK's EnterScene ends by calling gm.FinishedEnteringScene() (ENTERING_LEVEL -> PLAYING). A bottom gate ("up"
    // transition) ends at DROPPING_DOWN and only completes once the Knight physically LANDS — which the inert Knight
    // (physics off) never does, so gameState sticks at ENTERING_LEVEL and every later gate's TryDoTransition bails. Having
    // taken the hero role, we finish HK's half and settle transitionState (HK camera reads the Knight's as the proxy; a
    // stuck DROPPING_DOWN makes CameraTarget.ExitLockZone drop to FREE). Once per scene.
    private void CompleteStuckHkVerticalEntry(HeroController? knight) {
        if (hkEntryFixed || !HeroSwitch.HornetActive || !knight || knight.enabled) return; // only the INERT Knight
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
    //  1. PARK (scene loaded, not yet placed): she sits at the carried-over position, which in Radiance's arena is inside
    //     the wide Abyss Pit spike -> keep her on Ignore Raycast so she collides with nothing (like HK's warp).
    //  2. PLACED (dream-gate branch set dreamHeroPlaced): restore Player so she collides with the arena platform and stays
    //     (HK restores it ~0.7s late, during which she'd otherwise fall through the floor into the pit).
    private static readonly int PlayerLayer = LayerMask.NameToLayer("Player");
    private static readonly int IgnoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

    private System.Collections.IEnumerator ManageArrivalLayer() {
        var hero = HornetSpawner.RealHero;
        if (!hero) yield break;
        for (var i = 0; i < 120 && !dreamHeroPlaced && hero; i++) { // park: non-colliding until placed (cap ~2.4s)
            hero.gameObject.layer = IgnoreRaycastLayer;
            yield return new WaitForFixedUpdate();
        }

        for (var i = 0; i < 45 && hero; i++) { // placed: hold Player through HK's late restore so there's no gap
            if (hero.gameObject.layer != PlayerLayer) hero.gameObject.layer = PlayerLayer;
            yield return new WaitForFixedUpdate();
        }

        arrivalInvulnerable = false;
    }

    private static void SnapHornetToKnight(HeroController knight) {
        var hornet = HornetSpawner.HornetRoot;
        if (!hornet) return;
        hornet.transform.position = knight.transform.position;
        var rb = hornet.GetComponent<Rigidbody2D>();
        if (rb && rb.simulated) rb.linearVelocity = Vector2.zero;
    }

    // enterWithoutInput entries deliberately skip AcceptInput (expecting an HK arrival FSM to close them); Hornet has none.
    // Do just the Regain-Control close, at the skip point. Skip move-resumes (dash/sprint/quake) — they resume on their own.
    private void OnFinishedEnteringScene(Action<Silksong::HeroController, bool, bool> orig, Silksong::HeroController self,
        bool setHazardMarker, bool preventRunBob) {
        var wasEnterWithoutInput = self.enterWithoutInput;
        var isMoveResume = self.exitedSuperDashing || self.exitedQuake || self.exitedSprinting;
        orig(self, setHazardMarker, preventRunBob);

        // HK's gameState ist driven by the HK EnterScene, which finishes after Hornet.
        // During that window she has control but gates are disabled during ENTERING_LEVEL, so dashing back through the transition
        // keeps her in the scene out of bounds.
        if (HeroSwitch.HornetActive) {
            var hkGm = GameManager.UnsafeInstance;
            if (hkGm != null && hkGm.gameState == GameState.ENTERING_LEVEL) {
                hkGm.SetState(GameState.PLAYING);
            }
        }

        if (!wasEnterWithoutInput || isMoveResume || !HeroSwitch.HornetActive) return;
        self.RegainControl();
        self.StartAnimationControl();
        self.AcceptInput();
        // The white blanker stays in its "Fade In" state (opaque, no auto-exit) until a "FADE OUT" event. On a cutscene
        // arrival (e.g. the White Palace half-Kingsoul dream) HK's Knight arrival FSM sends it; Hornet has none, so a
        // no-op here left a permanent whitescreen. Send it ourselves (same call the dream-return path uses). Harmless
        // no-op if the blanker isn't faded in (Idle/Faded Out ignore FADE OUT).
        FadeBlankerOut("Blanker White");
        LogDebug("closed enterWithoutInput entry (RegainControl+AcceptInput+FADE OUT white; Hornet has no arrival FSM)");
    }

    // Run Hornet's REAL EnterScene from a Silksong TransitionPoint fabricated to mirror HK's gate, so she walks/drops in
    // with her own entry FSMs. Mirrors GameManager.OnNextLevelReady's regain-then-enter (our GM is inactive, never fires):
    // without the RegainControl a door up-interact's earlier RelinquishControl sticks across the transition and gates
    // CanSprint/mantle. SuppressRegainControl honored as OnNextLevelReady does.
    private IEnumerator RunEntry(HeroController knight) {
        var hc = HornetSpawner.RealHero;
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

    internal bool entryInProgress;

    // Inactive Silksong TransitionPoint mirroring HK's gate: GetGatePosition parses the NAME for the side, the rest are
    // fields identical in both games. Inactive so its Awake (needs the Silksong scene-setup env) never runs — EnterScene
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

    // Dream-return arrival: normal entry, then force the idle clip once grounded — "door_dreamReturn" is a door path with
    // no real door, leaving her animator on the warp clip. HK's Dream Return get-up would do this.
    private IEnumerator DreamReturnEntry(HeroController knight) {
        yield return RunEntry(knight);
        var hero = HornetSpawner.RealHero;
        if (!hero) yield break;
        for (var i = 0; i < 60 && (hero.cState == null || !hero.cState.onGround); i++) yield return null;
        hero.StartAnimationControlToIdle();
    }

    #endregion

    #region dream white-blanker

    // Dream / GrimmDream / GodsAndGlory entries fade a white blanker IN and rely on the Knight-only Dream Return FSM to
    // fade it OUT + return control on arrival. Hornet lacks it; arm the fade-out (and capture the gate for a same-scene
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
    // Subscriber on the shared SceneManager.activeSceneChanged multicast: must NEVER throw, or it aborts the remaining
    // subscribers — including HK's own LevelActivated/scene-setup that populates e.g. the menu. Guard the whole body.
    private void OnActiveSceneChanged(Scene from, Scene to) {
        try {
            RecycleSilksongPooledObjects();
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
        if (rb && rb.simulated) rb.linearVelocity = Vector2.zero;
        LogDebug($"same-scene re-entry: placed Hornet at gate '{dreamGate}' {pos}");
    }

    private IEnumerator FadeInAfterSettle() {
        yield return new WaitForSeconds(0.5f); // let the new scene's blanker FSMs (re)init before we send the event
        FadeBlankerOut("Blanker White");
        FadeBlankerOut("Blanker");
        CompleteArrival();
    }

    // The cutscene RelinquishControl'd + StopAnimationControl'd her and enters via "door_dreamReturn" (door path, no door)
    // -> stuck in WAITING_TO_ENTER_LEVEL with a frozen animator. Apply HK's Dream Return get-up: finish the entry, return
    // control, resume idle, broadcast DREAM WAKE (dream-scene directors wait on it — else she's on the bare dais and the
    // Fall Catcher loops her).
    private void CompleteArrival() {
        var hero = HornetSpawner.RealHero;
        if (!hero) return;
        // With a real gate, RunEntry (EnterScene -> FinishedEnteringScene) owns the control-return; doing it here mid-entry
        // breaks its no_input door walk (she gets input+gravity and falls out of the arena, non-deterministically).
        if (!entryInProgress) {
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

    private static GameObject? FindBlanker(string childName) {
        var gc = GameCameras.instance;
        if (gc && gc.hudCamera) {
            var t = gc.hudCamera.transform.Find(childName);
            if (t) return t.gameObject;
        }

        return GameObject.Find($"_GameCameras/HudCamera/{childName}");
    }

    #endregion
}

// Thin per-frame ticker for SceneTransitionModule — own MonoBehaviour (not ModuleBase.HornetActiveUpdate) so it runs
// Knight-active too. Order -8001: one step before HeroSwitch's CameraSwitchDriver (-8000) so a scene-change snap lands
// before that driver retargets the camera the same frame.
[DefaultExecutionOrder(-8001)]
internal sealed class SceneTransitionDriver : MonoBehaviour {
    internal SceneTransitionModule Module = null!;

    private void Update() {
        Module.Tick();
    }
}
