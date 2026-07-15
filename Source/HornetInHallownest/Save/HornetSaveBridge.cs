extern alias Silksong;
using System;
using HornetPlayer.Playground;
using Newtonsoft.Json;
using Newtonsoft.Json.UnityConverters;

namespace HornetPlayer.HornetInHallownest.Save;

// Persists Hornet's Silksong PlayerData across HK's save/load (HornetPlayerMod delegates its ILocalSettings here).
// Snapshot() <- OnSaveLocal (SaveGame); Stash() <- OnLoadLocal (LoadGame). Load runs before the hero is spawned, so
// Stash keeps the JSON and ApplyPending() applies it post-spawn — once (pending clears on apply).
internal static class HornetSaveBridge {
    // Which hero to record as active in the next snapshot, overriding live HeroSwitch. Set by the quit-to-menu hook
    // before it force-switches to the Knight (camera handback), so the follow-up autosave doesn't record Knight.
    internal static bool? SaveActiveOverride;

    // Mirror Silksong's own PlayerData serializer: the Unity converters emit clean {x,y,…} for Vector/Color and skip
    // computed struct properties (the self-referencing-loop cause). Copied into fresh settings, not the shared default.
    private static readonly JsonSerializerSettings saveSettings = BuildSettings();

    private static string? pendingPlayerData;
    private static bool? pendingHornetActive;

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
        return new HornetSaveData {
            Version = 1,
            PlayerData = spd != null ? JsonConvert.SerializeObject(spd, saveSettings) : null,
            HornetActive = SaveActiveOverride ?? HeroSwitch.HornetActive
        };
    }

    internal static void Stash(HornetSaveData? data) {
        pendingPlayerData = data?.PlayerData;
        pendingHornetActive = data?.HornetActive;
        ApplyPending(); // apply now if the hero already exists; otherwise deferred to the post-spawn call
    }

    // Apply the stashed save post-spawn, overriding the bootstrap's default grants (the save is the truth).
    internal static void ApplyPending() {
        // Gate on the spawned hero, not PlayerData.instance (a singleton that survives despawn): during a menu->game
        // load it's non-null before the hero exists, so restoring then would consume the pending state with no hero.
        var hero = BundleSpike.RealHero;
        if (hero == null) return; // hero not spawned yet — the post-spawn hook will call us again
        var spd = Silksong::PlayerData.instance;
        if (spd == null) return;

        SaveActiveOverride = null; // back in gameplay: live HeroSwitch state is authoritative for future saves again

        if (pendingPlayerData != null)
            try {
                JsonConvert.PopulateObject(pendingPlayerData, spd, saveSettings);
            } catch (Exception e) {
                Log.Error($"[HornetSave] PopulateObject failed: {e.Message}");
            } finally {
                pendingPlayerData = null;
            }

        if (pendingHornetActive == null) return;
        
        var want = pendingHornetActive.Value ? ActiveHero.Hornet : ActiveHero.Knight;
        if (HeroSwitch.Active != want) HeroSwitch.SetActive(want);
        pendingHornetActive = null;
    }
}
