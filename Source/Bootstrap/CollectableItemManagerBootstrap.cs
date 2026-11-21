extern alias Silksong;

namespace HornetInHallownest.Bootstrap;

internal static class CollectableItemManagerBootstrap {
    private const string GoName = "Silksong_CollectableItemManager";

    internal static void Ensure() {
        ManagerSingletonBootstrap.BringUp(typeof(Silksong::CollectableItemManager), GoName, "masterList", "invalidTemplate");
    }

    internal static void Cleanup() {
        ManagerSingletonBootstrap.Destroy(GoName);
    }
}
