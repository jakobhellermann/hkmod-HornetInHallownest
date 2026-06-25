extern alias Silksong;
using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using SHeroController = Silksong::HeroController;

namespace HornetPlayer.Playground;

// DIAGNOSTIC: trace why the harpoon dash (S -> SuperDash action) intermittently fails to fire. The harpoon is triggered
// by HeroController.LookForQueueInput: on SuperDash.WasPressed (+ gates) it sends the "Harpoon Dash" FSM "DO MOVE".
// Hook it and log — only on a frame where SuperDash is pressed — whether LookForQueueInput runs at all, what it sees
// (WasPressed/IsPressed), and whether CanHarpoonDash() passes. Pair with the InputDriver's [HarpoonInput] log (does the
// WasPressed edge get produced at all). Localises the break: edge not produced (InControl/commit) vs LookForQueueInput
// not running (gated upstream) vs gate fails vs DO MOVE sent but FSM no-ops.
internal static class HarpoonProbe {
    private static Hook? hook;

    internal static void Install() {
        var mi = typeof(SHeroController).GetMethod("LookForQueueInput",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi == null) {
            Log.Error("[HarpoonProbe] HeroController.LookForQueueInput not found");
            return;
        }

        hook = new Hook(mi, (Action<Action<SHeroController>, SHeroController>)OnLookForQueueInput);
        Log.Info("[HarpoonProbe] installed: HeroController.LookForQueueInput");
    }

    private static void OnLookForQueueInput(Action<SHeroController> orig, SHeroController self) {
        var ia = SilksongBootstrap.InputActions;
        var sd = ia?.SuperDash;
        // Only the rising edge = one line per press attempt. When the harpoon FAILS to fire while Hornet is active, this
        // shows exactly why: CanHarpoonDash=false (a gate) vs true-but-no-dash (queuing / FSM). The in-progress dash
        // frames (was=false, is=true) are not logged.
        if (sd != null && sd.WasPressed)
            Log.Info($"[HarpoonProbe] SuperDash WasPressed: CanHarpoonDash={self.CanHarpoonDash()} " +
                     $"blocked={self.IsInputBlocked()} paused={self.IsPaused()}");
        orig(self);
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }
}
