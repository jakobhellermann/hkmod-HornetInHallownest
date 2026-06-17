namespace Silksong;

// Shim: HK has a CheatManager but not Silksong's `UseFieldAccessOptimisers` toggle. Because the decompiled
// HeroControllerStates lives in `namespace Silksong`, this same-namespace type wins name resolution over HK's global
// CheatManager. Keep it `true` so GetState/SetState route through BoolFieldAccessOptimizer (which operates on the
// correct `this` instance); the `false` fallback in the decompiled code dereferences HK's HeroController.instance.cState
// (wrong object) — it still compiles but we don't want it executed.
public static class CheatManager {
    public static bool UseFieldAccessOptimisers = true;
}
