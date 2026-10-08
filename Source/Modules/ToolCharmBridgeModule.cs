extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using Modding;

namespace HornetInHallownest.Modules;

public sealed class ToolCharmBridgeModule : ModuleBase {
    private const string CompassCharm = "equippedCharm_2";

    private static readonly Dictionary<string, string> tools = new() {
        ["equippedCharm_1"] = "Rosary Magnet", // Gathering Swarm
        [CompassCharm] = "Compass" // Wayward Compass
    };

    public override string Id => "tool-charm-bridge";

    public override void Initialize() {
        ModHooks.GetPlayerBoolHook += OnGetBool;
        Detour(typeof(GameMap), nameof(GameMap.WorldMap), OnWorldMap);
        Detour(typeof(GameMap), nameof(GameMap.PositionCompass), OnPositionCompass);
    }

    protected override void OnDeinitialize() {
        ModHooks.GetPlayerBoolHook -= OnGetBool;
    }

    private static bool OnGetBool(string name, bool orig) =>
        HeroSwitch.HornetActive && tools.TryGetValue(name, out var tool)
            ? Silksong::ToolItemManager.IsToolEquipped(tool)
            : orig;

    private static void OnWorldMap(Action<GameMap> orig, GameMap self) => WithHornetCompass(() => orig(self));

    private static void OnPositionCompass(Action<GameMap, bool> orig, GameMap self, bool posShade) =>
        WithHornetCompass(() => orig(self, posShade));

    // GameMap reads the compass charm's field directly rather than through GetBool.
    private static void WithHornetCompass(Action call) {
        var pd = PlayerData.instance;
        if (!HeroSwitch.HornetActive || pd == null) {
            call();
            return;
        }

        var knightCompass = pd.equippedCharm_2;
        pd.equippedCharm_2 = Silksong::ToolItemManager.IsToolEquipped(tools[CompassCharm]);
        try {
            call();
        } finally {
            pd.equippedCharm_2 = knightCompass;
        }
    }
}
