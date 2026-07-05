extern alias Silksong;
using UnityEngine;
using SHeroController = Silksong::HeroController;

namespace HornetPlayer.Playground;

// Make Hornet sit when the player rests on an HK bench.
//
// HK's bench machinery already runs (its "Bench Control" FSM moves the inert Knight onto the bench, sets the respawn
// point via RespawnTrigger, heals HK's PlayerData, saves) — but it's Knight-tied, so Hornet just stands beside the
// resting Knight and her Silksong HP isn't healed. Same seam as death: HK drives, Hornet mirrors. The clean signal is
// HK's `PlayerData.atBench` (set true by HK's Bench Control FSM while resting); we mirror its edges onto Hornet:
//   enter -> snap to the bench (Knight's spot), heal her Silksong HP, relinquish control + gravity off + stop HAC, play
//            her real sit clips ("Sit" -> "Sit Idle").
//   exit  -> StartAnimationControlToIdle + gravity back + RegainControl (she stands up).
// Respawn-point/heal/save on the HK side already happen; this only adds Hornet's sit visual + her own heal.
internal sealed class HornetBench : MonoBehaviour {
    private static GameObject? go;
    private bool benchWakeUnstuck;
    private bool sitting;

    private void Update() {
        var hero = BundleSpike.RealHero;
        if (hero == null) {
            sitting = false;
            return;
        }

        var knight = HeroController.UnsafeInstance;
        var pd = PlayerData.instance; // HK's — atBench is HK's flag
        var resting = pd != null && pd.atBench && HeroSwitch.HornetActive;

        if (resting && !sitting) EnterSit(hero);
        else if (!resting && sitting) ExitSit(hero);

        // Unstick HK's death/load bench-wake so the bench can later free itself. After a death respawn the "Bench Control"
        // FSM runs Init Resting -> Startle; "Startle" plays "Wake To Sit" on the hero and waits for that animation to
        // COMPLETE before advancing to the get-up-ready "Resting". The animation plays on the INERT Knight (HeroSwitch
        // paused its tk2d animator) so it never completes -> the FSM hangs in "Startle" -> atBench stuck true AND the
        // bench can never be used again (it never returns to "Idle") until a scene reload. A normal rest never enters
        // "Startle", so completing it is safe + scoped: push it to "Resting" so the player's get-up cycles the FSM home.
        if (resting) {
            if (!benchWakeUnstuck && TryAdvanceStuckBenchWake()) benchWakeUnstuck = true;
        }
        else {
            benchWakeUnstuck = false;
        }

        if (knight == null || !HeroSwitch.HornetActive) return;

        if (sitting)
            // Track the Knight as HK's bench FSM slides it the last bit onto the seat (over several frames after atBench
            // flips), so Hornet ends up exactly where it settles.
            hero.transform.position = knight.transform.position;
        else if (knight.cState != null && knight.cState.nearBench)
            // Hornet is standing on a bench trigger — HK's RestBench set the KNIGHT's nearBench (it calls
            // HeroController.instance.NearBench on any layer-9 collider). Glue the far, inert Knight onto her NOW, BEFORE
            // rest is chosen: otherwise HK's bench FSM slides it onto the seat from across the room and the camera flashes
            // to that far slide. Pre-positioned here, the slide is a short local hop with nothing to flash to.
            knight.transform.position = hero.transform.position;
    }

    private void EnterSit(SHeroController hero) {
        sitting = true;

        // The Knight is already glued to Hornet here (the nearBench pre-position in Update ran while she walked onto the
        // bench), so no enter-time teleport is needed. Just kill carried velocity so she doesn't drift while seated.
        var rb = hero.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        hero.RelinquishControl(); // no movement/abilities while seated
        hero.AffectedByGravity(false);
        hero.StopAnimationControl(); // stop HAC so it doesn't override the sit clips with Idle/locomotion

        // Bench heals — HK's heal only touched HK's PlayerData, so heal her Silksong HP too.
        hero.MaxHealth();

        // Refill her tools for free. Tools normally replenish by spending Shell Shards, but Hornet can't collect shards
        // yet (not implemented) → her tools stay empty and unusable. Until shard collection exists, treat the bench as a
        // free full refill: set every unlocked tool's AmountLeft to its max (the exact idiom ToolItemManager uses for
        // IsInfiniteToolUseEnabled — no shard cost). Remove this once shards are collectable.
        RefillTools();

        // Her real sit anim: "Sit" (sit-down) then loop "Sit Idle". tk2d AnimationCompleted chains the loop.
        var anim = hero.AnimCtrl?.animator;
        if (anim != null) {
            anim.Play("Sit");
            anim.AnimationCompleted = (a, _) => a.Play("Sit Idle");
        }

        Log.Info($"[HornetBench] Hornet sits (atBench) at {(Vector2)hero.transform.position}, healed");
        // Mirror atBench onto Silksong's PlayerData so inventory CanChangeEquips() (reads
        // GameManager.instance.playerData.atBench = Silksong's PD) allows equipping while resting.
        var spd = Silksong::PlayerData.instance;
        spd?.atBench = true;
    }

    // Set every unlocked tool's AmountLeft to its storage max — a free refill (no Shell Shard spend), mirroring
    // ToolItemManager's own IsInfiniteToolUseEnabled path. TEMPORARY: drop when Shell Shard collection is implemented.
    private static void RefillTools() {
        try {
            var spd = Silksong::PlayerData.instance;
            var tools = spd?.Tools;
            if (tools == null) return;
            var n = 0;
            foreach (var tool in Silksong::ToolItemManager.GetUnlockedTools()) {
                if (tool == null) continue;
                var data = tools.GetData(tool.name);
                data.AmountLeft = Silksong::ToolItemManager.GetToolStorageAmount(tool);
                tools.SetData(tool.name, data);
                n++;
            }

            Log.Info($"[HornetBench] refilled {n} tools to full (Shell Shards not yet collectable)");
        } catch (System.Exception e) {
            Log.Error($"[HornetBench] tool refill failed: {e.Message}");
        }
    }

    // Find the active HK bench FSM hung in "Startle" and push it past the un-completing wake animation toward "Resting".
    // Returns true once it sent the event (caller stops scanning). Only scans during the brief death-respawn window
    // (resting && not yet unstuck), so the FindObjectsOfType cost is bounded.
    private static bool TryAdvanceStuckBenchWake() {
        foreach (var fsm in FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.InstanceID))
            if (fsm.FsmName == "Bench Control" && fsm.ActiveStateName == "Startle") {
                fsm.SendEvent("FINISHED"); // Startle -> Update Map Silently -> Resting (the Wake-To-Sit complete event)
                Log.Info("[HornetBench] bench wake hung in 'Startle' (Wake-To-Sit never completes on inert Knight) "
                         + "-> sent FINISHED to advance toward 'Resting' (get-up ready, frees the bench)");
                return true;
            }

        return false;
    }

    private void ExitSit(SHeroController hero) {
        sitting = false;
        var anim = hero.AnimCtrl?.animator;
        if (anim != null) anim.AnimationCompleted = null; // drop the Sit-Idle chaining
        hero.StartAnimationControlToIdle(); // resume HAC -> stand in Idle
        hero.AffectedByGravity(true);
        hero.RegainControl();
        // Clear the atBench mirror EnterSit set. Without this it stays true forever after the first rest, and anything
        // gated on Silksong's atBench silently breaks — e.g. Needolin's "Needolin Sub" has PlayerDataBoolTest(atBench,
        // isTrue->CANCEL) ("Cancel if at bench to prevent restbench and this from fighting"), so needolin instantly
        // cancels every press once she has rested anywhere.
        var spd = Silksong::PlayerData.instance;
        if (spd != null) spd.atBench = false;
        Log.Info("[HornetBench] Hornet stands up (left bench)");
    }

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.HornetBench");
        go.AddComponent<HornetBench>();
        DontDestroyOnLoad(go);
    }

    internal static void Cleanup() {
        if (go != null) {
            Destroy(go);
            go = null;
        }
    }
}
