extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Hornet's down-dash breaks HK "Quake Floor"s (which in vanilla only the Knight's Desolate Dive breaks — Hornet has no
// equivalent high-vertical-impact move, and requiring an equipped skill would be annoying).
//
// How HK quake floors break: each idles in its `quake_floor` FSM "Solid" state; a broadcast "QUAKE FALL START" flips it
// to "Transient" (box collider -> trigger, listening OnTriggerEnter2D for tag "Player"), and "QUAKE FALL END" flips it
// back. HK's "Spell Control" broadcasts these around the dive; the diving Knight then falls through the transient floor
// and its trigger fires the real break (debris/audio/PersistentBool).
//
// Event-driven edges + a minimal per-frame break, no global tag:
//   - Hook HeroDashPressed (once per dash start): if it's a down-dash, broadcast "QUAKE FALL START" so floors turn
//     transient, and cache the quake_floor FSMs + their colliders (one scan).
//   - While quaking, Tick() (per-frame from HornetEnvironmentAdapter, but only iterates the small cached list) sends
//     "DESTROY" directly to any cached floor Hornet's body collider overlaps. We do this instead of relying on the
//     floor's own OnTriggerEnter2D(collideTag=Player) because Hornet isn't "Player"-tagged (Silksong's hero prefab
//     isn't tagged; tagging her globally has too broad a blast radius — see the reverted attempt). DESTROY is a valid
//     transition from "Transient", so it runs HK's real break.
//   - Hook FinishedDashing (once per dash end): broadcast "QUAKE FALL END" and clear the cache.
//
// TODO: gate on the dive being unlocked (HK PlayerData.quakeLevel >= 1, i.e. after Soul Master) — deferred.
internal static class QuakeFloorBridge {
    private static Hook? dashHook;
    private static Hook? finishHook;
    private static Hook? getStateHook;
    private static bool quaking;
    private static readonly List<PlayMakerFSM> floors = new();
    private static readonly List<Collider2D> floorCols = new();

    internal static void Install() {
        if (dashHook != null) return;

        var dash = typeof(Silksong::HeroController)
            .GetMethod("HeroDashPressed", BindingFlags.Instance | BindingFlags.NonPublic);
        var finish = typeof(Silksong::HeroController)
            .GetMethod("FinishedDashing", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) },
                null);
        if (dash == null || finish == null) {
            Log.Error("[QuakeFloor] HeroDashPressed / FinishedDashing not found");
            return;
        }

        dashHook = new Hook(dash, (Action<Action<Silksong::HeroController>, Silksong::HeroController>)((orig, self) => {
            orig(self);
            if (!quaking && HeroSwitch.HornetActive && self.dashingDown) StartQuake();
        }));
        finishHook = new Hook(finish,
            (Action<Action<Silksong::HeroController, bool>, Silksong::HeroController, bool>)((orig, self, wasDown) => {
                orig(self, wasDown);
                if (quaking) EndQuake();
            }));

        // Some HK floors (e.g. Crystal Peak's Loose Floors via the "Detect Quake" FSM) don't use the quake_floor FSM;
        // they detect the diving hero by calling HeroController.GetState("spellQuake"). Hornet has no such cState, so
        // report it true while she's mid-down-dash — the whole HK detect-quake -> BREAK sequence (debris, cracks, the
        // passable Quaked Floor) then runs itself. (Also avoids the "Could not find bool named spellQuake" log.)
        var getState = typeof(Silksong::HeroController).GetMethod("GetState",
            BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
        if (getState != null)
            getStateHook = new Hook(getState,
                (Func<Func<Silksong::HeroController, string, bool>, Silksong::HeroController, string, bool>)(
                    (orig, self, name) =>
                        (quaking && HeroSwitch.HornetActive && name == "spellQuake") || orig(self, name)));
        else Log.Error("[QuakeFloor] HeroController.GetState(string) not found");

        Log.Debug("[QuakeFloor] installed: down-dash -> quake-floor break");
    }

    // Per-frame while quaking only: break the transient floors Hornet's body collider overlaps. Cheap — iterates the
    // small cached list (built once on StartQuake), no per-frame FindObjectsByType.
    internal static void Tick(Silksong::HeroController hero) {
        if (!quaking) return;
        var body = hero.GetComponent<BoxCollider2D>();
        if (body == null) return;
        var hb = body.bounds;
        for (var i = 0; i < floors.Count; i++) {
            var fsm = floors[i];
            var col = floorCols[i];
            if (fsm != null && col != null && col.bounds.Intersects(hb)) fsm.SendEvent("DESTROY");
        }
    }

    private static void StartQuake() {
        quaking = true;
        floors.Clear();
        floorCols.Clear();
        foreach (var fsm in Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None))
            if (fsm != null && fsm.FsmName == "quake_floor") {
                fsm.SendEvent("QUAKE FALL START");
                floors.Add(fsm);
                floorCols.Add(fsm.GetComponent<Collider2D>());
            }

        Log.Info($"[QuakeFloor] QUAKE FALL START -> {floors.Count} floors");
    }

    private static void EndQuake() {
        foreach (var fsm in floors)
            if (fsm != null)
                fsm.SendEvent("QUAKE FALL END");
        floors.Clear();
        floorCols.Clear();
        quaking = false;
    }

    // Safety: switch/despawn mid-dash -> un-transient the floors so they don't stay pass-through.
    internal static void CancelIfActive() {
        if (quaking) EndQuake();
    }

    internal static void Cleanup() {
        if (quaking) EndQuake();
        dashHook?.Dispose();
        dashHook = null;
        finishHook?.Dispose();
        finishHook = null;
        getStateHook?.Dispose();
        getStateHook = null;
    }
}
