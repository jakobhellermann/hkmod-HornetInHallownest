namespace HornetPlayer.Playground;

// Brings up the Silksong singletons/managers the hero needs BEFORE its prefab is activated (so their Awake/FSMs find
// a populated environment). Aggregates the individual bootstraps behind one call so the spawner doesn't depend on the
// bring-up details — as bring-up is validated/reworked, only this stays the spawner's contract.
//
// Order matters: GameCameras must exist before the hero's FSMs Awake (else camera errors), GlobalSettings before
// anything reads it, etc. Each Ensure/Apply is itself idempotent.
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
