extern alias Silksong;
using System;
using HornetInHallownest.HornetInHallownest.Core;
using HornetInHallownest.HornetInHallownest.Save;
using HornetInHallownest.HornetInHallownest.Util;
using HornetInHallownest.Playground;
using InControl;
using Modding;
using UnityEngine;
using SsActions = Silksong::HeroActions;
using SsAction = Silksong::InControl.PlayerAction;

namespace HornetInHallownest.HornetInHallownest.Modules;

// Feeds Hornet's (Silksong) HeroActions each frame so her unmodified hero pipeline reads move/jump/attack/... as usual.
// HK's own HeroActions (bound to the player's keyboard+gamepad, updated by HK's InputManager) drive the same-named
// Silksong action. Two twists: an action carrying a settings override reads our own bound InControl action instead of
// HK's; the two actions with no HK equivalent (Taunt, tools-pane) only exist as overrides. Silksong's InControl
// InputManager never runs, so we push each PlayerAction with CommitWithState (which computes WasPressed/IsPressed) and
// recompute the composite two-axis actions.
public sealed class InputModule : ModuleBase {
    // Actions mirrored from HK, not rebindable (UI, movement).
    // MenuCancel is handled separately (ESC).
    private static readonly (Func<HeroActions, PlayerAction> hk, Func<SsActions, SsAction> ss)[] mirror = [
        (h => h.left, s => s.Left), (h => h.right, s => s.Right), (h => h.up, s => s.Up), (h => h.down, s => s.Down),
        (h => h.paneLeft, s => s.PaneLeft), (h => h.paneRight, s => s.PaneRight), (h => h.menuSubmit, s => s.MenuSubmit),
        (h => h.rs_up, s => s.RsUp), (h => h.rs_down, s => s.RsDown), (h => h.rs_left, s => s.RsLeft),
        (h => h.rs_right, s => s.RsRight)
    ];

    // Overridable actions. Null in config means use the HK equivalent binding.
    private static readonly Overridable[] overridable = [
        new(s => s.Jump, h => h.jump, a => a.Jump),
        new(s => s.Attack, h => h.attack, a => a.Attack),
        new(s => s.Dash, h => h.dash, a => a.Dash),
        new(s => s.Harpoon, h => h.superDash, a => a.SuperDash),
        new(s => s.Bind, h => h.cast, a => a.Cast),
        new(s => s.Tool, h => h.quickCast, a => a.QuickCast),
        new(s => s.Needolin, h => h.dreamNail, a => a.DreamNail),
        // inventory sets default from HK, but is separate since HK is disabled when hornet is active
        new(s => s.OpenInventory, null, a => a.OpenInventory, h => h.openInventory),
        // without HK equivalent
        new(s => s.Taunt, null, a => a.Taunt),
        new(s => s.OpenTools, null, a => a.OpenInventoryTools)
    ];

    // Global-persisted binds 
    internal static InputSettings Settings = new();

    private HornetInputActions? overrideSet;
    private PlayerAction?[] overrideActions = [];
    private ulong tick;

    public override string Id => "input";

    public override bool RunWhilePaused => true;

    private static HeroActions? HkActions => InputHandler.Instance != null ? InputHandler.Instance.inputActions : null;

    public override void Initialize() {
        overrideSet = new HornetInputActions(overridable.Length);
        overrideActions = new PlayerAction?[overridable.Length];
        var hk = HkActions;
        for (var i = 0; i < overridable.Length; i++) {
            var def = overridable[i];
            var bind = def.Setting(Settings);
            if (bind == null && def.DefaultFrom != null && hk != null) // inherit HK's current binding as the default
                bind = KeybindUtil.GetKeyOrMouseBinding(def.DefaultFrom(hk)).ToString();
            if (bind == null) continue; // mirrored action or no bind
            if (KeybindUtil.ParseBinding(bind) is not { } parsed) {
                LogError($"unparseable keybind '{bind}'");
                continue;
            }

            var action = overrideSet.Slots[i];
            action.AddKeyOrMouseBinding(parsed);
            overrideActions[i] = action;
        }
    }

    protected override void OnDeinitialize() {
        SetHkInventoryEnabled(HkActions, true);
        overrideSet?.Destroy();
        overrideSet = null;
        overrideActions = [];
    }

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        var inputActions = SilksongBootstrap.InputActions;
        if (inputActions == null) return;
        
        var hk = HkActions;
        SetHkInventoryEnabled(hk, false);
        tick++;
        var dt = Time.deltaTime;

        foreach (var (hkGet, ssGet) in mirror) {
            var act = ssGet(inputActions);
            act.CommitWithState((hk != null && hkGet(hk).IsPressed), tick, dt);
        }

        for (var i = 0; i < overridable.Length; i++) {
            var def = overridable[i];
            var act = def.Ss(inputActions);
            var ov = overrideActions[i];
            var pressed = ov?.IsPressed ?? def.Hk != null && hk != null && def.Hk(hk).IsPressed;
            act.CommitWithState(pressed, tick, dt);
        }

        // ESC only reaches HK's InputHandler, manually route it to the inventory MenuCancel
        var esc = Input.GetKey(KeyCode.Escape);
        inputActions.MenuCancel.CommitWithState((hk != null && hk.menuCancel.IsPressed) || esc, tick, dt);

        // Recompute the composite two-axis actions from the just committed axes 
        inputActions.MoveVector.InvokeMethod("Update", tick, dt);
        inputActions.RightStick.InvokeMethod("Update", tick, dt);
    }

    public override void HornetToggled(bool active) {
        if (!active) SetHkInventoryEnabled(HkActions, true);
    }

    private static void SetHkInventoryEnabled(HeroActions? hk, bool enabled) {
        hk?.openInventory.Enabled = enabled;
    }

    private readonly record struct Overridable(
        Func<InputSettings, string?> Setting, // global settings key
        Func<HeroActions, PlayerAction>? Hk, // hk action
        Func<SsActions, SsAction> Ss, // silksong action
        Func<HeroActions, PlayerAction>? DefaultFrom = null); // snapshot HK binding as default
}
