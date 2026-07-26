extern alias Silksong;

namespace HornetInHallownest.Playground;

// Bring up Silksong's `CollectableItemManager` (inventory item panes deref it). Copies its two serialized assets off
// the _GameManager prefab onto a single-manager GO — see ManagerSingletonBootstrap.
internal static class CollectableItemManagerBootstrap {
    private const string GoName = "Silksong_CollectableItemManager";

    internal static object Ensure() {
        var mgr = ManagerSingletonBootstrap.BringUp(
            typeof(Silksong::CollectableItemManager), GoName, "masterList", "invalidTemplate");
        if (mgr == null) return new { error = "CollectableItemManager bring-up failed (see log)" };
        return new { ok = true, instanceSet = Silksong::CollectableItemManager.SilentInstance != null };
    }

    internal static void Cleanup() {
        ManagerSingletonBootstrap.Destroy(GoName);
    }
}
