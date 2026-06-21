extern alias Silksong;
using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using SGM = Silksong::GameManager;
using SFreeze = Silksong::GlobalEnums.FreezeMomentTypes;

namespace HornetPlayer.Playground;

// GameManager.FreezeMoment(FreezeMomentTypes) does `this.StartCoroutine(FreezeMoment(floats…))` — the hit-stop time-ramp.
// But our GM GO is kept INACTIVE (the seam: no GameManager.Awake/shadow-world), so every hit/kill logs
// "Coroutine couldn't be started because Silksong_GameManager is inactive". The freeze can't simply be hosted on an
// active GO either: its IEnumerator body nests `StartCoroutine(SetTimeScale(…))` on the GM again. The freeze is pure
// juice (brief global Time.timeScale dip) with no gameplay dependency, so we no-op it — but still invoke the `onFinish`
// callback so any caller that chains off the freeze (death sequences) doesn't hang. Both void overloads route through
// this 2-arg method (FreezeMoment(int) -> FreezeMoment((FreezeMomentTypes)type, null)).
internal static class FreezeMomentFix {
    private static Hook? hook;

    internal static void Install() {
        var mi = typeof(SGM).GetMethod("FreezeMoment", BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(SFreeze), typeof(Action)], null);
        if (mi == null) {
            Log.Error("[FreezeMomentFix] GameManager.FreezeMoment(FreezeMomentTypes,Action) not found");
            return;
        }

        hook = new Hook(mi, (Hooked)((orig, self, type, onFinish) => onFinish?.Invoke()));
        Log.Info(
            "[FreezeMomentFix] installed: GameManager.FreezeMoment no-op (+onFinish; inactive GM can't run its coroutine)");
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }

    private delegate void Orig(SGM self, SFreeze type, Action onFinish);

    private delegate void Hooked(Orig orig, SGM self, SFreeze type, Action onFinish);
}
