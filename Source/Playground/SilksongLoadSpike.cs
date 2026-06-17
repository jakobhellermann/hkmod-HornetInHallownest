using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// De-risk B2 (whole-graph): does the IL-prefixed Silksong assembly set (Assembly-CSharp + firstpass, both Silksong.*)
// LOAD in HK's Mono at runtime? Touch types that previously cascaded into firstpass (GameManager -> GameCameras,
// PlayMaker iTween actions) and AddComponent HeroController onto an inactive GO.
internal static class SilksongLoadSpike {
    private static GameObject? host;

    internal static void Run() {
        Try("typeof HeroController", () => typeof(Silksong.HeroController));
        Try("typeof GameManager", () => typeof(Silksong.GameManager));   // failed in the single-DLL attempt
        Try("typeof GameCameras", () => typeof(Silksong.GameCameras));
        Try("typeof PlayerData", () => typeof(Silksong.PlayerData));

        try {
            host = new GameObject("SilksongLoadSpike");
            host.SetActive(false);
            var comp = host.AddComponent<Silksong.HeroController>();
            Log.Info($"[SilksongLoad] AddComponent<Silksong.HeroController> OK — non-null={comp != null}");
        } catch (Exception e) {
            Log.Error($"[SilksongLoad] AddComponent FAILED: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Try(string what, Func<Type> f) {
        try {
            var t = f();
            Log.Info($"[SilksongLoad] {what} OK — {t.FullName} in {t.Assembly.GetName().Name}");
        } catch (Exception e) {
            Log.Error($"[SilksongLoad] {what} FAILED: {e.GetType().Name}: {e.Message}");
        }
    }

    internal static void Cleanup() {
        if (host != null) { Object.Destroy(host); host = null; }
    }
}
