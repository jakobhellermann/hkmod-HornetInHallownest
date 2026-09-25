extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using HornetInHallownest.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using SFreeze = Silksong::GlobalEnums.FreezeMomentTypes;
using SFsm = SilksongPM::HutongGames.PlayMaker.Fsm;
using SFsmState = SilksongPM::HutongGames.PlayMaker.FsmState;

namespace HornetInHallownest.Modules;

// Small fixes for the intentionally inactive Silksong GameManager GO (the seam), whose StartCoroutine calls misfire:
//   - StartCoroutine: on an inactive GO Unity silently drops the coroutine (no log), breaking hazard respawn / death
//     sequences (GameManager.HazardRespawn -> StartCoroutine(hero.HazardRespawn())). Redirect those to our always-active
//     HornetRuntime; the coroutine captures its own `this`, so it runs correctly (the host just pumps it per frame).
//   - FreezeMoment: the GM's hit-stop coroutine can't start on the inactive GM ("Coroutine couldn't be started" per hit).
//     Pure juice (a brief global timeScale dip), so no-op it, but still invoke onFinish so death-sequence chains don't hang.
public sealed class MinorFixesModule : ModuleBase {
    private MonoBehaviour? host;

    public override string Id => "minor-fixes";

    public override void Initialize() {
        host = Object.FindObjectOfType<HornetRuntime>();
        Detour(typeof(MonoBehaviour), "StartCoroutine", OnStartCoroutine, typeof(IEnumerator));
        Detour(typeof(Silksong::GameManager), "FreezeMoment", OnFreezeMoment, typeof(SFreeze), typeof(Action));
        Detour(typeof(Silksong::HeroController), "RelinquishControl", OnRelinquishControl);
        Detour(typeof(Silksong::HeroController), nameof(Silksong::HeroController.PreventCastByDialogueEnd),
            OnPreventCastByDialogueEnd);
        Detour(typeof(IgnoreHeroCollision), "Ignore", OnIgnoreHeroCollision);
        Detour(typeof(SFsm), "SwitchState", OnSilksongSwitchState, typeof(SFsmState));
    }

    // A bind interrupted by damage goes through "Cancel All", which, unlike finishing it ("Shaman End?") or cancelling it
    // mid-air ("Shaman Air Cancel"), never plays the Shaman crest halo's "End", so the halo stays on Hornet.
    private static void OnSilksongSwitchState(Action<SFsm, SFsmState> orig, SFsm self, SFsmState to) {
        orig(self, to);
        if (self.Name != "Bind" || to.Name != "Cancel All") return;
        var halo = self.GameObject.transform.Find("Bind Effects/shaman_focus_halo");
        if (!halo || !halo.gameObject.activeSelf || !halo.TryGetComponent<Animator>(out var animator)) return;
        if (!animator.GetCurrentAnimatorStateInfo(0).IsName("End")) animator.Play("End");
    }

    #region Sprint + cutscene RelinquishControl

    // RelinquishControl does nothing if control is already relinquished (e.g. during dash), so ResetMotion never ran
    // for the abyss exit cutscene -> camera follows Hornet into nirvana.
    private static void OnRelinquishControl(Action<Silksong::HeroController> orig, Silksong::HeroController self) {
        if (self.cState is { isSprinting: true, dead: false })
            self.acceptingInput = true;
        orig(self);
    }

    #endregion

    private Coroutine OnStartCoroutine(Func<MonoBehaviour, IEnumerator, Coroutine> orig, MonoBehaviour self,
        IEnumerator? routine) {
        if (routine == null) return orig(self, null!);
        // Redirect only when the calling GO is inactive; normal calls pass through untouched.
        if (!host || (self && self.gameObject && self.gameObject.activeInHierarchy)) return orig(self, routine);
        return orig(host, routine);
    }

    private static void OnFreezeMoment(Action<Silksong::GameManager, SFreeze, Action> orig,
        Silksong::GameManager self, SFreeze type, Action? onFinish) {
        onFinish?.Invoke();
    }

    private static void
        OnPreventCastByDialogueEnd(Action<Silksong::HeroController> orig, Silksong::HeroController self) {
        // Silksong never calls the method, and never decrements the resulting timer, leading to cast/taunt being broken
        // after HK calls this on dialogue close.
    }

    // IgnoreHeroCollision unsubscribes from the Knight's heroInPosition only after Ignore succeeds. A handler left over
    // from a destroyed object throws inside HeroController.EnterScene, which aborts the scene entry on a black screen.
    private static void OnIgnoreHeroCollision(Action<IgnoreHeroCollision> orig, IgnoreHeroCollision self) {
        if (self) orig(self);
    }
}
