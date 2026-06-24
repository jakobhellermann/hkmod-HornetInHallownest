extern alias Silksong;
using System;
using HutongGames.PlayMaker;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// HK breakables/enemies read damage info from a PlayMakerFSM named "damages_enemy" on the attacker's slash.
// Silksong's slash uses DamageEnemies (code-based) instead of PlayMaker. This proxy adds an HK PlayMakerFSM
// named "damages_enemy" to each of Hornet's slash GOs with the variables HK reads (damageDealt, direction,
// attackType, magnitudeMult, circleDirection, Multiplier). direction is synced via a hook on
// DamageEnemies.SetDirection (called at attack time), not per-frame.
static class DamageEnemyProxy {

    private static Hook? setDirectionHook;

    internal static void Install() {
        var hero = BundleSpike.RealHero;
        if (hero == null) return;

        var attacks = hero.transform.Find("Attacks");
        if (attacks == null) return;

        int count = 0;
        foreach (var dmg in attacks.GetComponentsInChildren<Silksong::DamageEnemies>(true)) {
            var go = dmg.gameObject;
            if (go.GetComponent<PlayMakerFSM>() != null) continue;

            PlayMakerFSM fsmComp;
            Fsm? fsm = null;
            try {
                fsmComp = go.AddComponent<PlayMakerFSM>();
                fsm = fsmComp.Fsm;
            } catch (Exception e) {
                Log.ErrorOnce($"proxy|{go.name}", $"[DamageEnemyProxy] AddComponent failed on {go.name}: {e.Message}");
                continue;
            }
            if (fsm == null) continue;
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
            // InitData iterates states/events/globalTransitions. The default Fsm() constructor creates
            // states = new FsmState[1] but the element has null Transitions → NullRef in InitData when
            // the GO activates. Set empty arrays so InitData no-ops cleanly.
            fsm.States = [];
            fsm.Events = [];
            fsm.GlobalTransitions = [];

            count++;
        }

        if (setDirectionHook == null) {
            var mi = typeof(Silksong::DamageEnemies).GetMethod("SetDirection",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (mi != null) {
                setDirectionHook = new Hook(mi, SetDirectionHook);
            }
        }

        Log.Info($"[DamageEnemyProxy] installed on {count} slash GOs");
    }

    // Silksong has AttackTypes values 8+ that don't exist in HK's enum. Map those to Generic (1).
    private static int MapAttackType(int ss) => ss <= 7 ? ss : 1;

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
