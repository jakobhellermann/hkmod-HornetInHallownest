extern alias Silksong;
using HornetInHallownest.Util;

namespace HornetInHallownest.Bootstrap;

internal static class ToolItemManagerBootstrap {
    private const string GoName = "Silksong_ToolItemManager";

    internal static void Ensure() {
        var mgr = ManagerSingletonBootstrap.BringUp(typeof(Silksong::ToolItemManager), GoName, "toolItems", "crestList");
        // Called from GameManager.SceneInit, which the inactive bootstrap GameManager never runs.
        ((Silksong::ToolItemManager)mgr).SceneInit();
    }

    // Unlock + equip a tool by name without a bench (Silksong gates the equip UI to benches). Idempotent.
    internal static void EquipToolByName(string name) {
        var tool = Silksong::ToolItemManager.GetToolByName(name);
        if (tool == null) {
            Log.Error($"[ToolItemManager] can't equip '{name}': not found (manager down or wrong name)");
            return;
        }

        if (Silksong::ToolItemManager.IsToolEquipped(tool.name)) return;

        tool.SetUnlockedTestsComplete();
        tool.Unlock(null, Silksong::ToolItem.PopupFlags.None);
        Silksong::ToolItemManager.AutoEquip(tool);
        Silksong::ToolItemManager.SendEquippedChangedEvent(true);
    }

    internal static void RefreshBoundAttackTools() {
        var mgr = Silksong::ToolItemManager.SilentInstance;
        if (mgr == null) return;
        mgr.queueAttackToolsChanged = true;
        Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
        Silksong::ToolItemManager.SendEquippedChangedEvent(true);
    }

    internal static void Cleanup() {
        ManagerSingletonBootstrap.Destroy(GoName);
    }
}
