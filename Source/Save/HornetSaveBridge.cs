extern alias Silksong;
using System;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.UnityConverters;
using HornetInHallownest.Modules;
using HornetInHallownest.Util;
using Modding;

namespace HornetInHallownest.Save;

// Persists Hornet's Silksong PlayerData in modded savefile.
// Since the modded settings may be loaded before the hornet is activated, stash to-be-loaded settings away until she is.
public sealed class HornetSaveBridge : ModuleBase {
    public override string Id => "save-bridge";

    public override void Initialize() => ModHooks.NewGameHook += OnNewGame;
    protected override void OnDeinitialize() => ModHooks.NewGameHook -= OnNewGame;

    // Override set by quit to menu before switching back to knight, in order to prevent always autosaving knight.
    internal static bool? SaveActiveOverride;

    // Matches Silksong's PlayerData serializer
    private static JsonSerializerSettings SaveSettings => field ??= BuildSettings();

    private static string? pendingPlayerData;
    private static bool? pendingHornetActive;

    private static void OnNewGame() => Silksong::PlayerData.CreateNewSingleton(false);

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
            PlayerData = JsonConvert.SerializeObject(spd, SaveSettings),
            HornetActive = SaveActiveOverride ?? HeroSwitch.HornetActive
        };
    }

    internal static void Stash(HornetSaveData? data) {
        pendingPlayerData = data?.PlayerData;
        pendingHornetActive = data?.HornetActive;
        ApplyPending(); // apply now if the hero already exists; otherwise deferred to the post-spawn call
    }

    internal static void ApplyPending() {
        var hero = HornetSpawner.Hornet;
        if (!hero) return; // hero not spawned yet, the post-spawn hook will call us again
        var spd = Silksong::PlayerData.instance;

        SaveActiveOverride = null;

        if (pendingPlayerData != null)
            try {
                JsonConvert.PopulateObject(pendingPlayerData, spd, SaveSettings);
                // The restore bypasses SetEquippedTools, so refresh them manually.
                ToolItemManagerBootstrap.RefreshBoundAttackTools();
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
