extern alias Silksong;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.UnityConverters;

namespace HornetPlayer.Playground;

// Bridges Hornet's Silksong PlayerData to HK's save system. HornetPlayerMod implements ILocalSettings<HornetSaveData>
// and delegates here. This piggybacks on HK's save lifecycle so it behaves natively:
//   - Snapshot()  <- OnSaveLocal, fired at GameManager.SaveGame (bench rest / autosave).
//   - Stash()     <- OnLoadLocal, fired at GameManager.LoadGame.
//
// "Stash on load": OnLoadLocal runs DURING HK's LoadGame, before the scene finishes loading and the spawn/bootstrap has
// created Silksong's PlayerData.instance. So we can't apply immediately — we stash the JSON and ApplyPending() once the
// instance exists (called post-spawn from the FinishedEnteringScene hook). pending clears on apply, so a load applies
// exactly once; ApplyPending is a no-op otherwise (safe to call every scene entry).
internal static class HornetSaveBridge {
    private static string? pendingPlayerData;

    // Mirror Silksong's own PlayerData serializer (SaveDataUtility): the Unity type converters serialize Vector2/Vector3/
    // Color/Quaternion as clean {x,y,…} and NEVER touch computed struct properties (Vector2.normalized/magnitude), which
    // is what caused the self-referencing-loop throw — a proper fix, not just ReferenceLoopHandling.Ignore. Same four
    // handling flags as SaveDataUtility. Converters are copied into a fresh settings so we don't mutate the shared
    // UnityConverterInitializer.defaultUnityConvertersSettings.
    private static readonly JsonSerializerSettings SaveSettings = BuildSettings();

    private static JsonSerializerSettings BuildSettings() {
        var s = new JsonSerializerSettings {
            DefaultValueHandling = DefaultValueHandling.Populate,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        foreach (var c in UnityConverterInitializer.defaultUnityConvertersSettings.Converters) s.Converters.Add(c);
        return s;
    }

    internal static HornetSaveData Snapshot() {
        var spd = Silksong::PlayerData.instance;
        var data = new HornetSaveData {
            Version = 1,
            PlayerData = spd != null ? JsonConvert.SerializeObject(spd, SaveSettings) : null
        };
        Log.Info($"[HornetSave] snapshot PlayerData ({(data.PlayerData?.Length ?? 0)} chars)");
        return data;
    }

    internal static void Stash(HornetSaveData? data) {
        pendingPlayerData = data?.PlayerData;
        if (pendingPlayerData != null) Log.Info("[HornetSave] stashed loaded PlayerData (apply on spawn)");
        ApplyPending(); // apply now if the instance already exists; otherwise deferred
    }

    // Overwrite the live PlayerData with the stashed save. Call after PlayerData.instance is created (post-spawn). The
    // bootstrap's default grants run first; this then overrides them with the saved values (the save is the truth).
    internal static void ApplyPending() {
        if (pendingPlayerData == null) return;
        var spd = Silksong::PlayerData.instance;
        if (spd == null) return; // not ready yet — a later spawn will call us again
        try {
            JsonConvert.PopulateObject(pendingPlayerData, spd, SaveSettings);
            Log.Info("[HornetSave] applied loaded PlayerData onto live instance");
        } catch (Exception e) {
            Log.Error($"[HornetSave] PopulateObject failed: {e.Message}");
        } finally {
            pendingPlayerData = null;
        }
    }
}
