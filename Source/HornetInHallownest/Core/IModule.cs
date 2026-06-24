namespace HornetPlayer.HornetInHallownest.Core;

// A unit of the mod's runtime lifecycle. Modules are registered in a single ordered list (see ModuleHost):
// Initialize() runs in registration order at mod load, Deinitialize() in REVERSE order at unload — so teardown
// order falls out of the registration order automatically (no hand-maintained mirror list).
//
// Id is a stable, addressable name. The validation runner uses it to disable a single module in a live instance
// ("is this shim still needed?") and re-enable it afterwards, without a game restart.
public interface IModule {
    string Id { get; }
    void Initialize();
    void Deinitialize();
}
