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

    // HK's own inventory, which Hornet reaches through its map shortcut (double-tapping quick map).
    internal static bool HkInventoryOpen { get; private set; }

    public override void Initialize() {
        Detour(typeof(Silksong::GameManager), "SetIsInventoryOpen", OnSetInventoryOpen);
        Detour(typeof(GameManager), "SetIsInventoryOpen", OnSetHkInventoryOpen);
        // Silksong freezes the view when the inventory opens: renders Camera.main into a texture shown on a plane sized
        // from ITS camera (a disabled stand-in here -> the "game squeezes" frame), blurs it with a multi-pass shader
        // Unity 2020 can't run, and switches the real camera off. The world is already paused (timeScale 0), so skip
        // it and let the paused game keep rendering behind the inventory - a true overlay.
        Detour(typeof(Silksong::DisplayFrozenCamera), "Freeze",
            (Action<Action<Silksong::DisplayFrozenCamera>, Silksong::DisplayFrozenCamera>)((_, _) => { }));
        Detour(typeof(Silksong::DisplayFrozenCamera), "Unfreeze",
            (Action<Action<Silksong::DisplayFrozenCamera>, Silksong::DisplayFrozenCamera>)((_, _) => { }));
    }

    private static void OnSetHkInventoryOpen(Action<GameManager, bool> orig, GameManager self, bool value) {
        HkInventoryOpen = value;
        orig(self, value);
    }

    protected override void OnDeinitialize() {
        HkInventoryOpen = false;
        // A hot-reload with the inventory open leaves the world frozen, HK's main camera off (DisplayFrozenCamera.Freeze)
        // and isInventoryOpen stuck (-> IsPaused -> CanInput false -> hero stuck).
        if (Time.timeScale == 0f) Time.timeScale = prevTimeScale;

        Silksong::PlayerData.instance.isInventoryOpen = false;

        var gameCameras = GameCameras.instance; // HK
        if (gameCameras && gameCameras.mainCamera && !gameCameras.mainCamera.enabled)
            gameCameras.mainCamera.enabled = true;

        // prevent knight being softlocked after hot reload in hornet inventory
        var hkGm = GameManager.instance;
        if (hkGm) {
            hkGm.isPaused = false;
            var hkIh = hkGm.GetComponent<InputHandler>();
            if (hkIh) hkIh.StartUIInput();
        }

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
            }
            else {
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
