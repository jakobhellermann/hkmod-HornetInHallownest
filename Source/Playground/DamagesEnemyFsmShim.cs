extern alias Silksong;
using System;
using System.Reflection;
using HutongGames.PlayMaker;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

// HK's PlayMaker (global ref) — Fsm / FsmFloat / FsmInt / FsmVariables

namespace HornetPlayer.Playground;

// Make HK objects that detect a nail hit by inspecting the attacker's "damages_enemy" FSM work for Hornet.
//
// Several HK scene objects read the SLASH's PlayMaker FSM named "damages_enemy" to react to a nail hit. Two lookup
// paths, two reads:
//   - BreakableObject / BreakablePoleSimple: PlayMakerFSM.FindFsmOnGameObject(slash, "damages_enemy") -> read float
//     "direction" (which way to topple).
//   - ReceivedDamage PlayMaker action (stag Station Bell "Bell Control", levers, …): FSMUtility.LocateFSM(slash,
//     "damages_enemy") -> require int "damageDealt" > 0, then fire its event (e.g. "NAIL HIT").
// Hornet's slash has no HK "damages_enemy" FSM (her FSMs run on the isolated Silksong.PlayMaker), so both lookups
// return null and the objects never react (geo rock NullRef'd; the bell stays silent).
//
// Fix (general): hook BOTH lookups and, for "damages_enemy" on one of Hornet's slashes, return a persistent dummy HK
// PlayMakerFSM carrying "direction" (TC angle: 0=right/90=up/180=left/270=down) and "damageDealt" (her nail damage).
// HK's Knight is untouched: it has the real FSM, so the originals return non-null and we don't interfere.
internal static class DamagesEnemyFsmShim {
    private static Hook? findHook;
    private static Hook? locateHook;
    private static PlayMakerFSM? dummy;
    private static FsmFloat? dirVar;
    private static FsmInt? dmgVar;

    internal static void Install() {
        var find = typeof(PlayMakerFSM).GetMethod("FindFsmOnGameObject", BindingFlags.Public | BindingFlags.Static,
            null, [typeof(GameObject), typeof(string)], null);
        if (find != null) findHook = new Hook(find, (Hooked)OnLookup);
        else Log.Error("[DamagesEnemyFsmShim] PlayMakerFSM.FindFsmOnGameObject not found");

        // ReceivedDamage (and others) locate the FSM via FSMUtility.LocateFSM, which uses GetComponents (NOT
        // FindFsmOnGameObject) — so it needs its own hook. Same (GameObject,string)->PlayMakerFSM signature.
        var locate = typeof(FSMUtility).GetMethod("LocateFSM", BindingFlags.Public | BindingFlags.Static,
            null, [typeof(GameObject), typeof(string)], null);
        if (locate != null) locateHook = new Hook(locate, (Hooked)OnLookup);
        else Log.Error("[DamagesEnemyFsmShim] FSMUtility.LocateFSM not found");

        Log.Info("[DamagesEnemyFsmShim] installed: FindFsmOnGameObject + FSMUtility.LocateFSM");
    }

    private static PlayMakerFSM OnLookup(Orig orig, GameObject go, string fsmName) {
        var found = orig(go, fsmName);
        if (found != null || fsmName != "damages_enemy" || go == null) return found;
        // Only stand in for Hornet's slash (a Silksong DamageEnemies attacker). Leave anything else to HK.
        if (go.GetComponentInParent<Silksong::DamageEnemies>() == null) return found;
        EnsureDummy();
        dirVar!.Value = SlashDirection(go);
        dmgVar!.Value = NailDamage();
        return dummy!;
    }

    // TC angle convention the readers use: 0=right, 90=up, 180=left, 270=down. Vertical slashes are named Up/DownSlash;
    // horizontal slashes (Slash/AltSlash/WallSlash) topple toward Hornet's facing.
    private static float SlashDirection(GameObject go) {
        var de = go.GetComponentInParent<Silksong::DamageEnemies>();
        var name = de != null ? de.gameObject.name : go.name;
        if (name.IndexOf("Up", StringComparison.OrdinalIgnoreCase) >= 0) return 90f;
        if (name.IndexOf("Down", StringComparison.OrdinalIgnoreCase) >= 0) return 270f;
        var hc = Silksong::HeroController.UnsafeInstance;
        var right = hc != null && hc.cState != null && hc.cState.facingRight;
        return right ? 0f : 180f;
    }

    private static int NailDamage() {
        try {
            var pd = Silksong::PlayerData.instance;
            if (pd != null && pd.nailDamage > 0) return pd.nailDamage;
        } catch {
        }

        return 5;
    }

    // Build once on an INACTIVE GameObject so PlayMakerFSM.Awake never runs (no empty-FSM PlayMaker log noise). We
    // return this object directly from the hooks, so it doesn't need to live on the slash; it only needs usable
    // FsmVariables with "direction"/"damageDealt". `new Fsm()` field-initialises Variables (non-null), so no Awake.
    private static void EnsureDummy() {
        if (dummy != null) return;
        var go = new GameObject("hp_damages_enemy_shim");
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);
        dummy = go.AddComponent<PlayMakerFSM>();
        var fsm = new Fsm { Name = "damages_enemy" };
        dirVar = new FsmFloat("direction");
        dmgVar = new FsmInt("damageDealt");
        fsm.Variables.FloatVariables = [dirVar];
        fsm.Variables.IntVariables = [dmgVar];
        typeof(PlayMakerFSM).GetField("fsm", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dummy, fsm);
    }

    internal static void Cleanup() {
        findHook?.Dispose();
        findHook = null;
        locateHook?.Dispose();
        locateHook = null;
        if (dummy != null) {
            Object.Destroy(dummy.gameObject);
            dummy = null;
            dirVar = null;
            dmgVar = null;
        }
    }

    private delegate PlayMakerFSM Orig(GameObject go, string fsmName);

    private delegate PlayMakerFSM Hooked(Orig orig, GameObject go, string fsmName);
}
