namespace HornetPlayer.HornetInHallownest.Save;

// Per-save-slot data persisted for Hornet via the modding API. 
public class HornetSaveData {
    // Which hero the player controlled when the save was written: true = Hornet, false = Knight. 
    public bool HornetActive;

    // Hornet's full Silksong PlayerData. Kept as string for control over serialization settings.
    // TODO: save as proper JSON?
    public string? PlayerData;
    public int Version = 1;
}
