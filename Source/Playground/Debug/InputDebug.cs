extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.HornetInHallownest.Modules;
using SsActions = Silksong::HeroActions;
using SsAction = Silksong::InControl.PlayerAction;

namespace HornetInHallownest.Playground;

// Debug-only: drives Silksong actions for a few frames without a physical key (POST /press), by name. Attaches to
// InputModule's DebugHold overlay while installed; detaching (or dropping this file at release) leaves input untouched.
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
        ["taunt"] = s => s.Taunt, ["opentools"] = s => s.OpenInventoryTools
    };

    private static readonly Dictionary<SsAction, int> forced = new();

    internal static string[] KnownActions => Actions.Keys.ToArray();

    internal static bool IsKnownAction(string action) => Actions.ContainsKey(action);

    internal static void Install() {
        InputModule.DebugHold = IsHeld;
    }

    internal static void Cleanup() {
        InputModule.DebugHold = null;
        forced.Clear();
    }

    internal static void Press(string action, int frames) {
        var ia = SilksongBootstrap.InputActions;
        if (ia == null || !Actions.TryGetValue(action, out var get)) return;
        forced[get(ia)] = frames;
    }

    // Queried once per action per frame, so decrementing on a hit spends exactly one frame.
    private static bool IsHeld(SsAction action) {
        if (!forced.TryGetValue(action, out var n) || n <= 0) return false;
        forced[action] = n - 1;
        return true;
    }
}
