extern alias Silksong;
using System;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using HornetInHallownest.Save;
using HornetInHallownest.Util;
using InControl;
using GlobalEnums;
using Modding;
using UnityEngine;
using SsActions = Silksong::HeroActions;
using SsAction = Silksong::InControl.PlayerAction;
using SsInputHandler = Silksong::InputHandler;
using SsInputControlType = Silksong::InControl.InputControlType;
using SsKey = Silksong::InControl.Key;
using SsMouse = Silksong::InControl.Mouse;

namespace HornetInHallownest.Modules;

// Feed Silksong's HeroActions.
// If possible (and not overridden), reuse HK keybinds for the equivalent silksong actions.
// Silksong's InControl InputManager is inactive, and manually Committed.
public sealed class InputModule : ModuleBase {
    // Actions mirrored from HK, not rebindable (UI, movement).
    // MenuCancel is handled separately (ESC).
    private static readonly (Func<HeroActions, PlayerAction> hk, Func<SsActions, SsAction> ss)[] mirror = [
        (h => h.paneLeft, s => s.PaneLeft), (h => h.paneRight, s => s.PaneRight),
        (h => h.menuSubmit, s => s.MenuSubmit),
        (h => h.rs_up, s => s.RsUp), (h => h.rs_down, s => s.RsDown), (h => h.rs_left, s => s.RsLeft),
        (h => h.rs_right, s => s.RsRight), (h => h.quickMap, s => s.QuickMap)
    ];

    // Overridable actions. Null in config means use the HK equivalent binding.
    private static readonly Overridable[] overridable = [
        new(s => s.MoveLeft, h => h.left, a => a.Left, Axis: true),
        new(s => s.MoveRight, h => h.right, a => a.Right, Axis: true),
        new(s => s.MoveUp, h => h.up, a => a.Up, Axis: true),
        new(s => s.MoveDown, h => h.down, a => a.Down, Axis: true),
        new(s => s.Jump, h => h.jump, a => a.Jump),
        new(s => s.Attack, h => h.attack, a => a.Attack),
        new(s => s.Dash, h => h.dash, a => a.Dash),
        new(s => s.Harpoon, h => h.superDash, a => a.SuperDash),
        new(s => s.Bind, h => h.cast, a => a.Cast),
        new(s => s.Tool, h => h.quickCast, a => a.QuickCast),
        new(s => s.Needolin, h => h.dreamNail, a => a.DreamNail),
        new(s => s.OpenInventory, h => h.openInventory, a => a.OpenInventory, SnapshotKey: true, PrimaryOnly: true),
        // without HK equivalent
        new(s => s.Taunt, null, a => a.Taunt, ControllerSetting: s => s.TauntController),
        new(s => s.OpenTools, null, a => a.OpenInventoryTools, ControllerSetting: s => s.OpenToolsController,
            PrimaryOnly: true)
    ];

    // Global-persisted binds
    internal static InputSettings Settings = new();

    private HornetInputActions? overrideSet;
    private PlayerAction?[] overrideActions = [];
    private HornetInputActions? menuButtons;

    // Buttons still held when a menu closes stay released for Hornet until let go: the press that closes a menu (B
    // cancels it) would otherwise land as gameplay input the same frame, e.g. her tap-to-bind.
    private readonly bool[] heldOverMenu = new bool[overridable.Length];
    private bool wasInInventory;
    private int lastUpdateFrame;
    private ulong tick;

    public override string Id => "input";

    public override bool RunWhilePaused => true;

    private static HeroActions? HkActions => InputHandler.Instance != null ? InputHandler.Instance.inputActions : null;

    public override void Initialize() {
        BuildOverrides();

        // HK has no menu Extra/Super actions. Silksong's menus read them on the controller only (keyboard uses dash and
        // dream nail), e.g. Super to change crest; bind them to Silksong's defaults.
        menuButtons = new HornetInputActions(2);
        menuButtons.Slots[0].AddBinding(new DeviceBindingSource(InputControlType.Action3));
        menuButtons.Slots[1].AddBinding(new DeviceBindingSource(InputControlType.Action4));

        // Rebuild when HK's bindings change. HK loads its keyboard bindings in InputHandler.Start, which can run after
        // this (the 1.5.78 Modding API initializes mods from InputHandler's own Awake).
        Detour(typeof(InputHandler), "MapKeyboardLayoutFromGameSettings", OnBindingsChanged);
        Detour(typeof(InputHandler), nameof(InputHandler.MapControllerButtons), OnMapControllerButtons);
        Detour(typeof(InputHandler), nameof(InputHandler.SendKeyBindingsToGameSettings), OnBindingsChanged);
        Detour(typeof(InputHandler), nameof(InputHandler.RemapUIButtons), OnBindingsChanged);

        // Silksong's actions are only ever committed, never bound, so its button prompts showed "None". Answer its
        // binding lookups with the HK action each one is fed from.
        Detour(typeof(SsInputHandler), nameof(SsInputHandler.GetKeyBindingForAction), OnGetKeyBindingForAction);
        Detour(typeof(SsInputHandler), nameof(SsInputHandler.GetButtonBindingForAction), OnGetButtonBindingForAction);
    }

    private SsInputHandler.KeyOrMouseBinding OnGetKeyBindingForAction(
        Func<SsInputHandler, SsAction, SsInputHandler.KeyOrMouseBinding> orig, SsInputHandler self, SsAction action) {
        if (SourceAction(action) is not { } source) return orig(self, action);
        var binding = source.GetKeyOrMouseBinding();
        if (binding.Mouse != Mouse.None)
            return new SsInputHandler.KeyOrMouseBinding(
                Enum.TryParse(binding.Mouse.ToString(), out SsMouse mouse) ? mouse : SsMouse.None);
        return new SsInputHandler.KeyOrMouseBinding(
            Enum.TryParse(binding.Key.ToString(), out SsKey key) ? key : SsKey.None);
    }

    private SsInputControlType OnGetButtonBindingForAction(Func<SsInputHandler, SsAction, SsInputControlType> orig,
        SsInputHandler self, SsAction action) {
        if (SourceAction(action) is not { } source) return orig(self, action);
        return Enum.TryParse(source.GetControllerButtonBinding().ToString(), out SsInputControlType control)
            ? control
            : SsInputControlType.None;
    }

    // The HK-side action whose state is committed to `action`.
    private PlayerAction? SourceAction(SsAction action) {
        var ss = SilksongBootstrap.InputActions;
        var hk = HkActions;
        if (ss == null || hk == null) return null;
        foreach (var (hkGet, ssGet) in mirror)
            if (ssGet(ss) == action)
                return hkGet(hk);
        for (var i = 0; i < overridable.Length; i++)
            if (overridable[i].Ss(ss) == action)
                return overrideActions[i] ?? overridable[i].Hk?.Invoke(hk);
        if (action == ss.MenuCancel) return hk.menuCancel;
        if (menuButtons == null) return null;
        if (action == ss.MenuExtra) return menuButtons.Slots[0];
        return action == ss.MenuSuper ? menuButtons.Slots[1] : null;
    }

    private void OnMapControllerButtons(Action<InputHandler, GamepadType> orig, InputHandler self, GamepadType type) {
        orig(self, type);
        BuildOverrides();
    }

    private void OnBindingsChanged(Action<InputHandler> orig, InputHandler self) {
        orig(self);
        BuildOverrides();
    }

    private void BuildOverrides() {
        overrideSet?.Destroy();
        overrideSet = new HornetInputActions(overridable.Length);
        overrideActions = new PlayerAction?[overridable.Length];
        var hk = HkActions;
        for (int i = 0; i < overridable.Length; i++) {
            var def = overridable[i];
            string? bind = def.Setting(Settings);
            if (bind == null && def is { SnapshotKey: true, Hk: not null } && hk != null) {
                var snapshot = KeybindUtil.GetKeyOrMouseBinding(def.Hk(hk));
                if (!InputHandler.KeyOrMouseBinding.IsNone(snapshot)) bind = snapshot.ToString();
            }
            if (bind == null) continue; // mirrored action or no bind
            if (KeybindUtil.ParseBinding(bind) is not { } parsed) {
                LogError($"unparseable keybind '{bind}'");
                continue;
            }

            var action = overrideSet.Slots[i];
            action.AddKeyOrMouseBinding(parsed);

            if (hk != null) InheritDeviceBindings(def.Hk?.Invoke(hk), action);
            AddControllerBinding(action, def.ControllerSetting?.Invoke(Settings));
            overrideActions[i] = action;
        }
    }

    private static void InheritDeviceBindings(PlayerAction? hkAction, PlayerAction action) {
        if (hkAction == null) return;
        foreach (var b in hkAction.Bindings)
            if (b is DeviceBindingSource dbs && dbs.Control != InputControlType.None)
                action.AddBinding(new DeviceBindingSource(dbs.Control));
    }

    private void AddControllerBinding(PlayerAction action, string? buttonName) {
        if (buttonName == null) return;
        if (Enum.TryParse(buttonName, true, out InputControlType control))
            action.AddBinding(new DeviceBindingSource(control));
        else LogError($"unparseable controller binding '{buttonName}'");
    }

    protected override void OnDeinitialize() {
        SetHkInventoryEnabled(HkActions, true);
        overrideSet?.Destroy();
        overrideSet = null;
        overrideActions = [];
        menuButtons?.Destroy();
        menuButtons = null;
    }

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        var inputActions = SilksongBootstrap.InputActions;
        if (inputActions == null) return;

        var hk = HkActions;
        // Only the primary owns the inventory: while co-driven but not primary the Knight owns it, so leave HK's key on.
        var primary = HeroSwitch.HornetActive;
        // While HK's inventory is open its key must close it, and Silksong's inventory must not open on top.
        var hkInventoryOpen = InventoryModule.HkInventoryOpen;
        SetHkInventoryEnabled(hk, !primary || hkInventoryOpen);
        tick++;
        var dt = Time.deltaTime;

        foreach (var (hkGet, ssGet) in mirror) {
            var act = ssGet(inputActions);
            act.CommitWithState((hk != null && hkGet(hk).IsPressed), tick, dt);
        }

        if (menuButtons != null) {
            inputActions.MenuExtra.CommitWithState(menuButtons.Slots[0].IsPressed, tick, dt);
            inputActions.MenuSuper.CommitWithState(menuButtons.Slots[1].IsPressed, tick, dt);
        }

        // HornetRuntime skips this update under HK's pause menu and HK's inventory, so a skipped frame means one of them
        // just closed. Hornet's own inventory keeps it running.
        var inInventory = Silksong::PlayerData.instance.isInventoryOpen;
        var leftMenu = lastUpdateFrame != Time.frameCount - 1 || (wasInInventory && !inInventory);
        lastUpdateFrame = Time.frameCount;
        wasInInventory = inInventory;

        for (var i = 0; i < overridable.Length; i++) {
            var def = overridable[i];
            var act = def.Ss(inputActions);
            var ov = overrideActions[i];
            var pressed = ov?.IsPressed ?? def.Hk != null && hk != null && def.Hk(hk).IsPressed;
            if (def.PrimaryOnly && (!primary || hkInventoryOpen)) pressed = false;
            if (!pressed) heldOverMenu[i] = false;
            else if (leftMenu && !def.Axis) heldOverMenu[i] = true;
            act.CommitWithState(pressed && !heldOverMenu[i], tick, dt);
        }

        // ESC only reaches HK's InputHandler, manually route it to the inventory MenuCancel - but only for the primary
        // (co-drive: the non-primary Hornet must not react to the Knight's pause/cancel).
        var esc = primary && Input.GetKey(KeyCode.Escape);
        inputActions.MenuCancel.CommitWithState((primary && hk != null && hk.menuCancel.IsPressed) || esc, tick, dt);

        // Recompute the composite two-axis actions from the just committed axes
        inputActions.MoveVector.InvokeMethod("Update", tick, dt);
        inputActions.RightStick.InvokeMethod("Update", tick, dt);

        if (!Paused) MaintainInputHandler();
        SyncActiveController();
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
    }

    // Mirror HK's active controller onto Silksong's InputHandler (its own InControl never runs); keyboard-only menu
    // shortcuts, the menu action mapping and the glyph UIs read it, also while an inventory pauses the game. Edge-only,
    // then fire RefreshActiveControllerEvent so glyphs recompute.
    private static void SyncActiveController() {
        var ih = SilksongBootstrap.Handler;
        if (!ih) return;
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
        bool SnapshotKey = false, // snapshot HK binding as default (since hk is disabled while Hornet is primary)
        bool PrimaryOnly = false, // suppressed while Hornet is active but not primary
        Func<InputSettings, string?>? ControllerSetting = null, // Silksong's controller default
        bool Axis = false); // movement: keeps working when held through a menu closing
}
