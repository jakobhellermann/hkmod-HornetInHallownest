extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine.LowLevel;

namespace HornetPlayer.Playground;

// Install Silksong's CustomPlayerLoop "LateFixedUpdate" phase into HK's Unity PlayerLoop.
//
// Silksong drives a custom post-FixedUpdate phase (CustomPlayerLoop) that ticks every registered ILateFixedUpdate and
// increments FixedUpdateCycle. DamageEnemies is an ILateFixedUpdate: its OnTriggerEnter2D only QUEUES hit colliders;
// the actual EvaluateDamage -> DoDamage -> ProcessDamageBuffer (-> IHitResponder.Hit) runs in LateFixedUpdate. So
// without this phase, Hornet's ground slashes (Slash/AltSlash/UpSlash) detect the enemy collider but never deal damage
// (the DownSlash worked only because HeroDownAttack calls DoDamage directly).
//
// Silksong installs the phase via a private [RuntimeInitializeOnLoadMethod] (SetupCustomPlayerLoop) that runs at engine
// boot. Our mod loads the Silksong assembly AFTER that phase has passed, so it never ran in HK. We invoke it once here.
// The phase's update delegate captures only Silksong's static lists (no reference to our mod), so it survives a mod
// hot-reload/unload cleanly; Ensure() is idempotent (skips if the phase is already in the loop). This replaces the
// HornetEnvironmentAdapter's manual FixedUpdateCycle bump — the real phase now advances the counter itself.
internal static class CustomPlayerLoopBootstrap {
    internal static void Ensure() {
        try {
            var cpl = typeof(Silksong::CustomPlayerLoop);
            var lateType = cpl.GetNestedType("LateFixedUpdate", BindingFlags.NonPublic);
            if (lateType == null) {
                Log.Error("[CustomPlayerLoop] nested LateFixedUpdate type not found");
                return;
            }

            if (Contains(PlayerLoop.GetCurrentPlayerLoop(), lateType)) {
                Log.Info("[CustomPlayerLoop] LateFixedUpdate phase already installed");
                return;
            }

            var setup = cpl.GetMethod("SetupCustomPlayerLoop", BindingFlags.NonPublic | BindingFlags.Static);
            if (setup == null) {
                Log.Error("[CustomPlayerLoop] SetupCustomPlayerLoop not found");
                return;
            }

            setup.Invoke(null, null);
            var ok = Contains(PlayerLoop.GetCurrentPlayerLoop(), lateType);
            Log.Info($"[CustomPlayerLoop] installed LateFixedUpdate phase (present={ok})");
        } catch (Exception e) {
            Log.Error($"[CustomPlayerLoop] {e}");
        }
    }

    private static bool Contains(PlayerLoopSystem sys, Type t) {
        if (sys.type == t) return true;
        var subs = sys.subSystemList;
        if (subs != null)
            foreach (var s in subs)
                if (Contains(s, t))
                    return true;
        return false;
    }
}
