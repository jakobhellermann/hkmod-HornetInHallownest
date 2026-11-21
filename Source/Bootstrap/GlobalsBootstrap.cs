namespace HornetInHallownest.Bootstrap;

// The Silksong singletons and managers the hero needs before her prefab activates, in dependency order.
internal static class GlobalsBootstrap {
    internal static void Ensure() {
        SilksongBootstrap.Ensure();
        ToolItemManagerBootstrap.Ensure();
        CollectableItemManagerBootstrap.Ensure();
        GlobalSettingsBootstrap.Apply();
        GameCamerasBootstrap.Ensure();
        PlayMakerUnity2dBootstrap.Ensure();
    }
}
