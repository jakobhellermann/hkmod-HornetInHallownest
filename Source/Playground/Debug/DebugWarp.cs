extern alias Silksong;
using System.Collections;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Dev convenience: warp to a scene (+ optional position) so we don't have to run back to a test spot. `GameManager`,
// `HeroController.UnsafeInstance` are HK's (unprefixed); Hornet is the Silksong hero. See the dreamer-test-spot memory
// for the canonical target (RestingGrounds_04 @ ~46.25,7.57).
internal static class DebugWarp {
    // POST /warp?scene=X[&x=..&y=..]: HK scene transition into X, land in the room (fade in properly), then — if x/y
    // given — drop Hornet + the Knight at (x,y). Control is restored either way.
    //
    // We deliberately pass an EMPTY EntryGateName. HK's modern load path is `LoadSceneAdditive` -> `EnterHero(
    // additiveGateSearch: true)`, whose branches are: empty gate -> `LogError("No entry gate...") + FinishedEnteringScene()`
    // (enters PLAYING with control, but skips the fade -> screen stays black); non-empty-not-found -> `LogError(
    // "Searching... failed") + return` (the additive branch has NO `array[0]` fallback, so it never even finishes the
    // entry -> hero half-initialized). Empty is the lesser evil: the entry FINISHES, only the fade is missing — so we
    // lift the black ourselves with `GameManager.FadeSceneIn()` after the load settles. One harmless HK "No entry
    // gate..." LogError per warp is accepted for a debug route.
    internal static object Warp(string scene, float? x, float? y) {
        var gm = GameManager.instance;
        if (gm == null) return new { error = "GameManager not ready" };

        gm.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = scene,
            EntryGateName = "",
            Visualization = GameManager.SceneLoadVisualizations.Default,
            AlwaysUnloadUnusedAssets = true
        });

        var host = Object.FindAnyObjectByType<PlaygroundHost>();
        if (host != null) host.StartCoroutine(FinishWarp(scene, x, y));

        return new { warping = scene, x, y };
    }

    private static IEnumerator FinishWarp(string scene, float? x, float? y) {
        var t = 0f;
        while (t < 10f && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != scene) {
            t += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.6f); // let HK's entry settle before we override / fade

        if (x.HasValue && y.HasValue) {
            var pos = new Vector3(x.Value, y.Value, 0f);
            var hornet = BundleSpike.HornetRoot;
            if (hornet != null) hornet.transform.position = pos;
            var knight = HeroController.UnsafeInstance;
            if (knight != null) knight.transform.position = pos;
        }

        // The empty-gate entry finished but never faded in -> lift the black overlay ourselves.
        GameManager.instance?.FadeSceneIn();

        var hero = BundleSpike.Hornet;
        if (hero != null) {
            hero.RegainControl();
            hero.AcceptInput();
        }

        Log.Debug($"[Warp] arrived at '{scene}'{(x.HasValue ? $" placed ({x}, {y})" : "")}, faded in");
    }

    internal static float? ParseFloat(string? s) {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
