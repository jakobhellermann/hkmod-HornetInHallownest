// ReSharper disable UnassignedField.Global
namespace HornetInHallownest.Save;

// Root of the mod's global (cross-save) settings, serialized to HornetInHallownestMod.GlobalSettings.json.
public sealed class HornetGlobalSettings {
    // Override of the Silksong install folder. Can point either to `Hollow Knight Silksong_Data` or its parent folder.
    // When null, attempt to autodetect next to hollow knight.
    public string? SilksongPath;

    public InputSettings Controls = new();

    // When true, switching will keep both the Knight and Hornet active and controllable, however only the "primary"
    // target will be able to interact, get targeted by enemies, show HUD, etc.
    public bool BothActive;
}
