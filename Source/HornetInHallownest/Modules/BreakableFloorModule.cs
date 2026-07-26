extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.HornetInHallownest.Core;
using HornetInHallownest.Playground;
using UnityEngine;
using Object = UnityEngine.Object;
using SHeroController = Silksong::HeroController;

namespace HornetInHallownest.HornetInHallownest.Modules;

// Hornet's down-dash breaks HK breakable floors (vanilla only the Knight's Desolate Dive does; Hornet has no equivalent).
// HK quake floors idle in "Solid"; "QUAKE FALL START" flips them to "Transient" (collider -> trigger), "QUAKE FALL END"
// back. We broadcast those around a down-dash and, while quaking, send "DESTROY" to any transient floor Hornet's body
// overlaps. // TODO is that necessary?
public sealed class BreakableFloorModule : ModuleBase {
    private readonly List<Collider2D> floorCols = [];
    private readonly List<PlayMakerFSM> floors = [];
    private bool quaking;

    public override string Id => "breakable-floor";

    public override void Initialize() {
        Detour(typeof(SHeroController), "HeroDashPressed", OnDashPressed);
        Detour(typeof(SHeroController), "FinishedDashing", OnFinishedDashing, typeof(bool));
        Detour(typeof(SHeroController), "GetState", OnGetState, typeof(string));
    }

    protected override void OnDeinitialize() {
        if (quaking) EndQuake();
    }

    // Switch to Knight / despawn mid-dash -> un-transient the floors so they don't stay pass-through.
    public override void HornetToggled(bool active) {
        if (!active && quaking) EndQuake();
    }

    // Per-frame while quaking: break the transient floors Hornet's body overlaps (small cached list, no FindObjects).
    public override void HornetActiveUpdate(SHeroController hero) {
        if (!quaking) return;
        // TODO: perf
        var body = hero.GetComponent<BoxCollider2D>();
        if (!body) return;
        var hb = body.bounds;
        for (var i = 0; i < floors.Count; i++)
            if (floors[i] && floorCols[i] && floorCols[i].bounds.Intersects(hb))
                floors[i].SendEvent("DESTROY");
    }

    private void OnDashPressed(Action<SHeroController> orig, SHeroController self) {
        orig(self);
        if (!quaking && HeroSwitch.HornetActive && self.dashingDown) StartQuake();
    }

    private void OnFinishedDashing(Action<SHeroController, bool> orig, SHeroController self, bool wasDown) {
        orig(self, wasDown);
        if (quaking) EndQuake();
    }

    // Some floors (Crystal Peak Loose Floors via the "Detect Quake" FSM) check HeroController.GetState("spellQuake")
    // instead of the quake_floor FSM. Hornet has no such cState -> report true mid-down-dash so their BREAK runs.
    private bool OnGetState(Func<SHeroController, string, bool> orig, SHeroController self, string name) {
        return (quaking && HeroSwitch.HornetActive && name == "spellQuake") || orig(self, name);
    }

    private void StartQuake() {
        quaking = true;
        floors.Clear();
        floorCols.Clear();
        foreach (var fsm in Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None)) {
            if (fsm && fsm.FsmName == "quake_floor") {
                fsm.SendEvent("QUAKE FALL START");
                floors.Add(fsm);
                floorCols.Add(fsm.GetComponent<Collider2D>());
            }
        }
    }

    private void EndQuake() {
        foreach (var fsm in floors) {
            if (fsm) {
                fsm.SendEvent("QUAKE FALL END");
            }
        }

        floors.Clear();
        floorCols.Clear();
        quaking = false;
    }
}
