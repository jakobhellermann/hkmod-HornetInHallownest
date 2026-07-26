extern alias Silksong;
using System;
using System.Linq;
using System.Reflection;
using HornetInHallownest.Core;
using UnityEngine.LowLevel;

namespace HornetInHallownest.Modules;

// Install Silksong's CustomPlayerLoop "LateFixedUpdate" phase into HK's PlayerLoop.
// Silksong ticks every ILateFixedUpdate there (DamageEnemies runs its queued EvaluateDamage -> DoDamage) and increments
// FixedUpdateCycle.
public sealed class PlayerLoopModule : ModuleBase {
    public override string Id => "player-loop";

    public override void Initialize() {
        var cpl = typeof(Silksong::CustomPlayerLoop);
        var lateType = cpl.GetNestedType("LateFixedUpdate", BindingFlags.NonPublic)!;
        if (Contains(PlayerLoop.GetCurrentPlayerLoop(), lateType)) return;
        cpl.GetMethod("SetupCustomPlayerLoop", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    }

    private static bool Contains(PlayerLoopSystem sys, Type t) {
        if (sys.type == t) return true;
        if (sys.subSystemList != null) return sys.subSystemList.Any(s => Contains(s, t));
        return false;
    }
}
