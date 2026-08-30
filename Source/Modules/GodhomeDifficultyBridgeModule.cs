extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine;
using SBossSceneController = Silksong::BossSceneController;

namespace HornetInHallownest.Modules;

// Apply Radiant/Ascended difficulty to hornet.
public sealed class GodhomeDifficultyBridgeModule : ModuleBase {
    public override string Id => "boss-level-bridge";

    private static SBossSceneController? mirror;

    public override void Initialize() {
        Detour(typeof(BossSceneController), "Awake", OnAwake);
        Detour(typeof(BossSceneController), "OnDestroy", OnDestroy);
    }

    private static void OnAwake(Action<BossSceneController> orig, BossSceneController self) {
        orig(self);
        EnsureMirror();
        mirror!.BossLevel = self.BossLevel;
        SBossSceneController.Instance = mirror;
    }

    private static void OnDestroy(Action<BossSceneController> orig, BossSceneController self) {
        orig(self);
        if (!BossSceneController.Instance) SBossSceneController.Instance = null;
    }

    private static void EnsureMirror() {
        if (mirror) return;
        var go = new GameObject("Silksong_BossSceneController_Mirror");
        go.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(go);
        mirror = go.AddComponent<SBossSceneController>();
    }
}
