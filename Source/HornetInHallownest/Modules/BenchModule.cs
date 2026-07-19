extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using Modding;

namespace HornetPlayer.HornetInHallownest.Modules;

// Sync hornets onBench state, refill tools on bench.
public sealed class BenchModule : ModuleBase {
    public override string Id => "bench";

    public override void Initialize() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
    }

    protected override void OnDeinitialize() {
        ModHooks.SetPlayerBoolHook -= OnSetBool;
    }

    private bool OnSetBool(string name, bool value) {
        if (name != "atBench" || !HeroSwitch.HornetActive) return value;
        var spd = Silksong::PlayerData.instance;
        if (spd == null || spd.atBench == value) return value;

        spd.atBench = value;
        if (value) RefillTools();
        return value;
    }

    // Setting a tool's AmountLeft doesn't notify the HUD; icons redraw only on these events.
    public static void RefreshToolHud() {
        Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
        Silksong::ToolItemManager.SendEquippedChangedEvent(true);
    }

    // TODO: implement shell shards?
    private void RefillTools() {
        try {
            var tools = Silksong::PlayerData.instance?.Tools;
            if (tools == null) return;
            foreach (var tool in Silksong::ToolItemManager.GetUnlockedTools()) {
                if (!tool) continue;
                var data = tools.GetData(tool.name);
                data.AmountLeft = Silksong::ToolItemManager.GetToolStorageAmount(tool);
                tools.SetData(tool.name, data);
            }

            RefreshToolHud();
        } catch (Exception e) {
            LogError($"tool refill failed: {e.Message}");
        }
    }
}
