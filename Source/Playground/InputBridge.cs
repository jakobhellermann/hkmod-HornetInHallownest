extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
using SsActions = Silksong::HeroActions;
using SsAction = Silksong::InControl.PlayerAction;

namespace HornetPlayer.Playground;

// Minimal dynamism at the InControl-actions level (not by hooking HeroController). Silksong's InControl is embedded
// in our prefixed assembly and its InputManager never runs, so the HeroActions set is never updated from a device.
// Instead of bringing up InputManager, we drive the individual PlayerActions directly each frame with
// CommitWithState(keyDown, tick, dt) (pushes state + computes WasPressed/IsPressed), then recompute the MoveVector
// composite. HeroController then reads inputActions.* exactly as in the real game -> move/turn/jump/dash/attack all
// go through the unmodified pipeline (incl. animation + facing). We do NOT call PlayerActionSet.Update (it would
// reset our states from the empty binding set). Runs before HeroController.Update via execution order.
internal static class InputBridge {
    private static GameObject? go;

    // Debug drive (for /press): force an action pressed for `frames` ticks, so we can demo without a physical key.
    internal static readonly Dictionary<string, int> Forced = new();

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.InputDriver");
        go.AddComponent<InputDriver>();
        Object.DontDestroyOnLoad(go);
        Log.Debug("[InputBridge] InputDriver installed (arrows=move, Z=jump, X=attack, C=dash)");
    }

    internal static void Cleanup() {
        if (go != null) {
            Object.Destroy(go);
            go = null;
        }
    }

    internal static void Press(string action, int frames) {
        Forced[action] = frames;
    }
}

[DefaultExecutionOrder(-10000)] // run before HeroController.Update so WasPressed lands the same frame
internal sealed class InputDriver : MonoBehaviour {
    private static MethodInfo? moveVectorUpdate;

    private static readonly (string name, KeyCode key)[] Map = {
        ("left", KeyCode.LeftArrow), ("right", KeyCode.RightArrow),
        ("up", KeyCode.UpArrow), ("down", KeyCode.DownArrow),
        ("jump", KeyCode.Z), ("attack", KeyCode.X), ("dash", KeyCode.C),
        ("superdash", KeyCode.S), // harpoon dash
        ("cast", KeyCode.A), // bind/heal + silk skill (Silksong's Cast action; Bind FSM ListenForCast)
        ("quickcast", KeyCode.F), // needle throw / quick tool / silk skill
        ("dreamnail", KeyCode.D), // needolin
        ("taunt", KeyCode.V), // taunt (ListenForTaunt reads Taunt.WasPressed; V is Silksong's own default taunt key)
        ("openinventory",
            KeyCode.K), // open the inventory (Inv pane); ListenForInventoryShortcut reads OpenInventory.WasPressed (K: I/O collide with HK's own inventory)
        ("opentools", KeyCode.L), // open the Tools/Crests pane directly
        ("menusubmit", KeyCode.Z), // inventory equip/submit
        ("menucancel", KeyCode.X) // inventory cancel/back
    };

    private bool infiniteSilk;

    private ulong tick;

    private void Update() {
        try {
            // T = teleport Hornet to HK's Knight (so she's where the camera/player is). Works even while paused.
            if (Input.GetKeyDown(KeyCode.T)) {
                var hornet = BundleSpike.HornetRoot;
                var knight = HeroController.UnsafeInstance;
                if (hornet != null && knight != null) {
                    hornet.transform.position = knight.transform.position;
                    Log.Info($"[InputDriver] teleported Hornet -> Knight at {knight.transform.position}");
                }
                else {
                    Log.Info($"[InputDriver] TP failed: hornet={hornet != null} knight={knight != null}");
                }
            }

            // Toggle infinite silk (B key). When on, refill silk to max each frame.
            if (Input.GetKeyDown(KeyCode.B)) {
                infiniteSilk = !infiniteSilk;
                Log.Info($"[InputDriver] infinite silk: {infiniteSilk}");
            }

            if (infiniteSilk) {
                var spd = Silksong::PlayerData.instance;
                if (spd != null && spd.silk < spd.silkMax) spd.silk = spd.silkMax;
            }

            // Don't drive input while HK is paused (HornetEnvironmentAdapter mirrors PAUSED to her GameManager) —
            // EXCEPT while the inventory is open: InventoryPauseBridge sets timeScale=0 to freeze the world, but the
            // inventory FSM still reads inputActions to navigate/close, so keep feeding input in that case. The hero's
            // own input blocker (added in the bridge) stops her from acting on it, so only the inventory consumes it.
            var pd = Silksong::PlayerData.instance;
            var inventoryOpen = pd != null && pd.isInventoryOpen;
            if (Time.timeScale <= 0.0001f && !inventoryOpen) return;
            // Only feed Hornet's actions while she's the active character; otherwise HK's Knight is in control.
            if (!HeroSwitch.HornetActive) return;

            var ia = SilksongBootstrap.InputActions;
            if (ia == null) return;
            tick++;
            var dt = Time.deltaTime;
            foreach (var (name, key) in Map) {
                var pressed = Input.GetKey(key) || Consume(name);
                ActionFor(ia, name)?.CommitWithState(pressed, tick, dt);
            }

            // Recompute the MoveVector (TwoAxis) from the freshly-committed Left/Right/Up/Down. Update is internal.
            moveVectorUpdate ??= ia.MoveVector.GetType().GetMethod("Update",
                BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(ulong), typeof(float)], null);
            moveVectorUpdate?.Invoke(ia.MoveVector, [tick, dt]);
        } catch (Exception e) {
            Log.Error($"[InputDriver] {e}");
        }
    }

    private static bool Consume(string name) {
        if (!InputBridge.Forced.TryGetValue(name, out var n) || n <= 0) return false;
        InputBridge.Forced[name] = n - 1;
        return true;
    }

    private static SsAction? ActionFor(SsActions ia, string name) {
        return name switch {
            "left" => ia.Left, "right" => ia.Right, "up" => ia.Up, "down" => ia.Down,
            "jump" => ia.Jump, "attack" => ia.Attack, "dash" => ia.Dash,
            "superdash" => ia.SuperDash, "cast" => ia.Cast, "quickcast" => ia.QuickCast, "dreamnail" => ia.DreamNail,
            "taunt" => ia.Taunt,
            "openinventory" => ia.OpenInventory, "opentools" => ia.OpenInventoryTools,
            "menusubmit" => ia.MenuSubmit, "menucancel" => ia.MenuCancel,
            _ => null
        };
    }
}
