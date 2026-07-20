namespace HornetPlayer.HornetInHallownest.Save;

// Root of the mod's global (cross-save) settings, serialized to HornetPlayerMod.GlobalSettings.json.
public sealed class HornetGlobalSettings {
    public InputSettings Controls = new();
}
