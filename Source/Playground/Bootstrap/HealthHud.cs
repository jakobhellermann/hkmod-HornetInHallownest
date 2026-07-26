extern alias SilksongPM;
using System;
using UnityEngine;

namespace HornetInHallownest.Playground;

// Silksong's health-mask HUD, driven through the isolated Silksong PlayMaker via the per-mask health_display FSMs.
internal static class HealthHud {
    // "MAX HP UP" can only ever add a mask.
    internal static void RefreshMaxHealthHud() {
        SendToHealthDisplays("MAX HP UP");
    }

    // "HUD APPEAR RESET" is a global transition (any state -> Init) so every mask re-reads count and fill; it can also
    // remove masks when maxHealth dropped (which "MAX HP UP" can't). Needed because the DDOL rig otherwise shows the
    // previous life's mask count on a reused rig (new save / menu re-entry).
    internal static void ResetHealthHud() {
        SendToHealthDisplays("HUD APPEAR RESET");
    }

    private static void SendToHealthDisplays(string evt) {
        // Re-enable the self-disabled (EnableFsmSelf(false)) FSMs first, so the event reaches masks parked in Idle/Inactive.
        foreach (var f in Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>())
            if (f != null && string.Equals(f.FsmName, "health_display", StringComparison.OrdinalIgnoreCase) &&
                f.gameObject.activeInHierarchy)
                try {
                    var b = (Behaviour)f;
                    if (!b.enabled) b.enabled = true;
                    f.SendEvent(evt);
                } catch {
                }
    }
}
