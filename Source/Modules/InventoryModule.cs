extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Modules;

// Silksong's "Inventory Control" FSM opens the inventory via CallMethodProper(GameManager.SetIsInventoryOpen). The call
// reaches our bootstrap GameManager but its real body -> SetPausedState derefs inactive singletons
// (gameCams/ui/GlobalSettings.Camera) and would NullRef. 
// TODO: reuse more upstream logic?
public sealed class InventoryModule : ModuleBase {
    private object? inputBlocker;
    private float prevTimeScale = 1f;

    public override string Id => "inventory";

    public override void Initialize() {
        Detour(typeof(Silksong::GameManager), "SetIsInventoryOpen", OnSetInventoryOpen);
    }

    protected override void OnDeinitialize() {
        // A hot-reload with the inventory open leaves the world frozen, HK's main camera off (DisplayFrozenCamera.Freeze)
        // and isInventoryOpen stuck (-> IsPaused -> CanInput false -> hero stuck). Undo all three.
        if (Time.timeScale == 0f) Time.timeScale = prevTimeScale;

        Silksong::PlayerData.instance.isInventoryOpen = false;

        var gameCameras = GameCameras.instance; // HK
        if (gameCameras && gameCameras.mainCamera && !gameCameras.mainCamera.enabled) gameCameras.mainCamera.enabled = true;

        inputBlocker = null;
    }

    private void OnSetInventoryOpen(Action<Silksong::GameManager, bool> orig, Silksong::GameManager self, bool value) {
        // Skip orig
        try {
            Silksong::PlayerData.instance.isInventoryOpen = value;

            if (value) {
                // Never remember a frozen scale as "previous", or closing would leave the world stuck.
                prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            } else {
                Time.timeScale = prevTimeScale;
            }

            var hero = HornetSpawner.Hornet;
            if (hero) {
                inputBlocker ??= new object();
                if (value) hero.AddInputBlocker(inputBlocker);
                else hero.RemoveInputBlocker(inputBlocker);
            }

            // Put HK into the input-suppressed state it uses for its own UI (isPaused + StopUIInput), so HK's ESC
            // pause-poll and every gm.isPaused-gated ListenFor* (incl. Bench get-up) stop behind the overlay.
            var hkGm = GameManager.instance;
            if (hkGm) {
                hkGm.isPaused = value;
                var hkIh = hkGm.GetComponent<InputHandler>();
                if (hkIh) {
                    if (value) hkIh.StopUIInput();
                    else hkIh.StartUIInput();
                }
            }
        } catch (Exception e) {
            LogError(e.ToString());
        }
    }
}
