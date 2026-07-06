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
    private static bool? pendingHornetActive;

    // Which hero to record as active in the NEXT snapshot, overriding the live HeroSwitch state. Set by the quit-to-menu
    // hook before it force-switches to the Knight (needed for the camera handback): ReturnToMainMenu then autosaves, and
    // without this the save would record Knight and clobber the "was playing Hornet" state. Cleared once back in gameplay
    // (ApplyPending), where the live HeroSwitch state is authoritative again.
    internal static bool? SaveActiveOverride;

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
            PlayerData = spd != null ? JsonConvert.SerializeObject(spd, SaveSettings) : null,
            HornetActive = SaveActiveOverride ?? HeroSwitch.HornetActive
        };
        Log.Info(
            $"[HornetSave] snapshot PlayerData ({data.PlayerData?.Length ?? 0} chars), hornetActive={data.HornetActive}");
        return data;
    }

    internal static void Stash(HornetSaveData? data) {
        pendingPlayerData = data?.PlayerData;
        pendingHornetActive = data?.HornetActive;
        if (pendingPlayerData != null) Log.Info("[HornetSave] stashed loaded PlayerData (apply on spawn)");
        ApplyPending(); // apply now if the instance already exists; otherwise deferred
    }

    // Apply the stashed save. Call after the hero is spawned (post-spawn). The bootstrap's default grants run first; this
    // then overrides them with the saved values (the save is the truth). Both pending bits clear on apply, so a load
    // applies exactly once and ApplyPending is a no-op on later scene entries.
    internal static void ApplyPending() {
        // Gate on the REAL spawned hero, not PlayerData.instance: Silksong's PlayerData.instance is a static singleton
        // that survives a despawn, so during a menu->game load it's non-null while the hero isn't spawned yet. Restoring
        // the active hero then (SetActive needs RealHero) would no-op but still consume pendingHornetActive -> the real
        // post-spawn restore finds nothing and we stay on the Knight.
        var hero = BundleSpike.RealHero;
        if (hero == null) return; // hero not spawned yet — the post-spawn hook will call us again
        var spd = Silksong::PlayerData.instance;
        if (spd == null) return;

        SaveActiveOverride = null; // back in gameplay: live HeroSwitch state is authoritative for future saves again

        if (pendingPlayerData != null)
            try {
                JsonConvert.PopulateObject(pendingPlayerData, spd, SaveSettings);
                Log.Info("[HornetSave] applied loaded PlayerData onto live instance");
            } catch (Exception e) {
                Log.Error($"[HornetSave] PopulateObject failed: {e.Message}");
            } finally {
                pendingPlayerData = null;
            }

        if (pendingHornetActive != null) {
            var want = pendingHornetActive.Value ? ActiveHero.Hornet : ActiveHero.Knight;
            // Guard on a real change so a Knight-save doesn't trigger a redundant switch (camera snap) on every load.
            if (HeroSwitch.Active != want) {
                HeroSwitch.SetActive(want);
                Log.Info($"[HornetSave] restored active hero: {want}");
            }

            pendingHornetActive = null;
        }
    }
}
