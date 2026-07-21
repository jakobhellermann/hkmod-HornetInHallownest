using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Modules.Hero;

// Shared logic for hero activate/deactivate.
internal abstract class HeroPresence {
    public abstract GameObject? Root { get; }

    public void Activate() {
        var go = Root;
        if (!go) return;
        SetSimulated(go, true);
        SetAnimating(go, true);
        SetVisible(go, true);
        OnActivate(go);
    }

    public void Deactivate() {
        var go = Root;
        if (!go) return;
        OnDeactivate(go);
        SetVisible(go, false);
        SetAnimating(go, false);
        SetSimulated(go, false);
    }

    // Re-hide while inert: HK re-enables the hero's renderer on scene entry, so this re-asserts it's hidden afterward.
    public void ReassertHidden() {
        var go = Root;
        if (go) SetVisible(go, false);
    }

    protected virtual void OnActivate(GameObject go) {
    }

    protected virtual void OnDeactivate(GameObject go) {
    }

    // The hero's HeroAnimationController - a different type per game, so each presence resolves it.
    protected abstract Behaviour? AnimCtrl(GameObject go);

    private static void SetSimulated(GameObject go, bool on) {
        if (go.TryGetComponent<Rigidbody2D>(out var rb)) rb.simulated = on;
    }

    // The tk2d body is one MeshRenderer on the root, so toggling it hides/shows the whole hero.
    private static void SetVisible(GameObject go, bool visible) {
        if (go.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = visible;
    }

    // The driver must stop first: it Play()s every frame and derefs the animator's current clip. Pausing (not disabling)
    // the tk2d animator keeps that clip valid while frozen.
    private void SetAnimating(GameObject go, bool on) {
        var driver = AnimCtrl(go);
        if (!on && driver) driver.enabled = false;
        foreach (var a in go.GetComponentsInChildren<tk2dSpriteAnimator>(true))
            if (on) a.Resume();
            else a.Pause();
        foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = on;
        if (on && driver) driver.enabled = true;
    }
}
