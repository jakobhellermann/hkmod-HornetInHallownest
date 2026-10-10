extern alias Silksong;
using System;
using GlobalEnums;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace HornetInHallownest.Modules;

// Redirect HK's "where/who is the hero" to the active hero.
//   - the inert Knight's position, kept on Hornet (enemy line of sight, the dark-room vignette and light on his children,
//     Gathering Swarm geo and the map compass all read HeroController.instance's position)
//   - PlayMaker global "Hero" var (most enemy/cutscene/UI FSMs)
//   - the GetHero action
//   - SoulOrb (a plain MonoBehaviour reading HeroController.instance directly) + soul->silk conversion
public sealed class HeroTargetModule : ModuleBase {
    private const int SoulPerSilk = 11;

    private int unspentSoul;

    public override string Id => "hero-target";

    public override void Initialize() {
        // GetHero caches instance into a per-FSM local var, so rewrite its result.
        Detour(typeof(GetHero), "OnEnter", OnGetHero);

        // SoulOrb (pooled) homes to HeroController.instance: Start caches it once at pool warmup (before Hornet exists),
        // OnEnable fires per fling, so retarget on both. AddMPCharge converts the soul to silk.
        Detour(typeof(SoulOrb), "Start", OnSoulOrbActivate);
        Detour(typeof(SoulOrb), "OnEnable", OnSoulOrbActivate);
        Detour(typeof(HeroController), "AddMPCharge", OnAddMpCharge, typeof(int));

        // The camera follows Hornet's transform (RetargetCamera) but reads the hero's state off HeroController.instance,
        // the inert Knight whose cState froze on deactivation: a frozen `falling` glues the camera to her Y (fallStick),
        // look up/down never triggers, the look-ahead keeps his facing. Mirror her state onto him for the call, and her
        // position too: CameraController's look offset is relative to hero_ctrl.transform. Leaving a lock zone reads his
        // transitionState, still mid-entry after a door while she already moves, and stops following her for good.
        Detour(typeof(CameraTarget), "Update", (Action<Action<CameraTarget>, CameraTarget>)OnCameraUpdate);
        Detour(typeof(CameraTarget), "ExitLockZone", (Action<Action<CameraTarget>, CameraTarget>)OnCameraUpdate);
        Detour(typeof(CameraController), "LateUpdate",
            (Action<Action<CameraController>, CameraController>)OnCameraUpdate);

        Detour(typeof(GetPosition), "DoGetPosition", OnGetPosition);
        Detour(typeof(GetPosition2D), "DoGetPosition", OnGetPosition2D);
        Detour(typeof(SetPosition), "DoSetPosition", OnSetPosition);
        Detour(typeof(SetPositionToObject), "DoSetPosition", OnSetPositionToObject);
    }

    #region Position in the Knight's terms

    // HK's cutscenes gate landings on the hero's height and place him at points measured from the Knight's origin, which
    // rests FeetDelta lower above a floor than Hornet's.
    private static void OnGetPosition(Action<GetPosition> orig, GetPosition self) {
        orig(self);
        if (!IsHornet(self.Fsm.GetOwnerDefaultTarget(self.gameObject))) return;
        var delta = FeetDelta();
        self.vector.Value -= new Vector3(0f, delta, 0f);
        self.y.Value -= delta;
    }

    private static void OnGetPosition2D(Action<GetPosition2D> orig, GetPosition2D self) {
        orig(self);
        if (!IsHornet(self.Fsm.GetOwnerDefaultTarget(self.gameObject))) return;
        var delta = FeetDelta();
        self.vector.Value -= new Vector2(0f, delta);
        self.y.Value -= delta;
    }

    private static void OnSetPosition(Action<SetPosition> orig, SetPosition self) {
        orig(self);
        var target = self.Fsm.GetOwnerDefaultTarget(self.gameObject);
        if ((self.vector.IsNone && self.y.IsNone) || !IsHornet(target)) return;
        target.transform.position += new Vector3(0f, FeetDelta(), 0f);
    }

    private static void OnSetPositionToObject(Action<SetPositionToObject> orig, SetPositionToObject self) {
        orig(self);
        var target = self.Fsm.GetOwnerDefaultTarget(self.gameObject);
        if (!target) return;
        if (IsHornet(target)) target.transform.position += new Vector3(0f, FeetDelta(), 0f);
        else if (IsHornet(self.targetObject.Value)) target.transform.position -= new Vector3(0f, FeetDelta(), 0f);
    }

    private static bool IsHornet(GameObject? go) =>
        go && HeroSwitch.HornetActive && go == HornetSpawner.HornetRoot;

    // How much higher Hornet's origin rests on a floor than the Knight's.
    internal static float FeetDelta() {
        var hornetCol = HornetSpawner.TerrainCollider;
        var knight = HeroController.UnsafeInstance;
        var knightCol = knight != null ? knight.GetComponent<BoxCollider2D>() : null;
        if (hornetCol == null || knightCol == null) return 0f;
        var hornetBottom = hornetCol.offset.y - hornetCol.size.y / 2f;
        var knightBottom = knightCol.offset.y - knightCol.size.y / 2f;
        return knightBottom - hornetBottom;
    }

    #endregion

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
        if (target && target != sweptHero) {
            SyncLocalHeroVars(target);
            sweptHero = target;
        }
    }

    private static GameObject? sweptHero;

    // Variables that get set to the hero game object.
    private static readonly string[] heroVarNames = ["Hero", "Hero Obj", "Hero Object"];

    private static void SyncLocalHeroVars(GameObject hero) {
        foreach (var fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None)) {
            var vars = fsm.FsmVariables;
            if (vars == null) continue;
            foreach (var name in heroVarNames) {
                var v = vars.FindFsmGameObject(name);
                if (v != null && v.Value != hero) v.Value = hero;
            }
        }
    }

    #endregion

    #region Knight position

    // Runs before HK's scripts read the position this frame. Skipped while HK places the Knight itself (scene entry,
    // respawn): the entry and revive code then move Hornet onto him. Once he's placed, HK plays out the rest of his
    // entry on timers after hers has ended, so he follows her from then on.
    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        var knight = HeroController.UnsafeInstance;
        var gm = GameManager.UnsafeInstance;
        if (HeroSwitch.BothActive || !knight || knight.enabled || !gm) return;
        if (gm.gameState is not (GameState.PLAYING or GameState.ENTERING_LEVEL)) return;
        if (gm.RespawningHero || hero.cState.dead || !knight.isHeroInPosition) return;
        if (hero.transitionState != Silksong::GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION) return;

        var kt = knight.transform;
        var position = hero.transform.position;
        kt.position = new Vector3(position.x, position.y, kt.position.z);
    }

    #endregion

    #region Enemy targeting

    private void OnGetHero(Action<GetHero> orig, GetHero self) {
        orig(self); // sets storeResult to HeroController.instance (the Knight)
        if (self.storeResult != null && HeroSwitch.ActiveHeroGameObject is { } hero)
            self.storeResult.Value = hero;
    }

    #endregion

    #region Camera

    private void OnCameraUpdate<T>(Action<T> orig, T self) {
        var knight = HeroController.UnsafeInstance;
        var hornet = HornetSpawner.Hornet;
        if (!HeroSwitch.HornetActive || !knight || knight.enabled || !hornet) {
            orig(self);
            return;
        }

        var kt = knight.transform;
        var ks = knight.cState;
        var hs = hornet.cState;
        var savedPosition = kt.position;
        var savedVelocity = knight.current_velocity;
        var savedTransitionState = knight.transitionState;
        var (falling, lookingUp, lookingDown, facingRight, dashing) =
            (ks.falling, ks.lookingUp, ks.lookingDown, ks.facingRight, ks.dashing);

        kt.position = hornet.transform.position;
        knight.current_velocity = hornet.current_velocity;
        knight.transitionState = (HeroTransitionState)hornet.transitionState;
        // Silksong sets `falling` at zero vertical velocity too (hanging on a ledge); HK only while moving down, and its
        // fall catch would yank the camera down to her.
        (ks.falling, ks.lookingUp, ks.lookingDown, ks.facingRight, ks.dashing) =
            (hs.falling && hornet.current_velocity.y < 0f, hs.lookingUp, hs.lookingDown, hs.facingRight, hs.dashing);
        try {
            orig(self);
        } finally {
            kt.position = savedPosition;
            knight.current_velocity = savedVelocity;
            knight.transitionState = savedTransitionState;
            (ks.falling, ks.lookingUp, ks.lookingDown, ks.facingRight, ks.dashing) =
                (falling, lookingUp, lookingDown, facingRight, dashing);
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
            unspentSoul += amount;
            var silk = unspentSoul / SoulPerSilk;
            unspentSoul %= SoulPerSilk;
            if (silk > 0) hero.AddSilk(silk, true);
            return;
        }

        orig(self, amount);
    }

    #endregion
}
