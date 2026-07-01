extern alias Silksong;
using System;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// Inventory -> HK pause.
//
// Hornet's inventory opens via the Silksong "Inventory Control" FSM. Its pause intent rides on a
// CallMethodProper(GameManager.SetIsInventoryOpen): the FSM resolves the target GameObject from the PlayMaker GLOBAL
// "GameManager" var (set in SilksongBootstrap, the one bit of GameManager.Awake we replicate) and the component by the
// string "GameManager" (resolved by GetComponentShim across the cross-game name collision). So the call now REACHES our
// bootstrap GameManager — but we must NOT run its real body: Silksong's SetIsInventoryOpen -> SetPausedState touches
// shadow-world singletons (gameCams/ui/GlobalSettings.Camera, all unset on the inactive bootstrap GM) and would
// NullRef-cascade. Instead we hook it, skip orig, and adapt the intent onto HK.
//
// The adaptation: freeze the world via the single global Time.timeScale (shared HK+Hornet, so 0 stops HK's
// enemies/physics behind the inventory's already-frozen image — the previously-missing piece), keep
// PlayerData.isInventoryOpen coherent (read elsewhere, e.g. InputBridge keeps feeding input while it's true so the
// inventory stays navigable under timeScale=0), and toggle the real hero's input blocker so she can't act while open.
internal static class InventoryPauseBridge {
    private static Hook? hook;
    private static object? inputBlocker;
    private static float prevTimeScale = 1f;

    internal static void Install() {
        if (hook != null) return;
        try {
            hook = new Hook(
                typeof(Silksong::GameManager).GetMethod("SetIsInventoryOpen"),
                (Action<Action<Silksong::GameManager, bool>, Silksong::GameManager, bool>)((orig, self, value) => {
                    // Deliberately do NOT call orig — its SetPausedState cascades into uninitialised shadow-world refs.
                    try {
                        var pd = Silksong::PlayerData.instance;
                        if (pd != null) pd.isInventoryOpen = value;

                        if (value) {
                            prevTimeScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
                            Time.timeScale = 0f;
                        }
                        else {
                            Time.timeScale = prevTimeScale > 0.0001f ? prevTimeScale : 1f;
                        }

                        var hero = BundleSpike.RealHero;
                        if (hero != null) {
                            inputBlocker ??= new object();
                            if (value) hero.AddInputBlocker(inputBlocker);
                            else hero.RemoveInputBlocker(inputBlocker);
                        }

                        // Put HK into the same input-suppressed state it enters for its OWN UI (PauseGameToggle sets
                        // isPaused + StopUIInput), minus showing HK's menu — so HK's input systems stop firing behind the
                        // Silksong inventory. This is the generic root fix for both symptoms: acceptingInput=false
                        // (StopUIInput) gates HK's ESC pause-poll (InputHandler.Update runs it inside if(acceptingInput));
                        // isPaused=true gates every HK ListenFor* action (they early-return on gm.isPaused) — incl. the
                        // Bench Control get-up on up/down. HK's GameManager is the unprefixed one (Silksong's is inactive).
                        var hkGm = GameManager.instance; // HK's GameManager
                        if (hkGm != null) {
                            hkGm.isPaused = value;
                            var hkIh = hkGm.GetComponent<InputHandler>();
                            if (hkIh != null) {
                                if (value) hkIh.StopUIInput();
                                else hkIh.StartUIInput();
                            }
                        }

                        Log.Info(
                            $"[InvPause] SetIsInventoryOpen({value}) -> HK world {(value ? "frozen" : "resumed")} "
                            + $"(timeScale={Time.timeScale})");
                    } catch (Exception e) {
                        Log.Error($"[InvPause] {e}");
                    }
                }));
            Log.Debug("[InvPause] installed (Silksong GameManager.SetIsInventoryOpen -> HK pause)");
        } catch (Exception e) {
            Log.Error($"[InvPause] install: {e.Message}");
        }
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        // If we tore down while the inventory was open, don't leave the world frozen across the reload.
        if (Time.timeScale <= 0.0001f) Time.timeScale = prevTimeScale > 0.0001f ? prevTimeScale : 1f;
        inputBlocker = null;
    }
}
