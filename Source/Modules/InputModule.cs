extern alias Silksong;
using System;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using HornetInHallownest.Save;
using HornetInHallownest.Util;
using InControl;
using Modding;
using UnityEngine;
using SsActions = Silksong::HeroActions;
using SsAction = Silksong::InControl.PlayerAction;

namespace HornetInHallownest.Modules;

// Feed Silksong's HeroActions.
// If possible (and not overridden), reuse HK keybinds for the equivalent silksong actinos.
// Silksong's InControl InputManager is inactive, and manually Committed.
public sealed class InputModule : ModuleBase {
    // Actions mirrored from HK, not rebindable (UI, movement).
    // MenuCancel is handled separately (ESC).
    private static readonly (Func<HeroActions, PlayerAction> hk, Func<SsActions, SsAction> ss)[] mirror = [
        (h => h.paneLeft, s => s.PaneLeft), (h => h.paneRight, s => s.PaneRight), (h => h.menuSubmit, s => s.MenuSubmit),
        (h => h.rs_up, s => s.RsUp), (h => h.rs_down, s => s.RsDown), (h => h.rs_left, s => s.RsLeft),
        (h => h.rs_right, s => s.RsRight)
    ];

    // Overridable actions. Null in config means use the HK equivalent binding.
    private static readonly Overridable[] overridable = [
        new(s => s.MoveLeft, h => h.left, a => a.Left),
        new(s => s.MoveRight, h => h.right, a => a.Right),
        new(s => s.MoveUp, h => h.up, a => a.Up),
        new(s => s.MoveDown, h => h.down, a => a.Down),
        new(s => s.Jump, h => h.jump, a => a.Jump),
        new(s => s.Attack, h => h.attack, a => a.Attack),
        new(s => s.Dash, h => h.dash, a => a.Dash),
        new(s => s.Harpoon, h => h.superDash, a => a.SuperDash),
        new(s => s.Bind, h => h.cast, a => a.Cast),
        new(s => s.Tool, h => h.quickCast, a => a.QuickCast),
        new(s => s.Needolin, h => h.dreamNail, a => a.DreamNail),
        // inventory sets default from HK, but is separate since HK is disabled when hornet is active.
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
        // Only the primary owns the inventory: while co-driven but not primary the Knight owns it, so leave HK's key on.
        var primary = HeroSwitch.HornetActive;
        SetHkInventoryEnabled(hk, !primary);
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
            if (def.PrimaryOnly && !primary) pressed = false;
            act.CommitWithState(pressed, tick, dt);
        }

        // ESC only reaches HK's InputHandler, manually route it to the inventory MenuCancel - but only for the primary
        // (co-drive: the non-primary Hornet must not react to the Knight's pause/cancel).
        var esc = primary && Input.GetKey(KeyCode.Escape);
        inputActions.MenuCancel.CommitWithState((primary && hk != null && hk.menuCancel.IsPressed) || esc, tick, dt);

        // Recompute the composite two-axis actions from the just committed axes
        inputActions.MoveVector.InvokeMethod("Update", tick, dt);
        inputActions.RightStick.InvokeMethod("Update", tick, dt);

        if (!Paused) MaintainInputHandler();
    }

    // The necessary parts from InputHandler.Update.
    // Not used entirely, because it also handles cursor, silksong pause toggle etc.
    private static void MaintainInputHandler() {
        var ih = SilksongBootstrap.Handler;
        if (!ih) return;

        ih.InvokeMethod("UpdateButtonQueueing");

        // Clear ForceDreamNailRePress once DreamNail is released (RegainControl sets it, only Update clears it, else
        // ListenForDreamNail skips forever). Inlined to skip PlayingInput's CheatManager.IsOpen read.
        if (ih.inputActions != null && !ih.inputActions.DreamNail.IsPressed)
            ih.ForceDreamNailRePress = false;

        // Mirror HK's active controller onto Silksong's InputHandler (its own InControl never runs); keyboard-only menu
        // shortcuts and the glyph UIs read it. Edge-only, then fire RefreshActiveControllerEvent so glyphs recompute.
        var hkController = InputHandler.Instance
            ? (Silksong::InControl.BindingSourceType)(int)InputHandler.Instance.lastActiveController
            : Silksong::InControl.BindingSourceType.KeyBindingSource;
        if (ih.lastActiveController != hkController) {
            ih.lastActiveController = hkController;
            ih.InvokeMethod("SendRefreshEvent");
        }
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
        Func<HeroActions, PlayerAction>? DefaultFrom = null, // snapshot HK binding as default
        bool PrimaryOnly = false); // suppressed while Hornet is active but not primary
}
