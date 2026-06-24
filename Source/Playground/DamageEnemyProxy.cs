extern alias Silksong;
using System;
using System.Reflection;
using HutongGames.PlayMaker;
using MonoMod.RuntimeDetour;

namespace HornetPlayer.Playground;

// HK breakables/enemies read damage info from a PlayMakerFSM named "damages_enemy" on the attacker's slash.
// Silksong's slash uses DamageEnemies (code-based) instead of PlayMaker. This proxy adds an HK PlayMakerFSM
// named "damages_enemy" to each of Hornet's slash GOs with the variables HK reads (damageDealt, direction,
// attackType, magnitudeMult, circleDirection, Multiplier). direction is synced via a hook on
// DamageEnemies.SetDirection (called at attack time), not per-frame.
internal static class DamageEnemyProxy {
    private static Hook? setDirectionHook;

    internal static void Install() {
        var hero = BundleSpike.RealHero;
        if (!hero) return;

        var attacks = hero.transform.Find("Attacks");
        if (!attacks) return;

        var count = 0;
        foreach (var dmg in attacks.GetComponentsInChildren<Silksong::DamageEnemies>(true)) {
            var go = dmg.gameObject;
            if (go.GetComponent<PlayMakerFSM>() != null) continue;

            // Add the FSM while the GO is INACTIVE, then restore its active state. AddComponent on an ACTIVE GO runs
            // PlayMakerFSM.Awake -> Init -> Preprocess synchronously on the default (uninitialized) Fsm — whose single
            // null-Transition state NullRefs before we can set the empty arrays below. On an INACTIVE GO Awake never
            // runs, so the .Fsm getter (fsm.Owner = this) NullRefs on the still-null fsm. Both failure modes were the
            // ~20 "AddComponent failed" errors on spawn. Adding while inactive defers Awake; Reset() initializes the
            // fsm so .Fsm is safe; restoring active state then runs Awake once on the clean, empty fsm.
            var wasActive = go.activeSelf;
            if (wasActive) go.SetActive(false);

            PlayMakerFSM fsmComp;
            Fsm fsm;
            try {
                fsmComp = go.AddComponent<PlayMakerFSM>();
                fsmComp.Reset(); // init fsm (Awake didn't run while inactive) so the .Fsm getter doesn't NullRef
                fsm = fsmComp.Fsm;
            } catch (Exception e) {
                Log.ErrorOnce($"proxy|{go.name}", $"[DamageEnemyProxy] AddComponent failed on {go.name}: {e.Message}");
                if (wasActive) go.SetActive(true);
                continue;
            }

            fsm.Name = "damages_enemy";

            fsm.Variables.IntVariables = [
                new FsmInt("damageDealt") { Value = dmg.damageDealt },
                new FsmInt("attackType") { Value = MapAttackType((int)dmg.attackType) }
            ];
            fsm.Variables.FloatVariables = [
                new FsmFloat("direction") { Value = dmg.direction },
                new FsmFloat("magnitudeMult") { Value = 1f },
                new FsmFloat("Multiplier") { Value = 1f }
            ];
            fsm.Variables.BoolVariables = [
                new FsmBool("circleDirection") { Value = dmg.CircleDirection }
            ];
            // Fsm.Init (run on activation) iterates states/events/globalTransitions. The default Fsm creates
            // states = new FsmState[1] but the element has null Transitions → NullRef. Empty arrays make Init no-op.
            fsm.States = [];
            fsm.Events = [];
            fsm.GlobalTransitions = [];

            // This is a pure VARIABLE CONTAINER for HK breakables to read (damageDealt/direction/…) — it must never
            // RUN. A live PlayMakerFSM with empty States ticks Fsm.Update → Continue → EnterState(null start state) →
            // NullRef every frame the GO is active. Disabling stops OnEnable/Start/Update while Awake still registers it
            // in fsmList and GetComponent/FsmVariables stay readable, so HK's lookup + variable reads are unaffected.
            fsmComp.enabled = false;

            if (wasActive) go.SetActive(true);
            count++;
        }

        if (setDirectionHook == null) {
            var mi = typeof(Silksong::DamageEnemies).GetMethod("SetDirection",
                BindingFlags.Public | BindingFlags.Instance);
            if (mi != null) setDirectionHook = new Hook(mi, SetDirectionHook);
        }

        Log.Info($"[DamageEnemyProxy] installed on {count} slash GOs");
    }

    // Silksong has AttackTypes values 8+ that don't exist in HK's enum. Map those to Generic (1).
    private static int MapAttackType(int ss) {
        return ss <= 7 ? ss : 1;
    }

    internal static void Cleanup() {
        setDirectionHook?.Dispose();
        setDirectionHook = null;
    }

    private static void SetDirectionHook(
        Action<Silksong::DamageEnemies, float> orig,
        Silksong::DamageEnemies self, float newDirection) {
        orig(self, newDirection);
        var fsm = self.GetComponent<PlayMakerFSM>();
        if (!fsm) return;
        var dir = fsm.Fsm.Variables.GetFsmFloat("direction");
        dir?.Value = newDirection;
    }
}
