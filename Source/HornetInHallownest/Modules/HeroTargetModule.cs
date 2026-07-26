using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.Playground;
using HutongGames.PlayMaker;

namespace HornetPlayer.HornetInHallownest.Modules;

// Redirect HK's "where/who is the hero" to the active hero.
//   - PlayMaker global "Hero" var (most enemy/cutscene/UI FSMs)
//   - LineOfSightDetector + the GetHero action
//   - SoulOrb (a plain MonoBehaviour reading HeroController.instance directly) + soul->silk conversion
public sealed class HeroTargetModule : ModuleBase {
    public override string Id => "hero-target";

    public override void Initialize() {
        // Enemy targeting: LoS reads HeroController.instance (positional), so place the inert Knight at Hornet's spot for
        // the call, restore after. GetHero caches instance into a per-FSM local var, so rewrite its result.
        Detour(typeof(LineOfSightDetector), "Update", OnLosUpdate);
        Detour(typeof(GetHero), "OnEnter", OnGetHero);

        // SoulOrb (pooled) homes to HeroController.instance: Start caches it once at pool warmup (before Hornet exists),
        // OnEnable fires per fling, so retarget on both. AddMPCharge converts the soul grant to silk.
        Detour(typeof(SoulOrb), "Start", OnSoulOrbActivate);
        Detour(typeof(SoulOrb), "OnEnable", OnSoulOrbActivate);
        Detour(typeof(HeroController), "AddMPCharge", OnAddMpCharge, typeof(int));
    }

    protected override void OnDeinitialize() {
        // Leave HK coherent: point the global back at the Knight.
        if (heroVar != null && HeroController.UnsafeInstance != null)
            heroVar.Value = HeroController.UnsafeInstance.gameObject;
        heroVar = null;
    }

    #region Global "Hero" variable

    private static FsmGameObject? heroVar;

    // Point HK's PlayMaker global "Hero" at the active hero. HK re-binds it to its Knight on scene entry.
    public static void SyncGlobal() {
        if (heroVar == null) {
            var globals = PlayMakerGlobals.Instance;
            if (!globals) return;
            heroVar = globals.Variables.FindFsmGameObject("Hero");
            if (heroVar == null) {
                return;
            }
        }

        var target = HeroSwitch.ActiveHeroGameObject;
        if (target && heroVar.Value != target) heroVar.Value = target;

        // A hero-GO change leaves HK's cached local "Hero" vars pointing at the old GO; re-sync them.
        if (target && target != lastHeroSwept) {
            SyncLocalHeroVars(target);
            lastHeroSwept = target;
        }
    }

    private static UnityEngine.GameObject? lastHeroSwept;

    private static void SyncLocalHeroVars(UnityEngine.GameObject hero) {
        foreach (var fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(UnityEngine.FindObjectsSortMode.None)) {
            var v = fsm.FsmVariables?.FindFsmGameObject("Hero");
            if (v != null && v.Value != hero) v.Value = hero;
        }
    }

    #endregion

    #region Enemy targeting

    private void OnGetHero(Action<GetHero> orig, GetHero self) {
        orig(self); // sets storeResult to HeroController.instance (the Knight)
        if (self.storeResult != null && HeroSwitch.ActiveHeroGameObject is { } hero)
            self.storeResult.Value = hero;
    }

    private void OnLosUpdate(Action<LineOfSightDetector> orig, LineOfSightDetector self) {
        var knight = HeroController.UnsafeInstance;
        var hornet = HornetSpawner.Hornet;
        if (!HeroSwitch.HornetActive || !knight || !hornet) {
            orig(self);
            return;
        }

        var kt = knight.transform;
        var saved = kt.position;
        kt.position = hornet.transform.position;
        try {
            orig(self);
        } finally {
            kt.position = saved;
        }
    }

    #endregion

    #region Soul orbs -> silk

    private void OnSoulOrbActivate(Action<SoulOrb> orig, SoulOrb self) {
        orig(self);
        if (HeroSwitch.HornetActive && HornetSpawner.Hornet is { } hero)
            self.SetFieldValue("target", hero.transform);
    }

    private void OnAddMpCharge(Action<HeroController, int> orig, HeroController self, int amount) {
        if (HeroSwitch.HornetActive && HornetSpawner.Hornet is { } hero) {
            hero.AddSilk(1, true); // 1 silk per orb; skip the Knight's soul add
            return;
        }

        orig(self, amount);
    }

    #endregion
}
