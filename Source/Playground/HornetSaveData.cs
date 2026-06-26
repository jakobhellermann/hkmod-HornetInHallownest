namespace HornetPlayer.Playground;

// Per-save-slot data persisted for Hornet, stored INSIDE HK's save file. HornetPlayerMod implements
// ILocalSettings<HornetSaveData>, so the modding API serializes this (Newtonsoft -> ModSavegameData.modData) at HK's
// native save/load points (GameManager.SaveGame on bench/autosave -> OnSaveLocal; GameManager.LoadGame -> OnLoadLocal).
//
// Versioned + extensible: today it only carries PlayerData, but new persisted state goes in as additional fields (e.g.
// SceneData, a tools blob, …). Old saves missing a field deserialize it to null/default, so adding fields is backward-
// compatible. Bump Version + branch in HornetSaveBridge when a field's meaning changes incompatibly.
public class HornetSaveData {
    public int Version = 1;

    // The whole Silksong PlayerData as a JSON string (JsonConvert; Silksong's PlayerData is
    // [JsonObject(MemberSerialization.Fields)], the same shape its own save uses). Kept as a string so it round-trips
    // onto the LIVE PlayerData.instance via PopulateObject instead of replacing the instance the hero already holds.
    public string? PlayerData;
}
