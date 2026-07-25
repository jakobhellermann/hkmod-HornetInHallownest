// ReSharper disable UnassignedField.Global
namespace HornetPlayer.HornetInHallownest.Save;

// Root of the mod's global (cross-save) settings, serialized to HornetPlayerMod.GlobalSettings.json.
public sealed class HornetGlobalSettings {
    // Override of the Silksong install folder. Can point either to `Hollow Knight Silksong_Data` or its parent folder.
    // When null, attempt to autodetect next to hollow knight.
    public string? SilksongPath;

    public InputSettings Controls = new();
}
