extern alias Silksong;
using System.Linq;
using HornetInHallownest.Util;

namespace HornetInHallownest.Bootstrap;

// Bring up Silksong's `ToolItemManager` (tools/crests/nail-arts deref it). Copies its `toolItems` + `crestList`
// serialized assets onto a single-manager GO (see ManagerSingletonBootstrap); `cursedCrest` is derived by its Awake.
internal static class ToolItemManagerBootstrap {
    private const string GoName = "Silksong_ToolItemManager";

    internal static void Ensure() {
        var mgr = ManagerSingletonBootstrap.BringUp(typeof(Silksong::ToolItemManager), GoName, "toolItems",
            "crestList");
        if (mgr == null) return; // BringUp logged the failure

        // Our bootstrap GM never fires GameManager.SceneInit, so ToolItemManager.SceneInit (the only place that inits
        // the equipChangedTool*Reminder fields) never runs -> ReportBoundAttackToolUsed NullRefs. Call it directly.
        mgr.InvokeMethod("SceneInit");
    }

    // Diagnostic: confirm the singleton + serialized lists resolve, and that GetCrestByName works.
    // Called by a dev server route on the debug branch.
    // ReSharper disable once UnusedMember.Global
    internal static object Diag() {
        var mgr = Silksong::ToolItemManager.SilentInstance;
        if (mgr == null) return new { error = "ToolItemManager singleton null (Ensure not run / spawn first)" };
        var crests = Silksong::ToolItemManager.GetAllCrests();
        var tools = Silksong::ToolItemManager.GetUnlockedTools().ToList();
        var crestId = Silksong::PlayerData.instance.CurrentCrestID;
        var byName = string.IsNullOrEmpty(crestId) ? null : Silksong::ToolItemManager.GetCrestByName(crestId);
        return new {
            instanceSet = true,
            crestCount = crests.Count,
            crestNames = crests.Select(c => c.name).ToArray(),
            unlockedToolCount = tools.Count,
            currentCrestID = crestId,
            getCrestByNameResolves = byName != null
        };
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
        mgr.SetFieldValue("queueAttackToolsChanged", true);
        Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
        Silksong::ToolItemManager.SendEquippedChangedEvent(true);
    }

    internal static void Cleanup() {
        ManagerSingletonBootstrap.Destroy(GoName);
    }
}
