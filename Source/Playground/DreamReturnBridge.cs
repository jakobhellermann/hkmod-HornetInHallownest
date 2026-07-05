extern alias Silksong;
using System.Collections;
using HutongGames.PlayMaker; // HK's shared PlayMaker — the blankers live on HK's GameCameras
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using Object = UnityEngine.Object;
using HeroTransitionState = Silksong::GlobalEnums.HeroTransitionState;

namespace HornetPlayer.Playground;

// The dreamer "get Dream Nail" cutscene (and any HK Dream-visualization transition) fades a WHITE blanker IN during the
// cutscene, then relies on HK's Knight "Dream Return" FSM (state Prostrate) to fade it back OUT on arrival in the dream
// scene (SendEvent "FADE OUT" -> "HUD Blanker White" + "HUD Blanker") and return control. Hornet has NO such FSM — the
// cutscene's SetFsmBool(gameObject="Hero", fsmName="Dream Return", ...) fails ("Could not find FSM: Dream Return") — so
// the white never lifts => whitescreen (root-caused via HkFsmTracer: Control@Dreamer Scene 2 completes and transitions,
// but no one fades the white on the far side).
//
// This bridge replicates the ONE necessary piece of Prostrate for Hornet: on a Dream transition (while she's active),
// fade the HK blanker(s) out on arrival. Control is already restored by HornetEnvironmentAdapter.OnFinishedEnteringScene
// (the enterWithoutInput close). We deliberately skip the prostrate/rise cinematic — Hornet lacks those clips, and this
// is the incremental "only what's necessary" path.
internal static class DreamReturnBridge {
    private static bool pending;
    private static bool subscribed;
    private static string? pendingGate;

    // Called from HornetEnvironmentAdapter's BeginSceneTransition hook (after orig). Dream visualization is exactly the
    // set of entries that fade a blanker in and expect the Dream Return FSM to fade it out. We also capture the entry
    // gate NAME from the SceneLoadInfo — the same value HK resolves the hero's arrival position from — so the same-scene
    // re-entry can place Hornet at the gate itself, without going through HK's Knight.
    internal static void OnBeginSceneTransition(GameManager.SceneLoadInfo info) {
        if (!HeroSwitch.HornetActive) return;
        if (info.Visualization != GameManager.SceneLoadVisualizations.Dream) return;
        pending = true;
        pendingGate = info.EntryGateName;
        Subscribe();
        Log.Info($"[DreamReturn] Dream transition -> '{info.SceneName}' (gate '{pendingGate}'); " +
                 "will fade blanker(s) out on arrival");
    }

    private static void Subscribe() {
        if (subscribed) return;
        USceneManager.activeSceneChanged += OnActiveSceneChanged;
        subscribed = true;
    }

    private static void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from,
        UnityEngine.SceneManagement.Scene to) {
        if (!pending) return;
        pending = false;

        // Same-scene re-entry = the Dream Fall Catcher caught a fall and re-transitioned into THE SAME dream scene
        // (BeginSceneTransition(dreamReturnScene, Dream)). HeroSwitch's snap-to-entry only fires on a scene-NAME change,
        // so on a same-scene reload Hornet keeps her fall coords (Y < 0) — and the Fall Catcher's Detect state
        // (`Hero Y < 0 -> FALL`, every frame) re-fires the instant it starts => respawn loop. Place her at the entry
        // gate NOW (before the Fall Catcher's Detect runs) to put her back on solid ground. (First arrival comes from a
        // different scene and is positioned by HeroSwitch.)
        if (from.name == to.name) PlaceHornetAtGate();

        var host = Object.FindAnyObjectByType<PlaygroundHost>();
        if (host != null) host.StartCoroutine(FadeInAfterSettle());
    }

    // Position Hornet at the arrival gate directly (no Knight involved): HK resolves the arrival by finding the gate GO by
    // name and reading its transform + entryOffset (HeroController.EnterScene) — we do the same with the gate name we
    // captured from the SceneLoadInfo. Deterministic, level-authored, and the gate GO is already loaded at this point.
    private static void PlaceHornetAtGate() {
        var hornet = BundleSpike.HornetRoot;
        if (hornet == null || string.IsNullOrEmpty(pendingGate)) return;
        var gate = GameObject.Find(pendingGate);
        if (gate == null) {
            Log.Info($"[DreamReturn] re-entry gate '{pendingGate}' not found in scene");
            return;
        }

        var pos = gate.transform.position;
        var tp = gate.GetComponent<TransitionPoint>();
        if (tp != null) pos += (Vector3)tp.entryOffset;
        hornet.transform.position = pos;
        var rb = hornet.GetComponent<Rigidbody2D>();
        if (rb != null && rb.simulated) rb.linearVelocity = Vector2.zero;
        Log.Info($"[DreamReturn] same-scene re-entry: placed Hornet at gate '{pendingGate}' {pos}");
    }

    private static IEnumerator FadeInAfterSettle() {
        yield return new WaitForSeconds(0.5f); // let the new scene's blanker FSMs (re)init before we send the event
        var white = FadeBlankerOut("Blanker White");
        var black = FadeBlankerOut("Blanker");
        Log.Info($"[DreamReturn] arrival: faded blankers out (white={white}, black={black})");
        CompleteArrival();
    }

    // The cutscene RelinquishControl'd + StopAnimationControl'd Hornet, and HK enters the dream scene through the
    // "door_dreamReturn" gate — a door-entry path with no real door, which can strand her in WAITING_TO_ENTER_LEVEL with
    // a frozen animator (stuck on the airborne/warp clip). HK's Knight "Dream Return" FSM (states Regain Control / Get Up)
    // would finish the entry + return control + resume the idle animation; Hornet has no such FSM, so we apply that exact
    // effect here. Deterministic (runs off activeSceneChanged, not the inert Knight's isHeroInPosition gate).
    private static void CompleteArrival() {
        var hero = BundleSpike.RealHero;
        if (hero == null) return;
        Log.Info($"[DreamReturn] pre-complete: transitionState={hero.transitionState}, " +
                 $"controlReq={hero.controlReqlinquished}, enterWithoutInput={hero.enterWithoutInput}");
        if (hero.transitionState == HeroTransitionState.WAITING_TO_ENTER_LEVEL)
            hero.transitionState = HeroTransitionState.WAITING_TO_TRANSITION; // the normal resting gameplay state
        hero.enterWithoutInput = false;
        hero.RegainControl();
        hero.AcceptInput();
        // Reverse the cutscene's StopAnimationControl (frozen frame). HK's Dream Return "Get Up"/"Regain Control" states
        // do exactly StartAnimationControl. Guarded: animCtrl.StartControlToIdle can NullRef out of a normal entry
        // context, and control must stay restored even if the anim resume throws.
        try {
            hero.StartAnimationControl();
        } catch (System.Exception e) {
            Log.Error($"[DreamReturn] StartAnimationControl threw: {e.Message}");
        }

        // HK's Dream Return "Get Up" broadcasts "DREAM WAKE" — the event dream SCENE directors wait on (e.g.
        // Dream_Nailcollection's "Witch Control" master FSM sits in Idle `on DREAM WAKE → First Pause`, which flies the
        // Seer in and activates the hidden First Platforms; without it she's on the bare entry dais, walks off, and the
        // Dream Fall Catcher re-transitions her back = a respawn loop). Hornet's arrival skips Get Up, so broadcast it.
        PlayMakerFSM.BroadcastEvent("DREAM WAKE");
        Log.Info($"[DreamReturn] completed arrival + DREAM WAKE: transitionState={hero.transitionState}, " +
                 $"controlReq={hero.controlReqlinquished}");
    }

    // Find an HK HudCamera blanker child by name and send its "Blanker Control" FSM "FADE OUT" (the same event HK's
    // Prostrate sends). Sending to every PlayMakerFSM on the GO is harmless — PlayMaker ignores events a state doesn't
    // handle — so we don't have to disambiguate the two FSMs on "Blanker White".
    private static bool FadeBlankerOut(string childName) {
        var go = FindBlanker(childName);
        if (go == null) return false;
        var sent = false;
        foreach (var fsm in go.GetComponents<PlayMakerFSM>()) {
            fsm.SendEvent("FADE OUT");
            sent = true;
        }

        return sent;
    }

    private static GameObject? FindBlanker(string childName) {
        var gc = GameCameras.instance;
        if (gc != null && gc.hudCamera != null) {
            var t = gc.hudCamera.transform.Find(childName);
            if (t != null) return t.gameObject;
        }

        // Fallback: the blanker sits under _GameCameras/HudCamera in the scene tree.
        return GameObject.Find($"_GameCameras/HudCamera/{childName}");
    }

    internal static void Cleanup() {
        if (subscribed) USceneManager.activeSceneChanged -= OnActiveSceneChanged;
        subscribed = false;
        pending = false;
    }
}
