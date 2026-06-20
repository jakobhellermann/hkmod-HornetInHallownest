extern alias Silksong;

namespace HornetPlayer.Playground;

// SURGICAL bring-up of Silksong's `CollectableItemManager` (open item #6) — the singleton the inventory's item panes
// deref (it owns the collectable master list + answers IsInHiddenMode). Same mechanism as ToolItemManager: copy its
// two serialized assets (`masterList`, `invalidTemplate`) off the _GameManager prefab onto a fresh single-manager GO,
// run only its (trivial) Awake. See ManagerSingletonBootstrap.
internal static class CollectableItemManagerBootstrap {
    private const string GoName = "Silksong_CollectableItemManager";

    internal static object Ensure() {
        var mgr = ManagerSingletonBootstrap.BringUp(
            typeof(Silksong::CollectableItemManager), GoName, "masterList", "invalidTemplate");
        if (mgr == null) return new { error = "CollectableItemManager bring-up failed (see log)" };
        return new { ok = true, instanceSet = Silksong::CollectableItemManager.SilentInstance != null };
    }

    internal static void Cleanup() => ManagerSingletonBootstrap.Destroy(GoName);
}
