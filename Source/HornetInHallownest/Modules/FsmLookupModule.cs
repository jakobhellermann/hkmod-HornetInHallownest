extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using HutongGames.PlayMaker;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.HornetInHallownest.Modules;

// Three HK FSM lookups (ActionHelpers.GetGameObjectFsm / PlayMakerFSM.FindFsmOnGameObject / FSMUtility.LocateFSM) find a
// named PlayMakerFSM via go.GetComponents<HK PlayMakerFSM>() — but the FSMs they expect aren't on Hornet's objects:
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
        dirVar = null;
        dmgVar = null;
        if (holder) Object.Destroy(holder);
        holder = null;
    }

    private PlayMakerFSM OnLookup(Func<GameObject, string, PlayMakerFSM> orig, GameObject go, string fsmName) {
        if (!string.IsNullOrEmpty(fsmName) && TryPatch(go, fsmName) is { } dummy) return dummy;
        return orig(go, fsmName);
    }

    private PlayMakerFSM? TryPatch(GameObject go, string fsmName) {
        if (heroInertNames.Contains(fsmName) && go.GetComponent<Silksong::HeroController>())
            return HeroDummy(fsmName);
        if (fsmName == "damages_enemy" && go.GetComponentInParent<Silksong::DamageEnemies>())
            return DamagesDummy(go);
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

    // Hero-owned FSMs the Knight has and Hornet lacks -> an empty dummy (Set/GetFsm* null-check the var, so it's a no-op).
    private static readonly HashSet<string> heroInertNames = ["Dream Return", "ProxyFSM"];
    private readonly Dictionary<string, PlayMakerFSM> heroDummies = new();

    private PlayMakerFSM HeroDummy(string fsmName) {
        if (!heroDummies.TryGetValue(fsmName, out var d) || !d) heroDummies[fsmName] = d = NewDummy(fsmName);
        return d;
    }

    #region damages_enemy — Hornet's slash uses a DamageEnemies component, not an FSM; hand HK's readers a stand-in
    private PlayMakerFSM? damagesDummy;
    private FsmFloat? dirVar;
    private FsmInt? dmgVar;

    private PlayMakerFSM DamagesDummy(GameObject go) {
        if (!damagesDummy) {
            damagesDummy = NewDummy("damages_enemy");
            dirVar = new FsmFloat("direction");
            dmgVar = new FsmInt("damageDealt");
            damagesDummy.Fsm.Variables.FloatVariables = [dirVar];
            damagesDummy.Fsm.Variables.IntVariables = [dmgVar];
        }

        dirVar!.Value = SlashDirection(go);
        dmgVar!.Value = NailDamage();
        return damagesDummy;
    }

    // TC angle: 0=right 90=up 180=left 270=down. Vertical slashes are named Up/Down; horizontal topple toward facing.
    private static float SlashDirection(GameObject go) {
        var de = go.GetComponentInParent<Silksong::DamageEnemies>();
        var name = de ? de.gameObject.name : go.name;
        if (name.Contains("Up", StringComparison.OrdinalIgnoreCase)) return 90f;
        if (name.Contains("Down", StringComparison.OrdinalIgnoreCase)) return 270f;
        var hc = Silksong::HeroController.UnsafeInstance;
        return hc && hc.cState is { facingRight: true } ? 0f : 180f;
    }

    private static int NailDamage() {
        var pd = Silksong::PlayerData.instance;
        return pd is { nailDamage: > 0 } ? pd.nailDamage : 5;
    }
    #endregion
}
