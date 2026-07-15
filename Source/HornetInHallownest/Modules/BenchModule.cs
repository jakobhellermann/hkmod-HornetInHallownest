extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using SHeroController = Silksong::HeroController;

namespace HornetPlayer.HornetInHallownest.Modules;

// Set respawn, heal, save. Mirrors HK PlayerData.atBench.
// Additionally, refuel tools unconditionally.
public sealed class BenchModule : ModuleBase {
    private bool benchWakeUnstuck;
    private bool sitting;

    public override string Id => "bench";

    public override void Initialize() {
    }

    public override void HornetActiveUpdate(SHeroController hero) {
        var knight = HeroController.UnsafeInstance; // HK
        var pd = PlayerData.instance; // HK's — atBench is HK's flag
        var resting = pd is { atBench: true };

        if (resting && !sitting) EnterSit(hero);
        else if (!resting && sitting) ExitSit(hero);

        // After a death respawn HK's "Bench Control" FSM enters "Startle", which plays "Wake To Sit" on the hero and
        // waits for it to complete — but it plays on the inert Knight (animator paused) so it never does, hanging the
        // FSM (atBench stuck true, bench unusable until scene reload). Push it past once. A normal rest never enters it.
        if (resting) {
            if (!benchWakeUnstuck && TryAdvanceStuckBenchWake()) benchWakeUnstuck = true;
        } else {
            benchWakeUnstuck = false;
        }

        if (!knight) return;

        if (sitting)
            // Track the Knight as HK's bench FSM slides it onto the seat over several frames.
            hero.transform.position = knight.transform.position;
        else if (knight.cState is { nearBench: true })
            // Glue the far, inert Knight onto Hornet before rest is chosen: HK's RestBench set the Knight's nearBench,
            // and its FSM would otherwise slide the Knight onto the seat from across the room (camera flashes there).
            knight.transform.position = hero.transform.position;
    }

    public override void HornetToggled(bool active) {
        if (active || !sitting) return;
        var hero = HornetSpawner.RealHero;
        if (hero) {
            ExitSit(hero);
            return;
        }

        // Despawned mid-sit: just reset our state + the Silksong atBench mirror.
        sitting = false;
        benchWakeUnstuck = false;
        var spd = Silksong::PlayerData.instance;
        spd?.atBench = false;
    }

    // Setting a tool's AmountLeft doesn't notify the HUD; icons redraw only on these events. Must run while the HUD
    // "In-game" container is active (ToolHudIcon subscribes in Awake and doesn't re-read on SetActive re-enable).
    public static void RefreshToolHud() {
        Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
        Silksong::ToolItemManager.SendEquippedChangedEvent(true);
    }

    private void EnterSit(SHeroController hero) {
        sitting = true;

        // Knight is already glued to Hornet (nearBench pre-position); just kill carried velocity so she doesn't drift.
        if (hero.TryGetComponent<Rigidbody2D>(out var rb)) rb.linearVelocity = Vector2.zero;

        hero.RelinquishControl();
        hero.AffectedByGravity(false);
        hero.StopAnimationControl(); // stop HAC so it doesn't override the sit clips
        hero.MaxHealth(); // HK's heal only touched HK's PlayerData; heal her Silksong HP too
        RefillTools();

        var ctrl = hero.AnimCtrl;
        var anim = ctrl ? ctrl.animator : null;
        if (anim) {
            anim.Play("Sit");
            anim.AnimationCompleted = (a, _) => a.Play("Sit Idle");
        }

        // Mirror atBench onto Silksong's PD so inventory CanChangeEquips() allows equipping while resting.
        var spd = Silksong::PlayerData.instance;
        if (spd != null) spd.atBench = true;
    }

    private void ExitSit(SHeroController hero) {
        sitting = false;
        var ctrl = hero.AnimCtrl;
        var anim = ctrl ? ctrl.animator : null;
        if (anim) anim.AnimationCompleted = null; // drop the Sit-Idle chaining

        hero.StartAnimationControlToIdle();
        hero.AffectedByGravity(true);
        hero.RegainControl();

        // Without clearing this, anything gated on Silksong's atBench breaks after the first rest (e.g. Needolin cancels
        // on atBench).
        var spd = Silksong::PlayerData.instance;
        if (spd != null) spd.atBench = false;
    }

    // Free full tool refill (no Shell Shard spend), mirroring ToolItemManager's IsInfiniteToolUseEnabled path.
    // temporary: drop once Shell Shard collection is implemented.
    private void RefillTools() {
        try {
            var tools = Silksong::PlayerData.instance?.Tools;
            if (tools == null) return;
            foreach (var tool in Silksong::ToolItemManager.GetUnlockedTools()) {
                if (!tool) continue;
                var data = tools.GetData(tool.name);
                data.AmountLeft = Silksong::ToolItemManager.GetToolStorageAmount(tool);
                tools.SetData(tool.name, data);
            }

            RefreshToolHud();
        } catch (Exception e) {
            LogError($"tool refill failed: {e.Message}");
        }
    }

    // Advance HK's bench FSM past the wake animation that never completes on the inert Knight. Only scanned during the
    // brief death-respawn window, so the FindObjects cost is bounded.
    private static bool TryAdvanceStuckBenchWake() {
        foreach (var fsm in Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.InstanceID))
            if (fsm.FsmName == "Bench Control" && fsm.ActiveStateName == "Startle") {
                fsm.SendEvent("FINISHED"); // Startle -> Update Map Silently -> Resting
                return true;
            }

        return false;
    }
}
