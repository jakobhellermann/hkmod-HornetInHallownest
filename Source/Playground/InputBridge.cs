extern alias Silksong;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
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

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.InputDriver");
        go.AddComponent<InputDriver>();
        Object.DontDestroyOnLoad(go);
        Log.Info("[InputBridge] InputDriver installed (arrows=move, Z=jump, X=attack, C=dash)");
    }

    internal static void Cleanup() {
        if (go != null) { Object.Destroy(go); go = null; }
    }

    // Debug drive (for /press): force an action pressed for `frames` ticks, so we can demo without a physical key.
    internal static readonly Dictionary<string, int> Forced = new();
    internal static void Press(string action, int frames) { Forced[action] = frames; }
}

[DefaultExecutionOrder(-10000)] // run before HeroController.Update so WasPressed lands the same frame
internal sealed class InputDriver : MonoBehaviour {
    private ulong tick;
    private static MethodInfo? moveVectorUpdate;

    private static readonly (string name, KeyCode key)[] Map = {
        ("left", KeyCode.LeftArrow), ("right", KeyCode.RightArrow),
        ("up", KeyCode.UpArrow), ("down", KeyCode.DownArrow),
        ("jump", KeyCode.Z), ("attack", KeyCode.X), ("dash", KeyCode.C),
        ("superdash", KeyCode.S), // harpoon dash
        ("cast", KeyCode.F),      // silk skill / special
        ("quickcast", KeyCode.G), // needle throw / quick tool
        ("dreamnail", KeyCode.D), // needolin
    };

    private void Update() {
        try {
            // Don't drive input while HK is paused (HornetEnvironmentAdapter mirrors PAUSED to her GameManager).
            if (Time.timeScale <= 0.0001f) return;

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
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(ulong), typeof(float) }, null);
            moveVectorUpdate?.Invoke(ia.MoveVector, new object[] { tick, dt });
        } catch (System.Exception e) { Log.Error($"[InputDriver] {e}"); }
    }

    private static bool Consume(string name) {
        if (!InputBridge.Forced.TryGetValue(name, out var n) || n <= 0) return false;
        InputBridge.Forced[name] = n - 1;
        return true;
    }

    private static SsAction? ActionFor(SsActions ia, string name) => name switch {
        "left" => ia.Left, "right" => ia.Right, "up" => ia.Up, "down" => ia.Down,
        "jump" => ia.Jump, "attack" => ia.Attack, "dash" => ia.Dash,
        "superdash" => ia.SuperDash, "cast" => ia.Cast, "quickcast" => ia.QuickCast, "dreamnail" => ia.DreamNail,
        _ => null,
    };
}
