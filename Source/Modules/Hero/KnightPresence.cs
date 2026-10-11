using System.Collections.Generic;
using UnityEngine;

namespace HornetInHallownest.Modules.Hero;

internal sealed class KnightPresence : HeroPresence {
    private readonly HashSet<PlayMakerFSM> disabledByUs = [];
    private readonly HashSet<Renderer> hiddenByUs = [];

    public override GameObject? Root =>
        HeroController.UnsafeInstance ? HeroController.UnsafeInstance.gameObject : null;

    protected override Behaviour? AnimCtrl(GameObject go) => go.GetComponent<HeroAnimationController>();

    protected override void OnDeactivate(GameObject go) {
        PlayMakerFSM? vignetteFsm = null;
        Transform? vignette = null;
        Renderer? heroLight = null;
        if (go.TryGetComponent<HeroController>(out var hk)) {
            hk.enabled = false;
            vignetteFsm = hk.vignetteFSM;
            vignette = hk.vignette ? hk.vignette.transform : null;
            heroLight = hk.heroLight;
        }

        // Disable every FSM on the Knight (root + children): the ability FSMs (Superdash/Spell Control/Nail Arts) listen
        // for input independently of HeroController.enabled and would fire on shared input; the rest target the PlayMaker
        // "Hero" global (repointed to Hornet) and would act on her. Remember only what we actually disabled, so restore
        // doesn't re-enable FSMs the game itself had off. The vignette's Darkness Control stays on: HK's scene setup
        // darkens dark rooms through it, for both heroes. So does Spawn Grimmchild, which spawns the Grimmchild at the
        // "Hero" global, Hornet, while her Grimmchild tool is on.
        foreach (var fsm in go.GetComponentsInChildren<PlayMakerFSM>(true))
            if (fsm.enabled && fsm != vignetteFsm && fsm.FsmName != "Spawn Grimmchild") {
                fsm.enabled = false;
                disabledByUs.Add(fsm);
            }

        // An effect playing at the switch freezes mid-animation (his animators are paused) and never hides itself, and he
        // stands on Hornet, so hide his child renderers too (Monarch Wings stuck behind her). The vignette and his light
        // stay: they light dark rooms for both heroes.
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.enabled && r.gameObject != go && r != heroLight && !(vignette && r.transform.IsChildOf(vignette))) {
                r.enabled = false;
                hiddenByUs.Add(r);
            }
    }

    protected override void OnActivate(GameObject go) {
        foreach (var fsm in disabledByUs)
            if (fsm)
                fsm.enabled = true;
        disabledByUs.Clear();

        foreach (var r in hiddenByUs)
            if (r)
                r.enabled = true;
        hiddenByUs.Clear();

        if (go.TryGetComponent<HeroController>(out var hk)) {
            hk.enabled = true;
            // A Hornet-driven transition relinquished the Knight (controlReqlinquished set at transition start), but being
            // inert its EnterScene never reached the closing RegainControl -> the flag sticks and blocks jump/abilities.
            // Restore control (near-no-op if already held) + re-run StartControl (its closing call never ran either, so
            // the anim controller stays frozen on the entry clip).
            hk.RegainControl();
            hk.GetComponent<HeroAnimationController>()?.StartControl();
        }

        // HK leaves the relinquished Knight's Rigidbody Kinematic; base re-enabled simulation but not the body type, so
        // without this he floats. Restore Dynamic so he falls normally.
        if (go.TryGetComponent<Rigidbody2D>(out var rb)) rb.bodyType = RigidbodyType2D.Dynamic;
    }
}
