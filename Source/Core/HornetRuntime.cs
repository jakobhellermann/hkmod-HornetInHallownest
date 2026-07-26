extern alias Silksong;
using System;
using UnityEngine;
using HornetInHallownest.Modules;
using HornetInHallownest.Util;

namespace HornetInHallownest.Core;

// Per frame logic and coroutine host.
[DefaultExecutionOrder(-1000)] // before HeroController.Update
public sealed class HornetRuntime : MonoBehaviour {
    private bool wasHornetActive;

    private void Update() {
        try {
            var paused = ModuleBase.Paused;
            var hero = HornetSpawner.Hornet;
            var active = hero && HeroSwitch.HornetActive;

            // Mirror HK's pause onto Silksong's GM so her input pipeline freezes with it
            var gm = Silksong::GameManager._instance;
            if (gm) {
                gm.SetFieldValue("<GameState>k__BackingField",
                    paused ? Silksong::GlobalEnums.GameState.PAUSED : Silksong::GlobalEnums.GameState.PLAYING);
            }

            if (active != wasHornetActive) {
                wasHornetActive = active;
                HornetInHallownestMod.LoadedInstance?.Modules.HornetToggled(active);
            }

            if (!active) return;

            // Her HeroController.Start disabled itself + set isGameplayScene false (non-gameplay path); re-assert so
            // Unity keeps ticking her Update.
            if (!paused) {
                if (!hero!.enabled) hero.enabled = true;
                hero.SetFieldValue("isGameplayScene", true);
            }

            // Pump the modules. Also while the inventory is open (world frozen, but input must still reach it); a full
            // pause menu (paused && !inventoryOpen) skips it.
            if (!paused || Silksong::PlayerData.instance.isInventoryOpen) {
                HornetInHallownestMod.LoadedInstance?.Modules.HornetActiveUpdate(hero!);
            }
        } catch (Exception e) {
            Log.Error($"[Runtime] {e}");
        }
    }
}
