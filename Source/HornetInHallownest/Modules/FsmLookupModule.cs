extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.HornetInHallownest.Core;
using HornetInHallownest.HornetInHallownest.Util;
using HutongGames.PlayMaker;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.HornetInHallownest.Modules;

// Three HK FSM lookups (ActionHelpers.GetGameObjectFsm / PlayMakerFSM.FindFsmOnGameObject / FSMUtility.LocateFSM) find a
// named PlayMakerFSM via go.GetComponents<HK PlayMakerFSM>(), but the FSMs they expect aren't on Hornet's objects:
// Knight-only ones (Dream Return), ones only present as the isolated Silksong type (ProxyFSM), or ones she implements
// differently (her slash uses a Silksong DamageEnemies component, not a "damages_enemy" FSM). So we synthesize a dummy:
// inert for the hero-owned names; one carrying direction/damageDealt for the slash, which HK geo rocks / stag bells read.
public sealed class FsmLookupModule : ModuleBase {
    public override string Id => "fsm-lookup";

    #region Generic hooks
    private GameObject? holder;

    public override void Initialize() {
        Detour(typeof(ActionHelpers), "GetGameObjectFsm", OnLookup, typeof(GameObject), typeof(string));
        Detour(typeof(PlayMakerFSM), "FindFsmOnGameObject", OnLookup, typeof(GameObject), typeof(string));
        Detour(typeof(FSMUtility), "LocateFSM", OnLookup, typeof(GameObject), typeof(string));
    }

    protected override void OnDeinitialize() {
        heroDummies.Clear();
        damagesDummy = null;
        roarLockDummy = null;
        roarObjectVar = null;
        if (holder) Object.Destroy(holder);
        holder = null;
    }

    private PlayMakerFSM OnLookup(Func<GameObject, string, PlayMakerFSM> orig, GameObject go, string fsmName) {
        if (!string.IsNullOrEmpty(fsmName) && TryPatch(go, fsmName) is { } dummy) return dummy;
        return orig(go, fsmName);
    }

    private PlayMakerFSM? TryPatch(GameObject go, string fsmName) {
        if (fsmName == "Roar Lock" && go.GetComponent<Silksong::HeroController>())
            return RoarLockDummy();
        if (heroInertNames.Contains(fsmName) && go.GetComponent<Silksong::HeroController>())
            return HeroDummy(fsmName);
        if (fsmName == "damages_enemy") {
            var de = go.GetComponentInParent<Silksong::DamageEnemies>();
            if (de) return DamagesDummy(de);
        }

        return null;
    }

    // Inactive holder so PlayMakerFSM.Awake never runs (no log noise / Update); a stateless FSM never runs anyway.
    private PlayMakerFSM NewDummy(string fsmName) {
        if (!holder) {
            holder = new GameObject("hp_fsm_lookup_shim");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
        }

        var dummy = holder.AddComponent<PlayMakerFSM>();
        dummy.SetFieldValue("fsm", new Fsm { Name = fsmName });
        return dummy;
    }
    #endregion

    internal static GameObject? RoarObject => roarObjectVar?.Value;
    private static FsmGameObject? roarObjectVar;
    private PlayMakerFSM? roarLockDummy;

    private PlayMakerFSM RoarLockDummy() {
        if (!roarLockDummy) {
            roarLockDummy = NewDummy("Roar Lock");
            roarLockDummy.Fsm.Variables.GameObjectVariables = [roarObjectVar = new FsmGameObject("Roar Object")];
        }

        return roarLockDummy;
    }

    // Hero-owned FSMs the Knight has and Hornet lacks -> an empty dummy (Set/GetFsm* null-check the var, so it's a no-op).
    private static readonly HashSet<string> heroInertNames = ["Dream Return", "ProxyFSM"];
    private readonly Dictionary<string, PlayMakerFSM> heroDummies = new();

    private PlayMakerFSM HeroDummy(string fsmName) {
        if (!heroDummies.TryGetValue(fsmName, out var d) || !d) heroDummies[fsmName] = d = NewDummy(fsmName);
        return d;
    }

    #region damages_enemy: Hornet's slash uses a DamageEnemies component, not an FSM; hand HK's readers a stand-in
    // Some FSMs (e.g. stag bell) read vars off the slash "damages_enemy" FSM.
    private PlayMakerFSM? damagesDummy;
    private FsmInt? damageDealt;
    private FsmInt? attackType;
    private FsmFloat? direction;
    private FsmBool? circleDirection;

    private PlayMakerFSM DamagesDummy(Silksong::DamageEnemies de) {
        if (!damagesDummy) {
            damagesDummy = NewDummy("damages_enemy");
            var v = damagesDummy.Fsm.Variables;
            v.IntVariables = [damageDealt = new FsmInt("damageDealt"), attackType = new FsmInt("attackType")];
            v.FloatVariables =
                [direction = new FsmFloat("direction"), new FsmFloat("magnitudeMult") { Value = 1f }, new FsmFloat("Multiplier") { Value = 1f }];
            v.BoolVariables = [circleDirection = new FsmBool("circleDirection")];
        }

        damageDealt!.Value = de.damageDealt;
        attackType!.Value = MapAttackType((int)de.attackType);
        direction!.Value = de.direction;
        circleDirection!.Value = de.CircleDirection;
        return damagesDummy;
    }

    // Silksong attacktypes 0-7 are unchanged, 8+ have no equivalent
    private static int MapAttackType(int ss) {
        return ss <= 7 ? ss : 1;
    }
    #endregion
}
