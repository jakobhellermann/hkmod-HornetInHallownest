namespace HornetPlayer.Playground;

// Aggregates the Silksong singleton/manager bring-ups the hero needs before its prefab activates. Order matters
// (GameCameras before the hero's FSMs Awake; GlobalSettings before anything reads it); each Ensure/Apply is idempotent.
internal static class GlobalsBootstrap {
    internal static void Ensure() {
        SilksongBootstrap.Ensure(); // GameManager/PlayerData/InputHandler/CameraController singletons the hero derefs
        ToolItemManagerBootstrap.Ensure(); // tools/crests/nail-art data source (open item #6)
        CollectableItemManagerBootstrap.Ensure(); // inventory items (open item #6)
        GlobalSettingsBootstrap.Apply(); // GlobalSettings._instance from the loaded SOs (bypass Addressables)
        GameCamerasBootstrap.Ensure(); // GameCameras.instance + CameraTarget before the hero's FSMs Awake
        PlayMakerUnity2dBootstrap.Ensure(); // "PlayMaker Unity 2D" manager so collision/trigger proxies stay enabled
    }
}
