extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Util;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SHeroController = Silksong::HeroController;

namespace HornetPlayer.Playground;

// The world (HK) owns respawn: HornetDeath routes death through HK's GameManager.PlayerDead, which reads HK's
// PlayerData. But cross-game scene FSMs set the respawn via CallMethodProper on the "Hero" global -> Hornet's Silksong
// HeroController -> Silksong's PlayerData, which the death path never reads. The Vengeful-Spirit hard save in
// Crossroads_ShamanTemple ("Check Fall"/"Set Respawns") is the first case: it calls SetBenchRespawn behind a gate, but
// HK's PlayerData keeps the last bench (wrong side of the gate) -> dying there sequence-breaks.
//
// Mirror Silksong's SetBenchRespawn/SetHazardRespawn onto HK's HeroController so HK's PlayerData — the source of truth
// the death respawn uses — stays in sync. Hornet uses HK benches (not Silksong's), so Silksong's setters are only ever
// hit by these cross-game FSM calls; mirroring unconditionally is correct.
internal static class RespawnBridge {
    private static Hook? benchHook;
    private static Hook? hazardHook;

    internal static void Install() {
        var bench = typeof(SHeroController).GetMethodInfo("SetBenchRespawn",
            [typeof(string), typeof(string), typeof(int), typeof(bool)]);
        if (bench != null)
            benchHook = new Hook(bench,
                (Action<Action<SHeroController, string, string, int, bool>, SHeroController, string, string, int, bool>)
                OnSetBenchRespawn);

        var hazard = typeof(SHeroController).GetMethodInfo("SetHazardRespawn", [typeof(Vector3), typeof(bool)]);
        if (hazard != null)
            hazardHook = new Hook(hazard,
                (Action<Action<SHeroController, Vector3, bool>, SHeroController, Vector3, bool>)OnSetHazardRespawn);
    }

    private static void OnSetBenchRespawn(Action<SHeroController, string, string, int, bool> orig,
        SHeroController self, string marker, string scene, int type, bool facingRight) {
        orig(self, marker, scene, type, facingRight);
        var hk = HeroController.UnsafeInstance;
        if (hk == null) return;
        hk.SetBenchRespawn(marker, scene, type, facingRight);
        Log.Info($"[RespawnBridge] mirrored SetBenchRespawn -> HK PlayerData: {scene}/{marker} type={type}");
    }

    private static void OnSetHazardRespawn(Action<SHeroController, Vector3, bool> orig,
        SHeroController self, Vector3 pos, bool facingRight) {
        orig(self, pos, facingRight);
        HeroController.UnsafeInstance?.SetHazardRespawn(pos, facingRight);
    }

    internal static void Cleanup() {
        benchHook?.Dispose();
        benchHook = null;
        hazardHook?.Dispose();
        hazardHook = null;
    }
}
