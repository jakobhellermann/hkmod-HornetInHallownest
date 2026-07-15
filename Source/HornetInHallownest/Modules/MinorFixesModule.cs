extern alias Silksong;
using System;
using System.Collections;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using UnityEngine;
using Object = UnityEngine.Object;
using SFreeze = Silksong::GlobalEnums.FreezeMomentTypes;

namespace HornetPlayer.HornetInHallownest.Modules;

// Small fixes for the intentionally inactive Silksong GameManager GO (the seam), whose StartCoroutine calls misfire:
//   - StartCoroutine: on an inactive GO Unity silently drops the coroutine (no log) — this breaks hazard respawn / death
//     sequences (GameManager.HazardRespawn -> StartCoroutine(hero.HazardRespawn())). Redirect those to our always-active
//     PlaygroundHost; the coroutine captures its own `this`, so it runs correctly — the host just pumps it per frame.
//   - FreezeMoment: the GM's hit-stop coroutine can't start on the inactive GM ("Coroutine couldn't be started" per hit).
//     Pure juice (a brief global timeScale dip), so no-op it — but still invoke onFinish so death-sequence chains don't hang.
public sealed class MinorFixesModule : ModuleBase {
    private MonoBehaviour? host;

    public override string Id => "minor-fixes";

    public override void Initialize() {
        host = Object.FindAnyObjectByType<PlaygroundHost>();
        Detour(typeof(MonoBehaviour), "StartCoroutine", OnStartCoroutine, typeof(IEnumerator));
        Detour(typeof(Silksong::GameManager), "FreezeMoment", OnFreezeMoment, typeof(SFreeze), typeof(Action));
    }

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
}
