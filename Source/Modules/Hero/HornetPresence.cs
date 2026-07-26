extern alias Silksong;
using UnityEngine;

namespace HornetInHallownest.Modules.Hero;

internal sealed class HornetPresence : HeroPresence {
    public override GameObject? Root =>
        HornetSpawner.Hornet ? HornetSpawner.Hornet.gameObject : null;

    protected override Behaviour? AnimCtrl(GameObject go) => go.GetComponent<Silksong::HeroAnimationController>();

    protected override void OnDeactivate(GameObject go) {
        // enabled is owned per-frame by HornetEnvironmentAdapter while she's active; turn it off so she goes inert.
        if (go.TryGetComponent<Silksong::HeroController>(out var hc)) hc.enabled = false;
    }

    protected override void OnActivate(GameObject go) {
        // Her own vignette stays off - the Knight's camera-parented vignette darkens the scene for both heroes.
        var v = go.transform.Find("Vignette");
        if (v) v.gameObject.SetActive(false);

        if (go.TryGetComponent<Silksong::HeroController>(out var hc)) {
            // While inert she missed HornetSceneEntry, so controlReqlinquished/acceptingInput stuck -> frozen on switch-in.
            // Force isHeroInPosition first (AcceptInput gates on it), then restore control + anim.
            hc.isHeroInPosition = true;
            hc.RegainControl();
            hc.StartAnimationControl();
            hc.enabled = true;
        }
    }
}
