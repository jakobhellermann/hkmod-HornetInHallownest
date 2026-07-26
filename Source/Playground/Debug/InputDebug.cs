extern alias Silksong;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Modules;
using InControl;
using UnityEngine;
using SsActions = Silksong::HeroActions;
using SsAction = Silksong::InControl.PlayerAction;

namespace HornetInHallownest.Playground;

// POST /press: holds Silksong actions through InputModule.DebugHold and HK's same-named actions through an extra binding.
internal static class InputDebug {
    // name -> the Silksong action it drives. Deliberately separate from the core input mapping — these names are a
    // debug-only interface, not core input; if the two drift, /press just can't drive the missing action.
    private static readonly Dictionary<string, Func<SsActions, SsAction>> Actions = new() {
        ["left"] = s => s.Left, ["right"] = s => s.Right, ["up"] = s => s.Up, ["down"] = s => s.Down,
        ["jump"] = s => s.Jump, ["attack"] = s => s.Attack, ["dash"] = s => s.Dash, ["evade"] = s => s.Evade,
        ["superdash"] = s => s.SuperDash, ["cast"] = s => s.Cast, ["quickcast"] = s => s.QuickCast,
        ["dreamnail"] = s => s.DreamNail, ["quickmap"] = s => s.QuickMap, ["openinventory"] = s => s.OpenInventory,
        ["paneleft"] = s => s.PaneLeft, ["paneright"] = s => s.PaneRight,
        ["menusubmit"] = s => s.MenuSubmit, ["menucancel"] = s => s.MenuCancel,
        ["taunt"] = s => s.Taunt, ["opentools"] = s => s.OpenInventoryTools, ["pause"] = s => s.Pause
    };

    private static readonly Dictionary<string, int> heldUntilFrame = new();
    private static readonly Dictionary<string, float> heldUntilTime = new();
    private static readonly List<(PlayerAction action, HeldBinding binding)> hkBindings = [];

    internal static string[] KnownActions => Actions.Keys.ToArray();

    internal static bool IsKnownAction(string action) => Actions.ContainsKey(action);

    internal static void Install() {
        InputModule.DebugHold = IsHeld;

        // HK's HeroActions name its actions like these, in camelCase (superDash, quickCast, ...).
        var hk = GameManager.instance.inputHandler.inputActions;
        foreach (var field in hk.GetType().GetFields()) {
            if (field.GetValue(hk) is not PlayerAction action || !Actions.ContainsKey(field.Name.ToLowerInvariant())) continue;
            var binding = new HeldBinding(field.Name.ToLowerInvariant());
            action.AddBinding(binding);
            hkBindings.Add((action, binding));
        }
    }

    internal static void Cleanup() {
        InputModule.DebugHold = null;
        foreach (var (action, binding) in hkBindings) action.RemoveBinding(binding);
        hkBindings.Clear();
        heldUntilFrame.Clear();
        heldUntilTime.Clear();
    }

    internal static void Press(string action, int frames) => heldUntilFrame[action] = Time.frameCount + frames;

    // Wall-clock hold: the frame rate isn't fixed, so a frame count says little about how long an action is held.
    internal static void PressFor(string action, float seconds) => heldUntilTime[action] = Time.unscaledTime + seconds;

    private static bool IsHeld(string action) =>
        (heldUntilFrame.TryGetValue(action, out var frame) && Time.frameCount < frame) ||
        (heldUntilTime.TryGetValue(action, out var time) && Time.unscaledTime < time);

    private static bool IsHeld(SsAction action) {
        var ia = SilksongBootstrap.InputActions;
        return ia != null && Actions.Any(kv => kv.Value(ia) == action && IsHeld(kv.Key));
    }

    private sealed class HeldBinding(string action) : BindingSource {
        public override string Name => $"/press {action}";
        // HK takes its last active controller from the binding that fired, and the HUD's button glyphs follow it.
        public override string DeviceName => "Keyboard";
        public override InputDeviceClass DeviceClass => InputDeviceClass.Keyboard;
        public override InputDeviceStyle DeviceStyle => InputDeviceStyle.Unknown;
        public override BindingSourceType BindingSourceType => BindingSourceType.KeyBindingSource;
        public override float GetValue(InputDevice inputDevice) => IsHeld(action) ? 1f : 0f;
        public override bool GetState(InputDevice inputDevice) => IsHeld(action);
        public override bool Equals(BindingSource other) => ReferenceEquals(this, other);
        public override void Save(BinaryWriter writer) => throw new NotSupportedException();
        public override void Load(BinaryReader reader, ushort dataFormatVersion) => throw new NotSupportedException();
    }
}
