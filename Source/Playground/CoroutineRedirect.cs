using System.Collections;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// General fix for the "StartCoroutine on inactive GO" problem.
//
// The Silksong GameManager GO is intentionally inactive (no heavy Awake/SetupGameRefs). But several Silksong
// methods call StartCoroutine on it (e.g. GameManager.HazardRespawn → EnterHero → StartCoroutine(hero_ctrl.HazardRespawn())).
// Unity silently refuses to start coroutines on inactive GameObjects — no error, no log, the coroutine just
// never runs. This manifests as "hazard respawn never happens", "death sequence stalls", etc.
//
// Fix: hook MonoBehaviour.StartCoroutine(IEnumerator). When the calling MonoBehaviour's GO is inactive,
// redirect the coroutine to our active PlaygroundHost (a DontDestroyOnLoad GO that's always active). The
// coroutine's internal state captures its own `this` (the real Silksong component), so it runs correctly —
// the host just provides the per-frame pump. Only fires on inactive GOs; normal calls are untouched.
internal static class CoroutineRedirect {
    private static Hook? hook;
    private static MonoBehaviour? host;

    internal static void Install() {
        host = Object.FindAnyObjectByType<PlaygroundHost>();
        if (host == null) {
            var go = new GameObject("HornetPlayer.CoroutineHost");
            host = go.AddComponent<PlaygroundHost>();
            Object.DontDestroyOnLoad(go);
        }

        var mi = typeof(MonoBehaviour).GetMethod("StartCoroutine",
            BindingFlags.Public | BindingFlags.Instance, null, [typeof(IEnumerator)], null);
        if (mi == null) {
            Log.Error("[CoroutineRedirect] MonoBehaviour.StartCoroutine(IEnumerator) not found");
            return;
        }

        hook = new Hook(mi, (HookedDel)RedirectCoroutine);
        Log.Debug("[CoroutineRedirect] installed: MonoBehaviour.StartCoroutine");
    }

    private static Coroutine RedirectCoroutine(OrigDel orig, MonoBehaviour self, IEnumerator routine) {
        if (routine == null) return orig(self, null!);
        // Only redirect when the calling GO is inactive — normal calls pass through untouched.
        if (self != null && self.gameObject != null && self.gameObject.activeInHierarchy) return orig(self, routine);
        // Inactive GO: Unity silently drops the coroutine. Redirect to our active host.
        return orig(host!, routine);
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }

    private delegate Coroutine OrigDel(MonoBehaviour self, IEnumerator routine);

    private delegate Coroutine HookedDel(OrigDel orig, MonoBehaviour self, IEnumerator routine);
}
