extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine;
using SBossSceneController = Silksong::BossSceneController;

namespace HornetInHallownest.Modules;

// Apply Radiant/Ascended difficulty to hornet, drain her silk where pantheons drain the Knight's soul, and refill her
// tools once she leaves one.
public sealed class GodhomeDifficultyBridgeModule : ModuleBase {
    public override string Id => "boss-level-bridge";

    private static SBossSceneController? mirror;

    public override void Initialize() {
        Detour(typeof(BossSceneController), "Awake", OnAwake);
        Detour(typeof(BossSceneController), "OnDestroy", OnDestroy);
        Detour(typeof(BossSequenceController), nameof(BossSequenceController.ApplyBindings), OnApplyBindings);
        Detour(typeof(BossSequenceController), nameof(BossSequenceController.RestoreBindings), OnRestoreBindings);
    }

    private static void OnApplyBindings(Action orig) {
        orig();
        DrainSilk();
    }

    private static void OnRestoreBindings(Action orig) {
        orig();
        DrainSilk();
        BenchModule.RefillTools();
    }

    private static void DrainSilk() {
        if (HornetSpawner.Hornet is { } hero) hero.TakeSilk(Silksong::PlayerData.instance.silk);
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
