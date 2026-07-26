extern alias Silksong;
using System.Collections;
using System.Globalization;
using System.Linq;
using GlobalEnums;
using HornetInHallownest.Core;
using HornetInHallownest.Modules;
using HornetInHallownest.Util;
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using UScene = UnityEngine.SceneManagement.Scene;
using LoadSceneMode = UnityEngine.SceneManagement.LoadSceneMode;

namespace HornetInHallownest.Playground;

// POST /warp?scene=X[&x=..&y=..] through HK's "dreamGate" entry; without x/y onto the room's first HazardRespawnMarker.
internal static class DebugWarp {
    private static Vector2? target;

    internal static object Warp(string scene, float? x, float? y) {
        var gm = GameManager.instance;
        if (gm == null) return new { error = "GameManager not ready" };

        target = x.HasValue && y.HasValue ? new Vector2(x.Value, y.Value) : null;
        USceneManager.sceneLoaded += PlaceKnight;
        gm.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = scene,
            EntryGateName = "dreamGate",
            Visualization = GameManager.SceneLoadVisualizations.Default,
            AlwaysUnloadUnusedAssets = true
        });
        ModuleBase.Runtime.StartCoroutine(FinishWarp(scene, (PlayerData.instance.dreamGateX, PlayerData.instance.dreamGateY)));

        return new { warping = scene, x, y };
    }

    // The speedrun DebugMod's FindEntryPoint patch keeps the Knight in place on a "dreamGate" entry, so move him too.
    private static void PlaceKnight(UScene to, LoadSceneMode mode) {
        USceneManager.sceneLoaded -= PlaceKnight;

        // Gate markers can sit outside the room, e.g. Crossroads_01's right1.
        var roots = to.GetRootGameObjects();
        var tileMap = roots.Select(go => go.GetComponentInChildren<tk2dTileMap>()).First(t => t);
        var room = new Rect(0, 0, tileMap.width, tileMap.height);
        var pos = target ?? roots
            .SelectMany(go => go.GetComponentsInChildren<HazardRespawnMarker>())
            .Select(marker => (Vector2?)marker.transform.position)
            .FirstOrDefault(p => room.Contains(p!.Value));
        if (pos is not { } p) {
            Log.Error($"[Warp] no HazardRespawnMarker inside '{to.name}', pass x and y");
            return;
        }

        var pd = PlayerData.instance;
        (pd.dreamGateX, pd.dreamGateY) = (p.x, p.y);
        HeroController.UnsafeInstance!.transform.position = p;
    }

    private static IEnumerator FinishWarp(string scene, (float x, float y) dreamGate) {
        var knight = HeroController.UnsafeInstance!;
        var timeout = Time.realtimeSinceStartup + 60f;
        while (knight.transitionState == HeroTransitionState.WAITING_TO_TRANSITION) yield return null;
        while (USceneManager.GetActiveScene().name != scene || knight.transitionState != HeroTransitionState.WAITING_TO_TRANSITION) {
            if (Time.realtimeSinceStartup > timeout) {
                Log.Error($"[Warp] never arrived at '{scene}': active scene {USceneManager.GetActiveScene().name}, " +
                          $"knight {knight.transitionState}");
                yield break;
            }

            yield return null;
        }

        var pd = PlayerData.instance;
        Log.Debug($"[Warp] arrived at '{scene}' {knight.transform.position}");
        (pd.dreamGateX, pd.dreamGateY) = dreamGate;

        // Warping out of Godhome leaves its white blanker faded in.
        GameObject.Find("Blanker White").GetComponent<PlayMakerFSM>().SendEvent("RESET");

        // A dream gate arrival stays faded out and leaves the Knight without gravity and control.
        GameManager.instance.FadeSceneIn();
        if (HeroSwitch.HornetActive) yield break;
        knight.AffectedByGravity(true);
        knight.RegainControl();
        knight.StartAnimationControl();
    }

    internal static float? ParseFloat(string? s) {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
