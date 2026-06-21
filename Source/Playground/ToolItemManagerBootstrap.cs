extern alias Silksong;
using System.Linq;

namespace HornetPlayer.Playground;

// SURGICAL bring-up of Silksong's `ToolItemManager` (open item #6) — the singleton the tools/crests/nail-art systems
// deref. It carries two serialized assets: `toolItems` (ToolItemList) + `crestList` (ToolCrestList). The mechanics
// (load the _GameManager prefab ASSET, copy serialized fields onto a fresh single-manager GO, run only its Awake) live
// in ManagerSingletonBootstrap — see there for why we don't instantiate the prefab. `cursedCrest` (the 3rd
// SerializeField) is NOT copied: ToolItemManager.Awake derives it from crestList by name.
internal static class ToolItemManagerBootstrap {
    private const string GoName = "Silksong_ToolItemManager";

    internal static object Ensure() {
        var mgr = ManagerSingletonBootstrap.BringUp(typeof(Silksong::ToolItemManager), GoName, "toolItems",
            "crestList");
        if (mgr == null) return new { error = "ToolItemManager bring-up failed (see log)" };
        return Diag();
    }

    // Diagnostic: confirm the singleton resolves and the serialized lists are populated, and probe GetCrestByName
    // (which relies on the SO's OnEnable-built name dictionary — verifies that fired on bundle load).
    internal static object Diag() {
        var mgr = Silksong::ToolItemManager.SilentInstance;
        if (mgr == null) return new { error = "ToolItemManager singleton null (Ensure not run / spawn first)" };
        var crests = Silksong::ToolItemManager.GetAllCrests();
        var tools = Silksong::ToolItemManager.GetUnlockedTools().ToList();
        var crestId = Silksong::PlayerData.instance?.CurrentCrestID;
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

    internal static void Cleanup() {
        ManagerSingletonBootstrap.Destroy(GoName);
    }
}
