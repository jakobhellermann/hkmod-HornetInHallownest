extern alias Silksong;
extern alias SilksongLoc;
using System;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using SEnemyJournalManager = Silksong::EnemyJournalManager;
using SEnemyJournalRecord = Silksong::EnemyJournalRecord;
using SJournalItemManager = Silksong::JournalItemManager;
using SKillData = Silksong::EnemyJournalKillData.KillData;
using SLocalisedString = SilksongLoc::TeamCherry.Localization.LocalisedString;

namespace HornetInHallownest.Modules;

// Hornet's Hunter's Journal is the Knight's: it opens once he holds HK's journal and lists HK's entries, in HK's order,
// with his kills.
public sealed class HkJournalModule : ModuleBase {
    // Silksong has a "Journal" sheet of its own.
    private const string HkJournalSheet = LanguageSyncModule.HkSheetPrefix + "Journal";

    private static readonly Dictionary<SEnemyJournalRecord, HkJournalEntry> entries = [];
    private static readonly List<SEnemyJournalRecord> records = [];

    public override string Id => "hk-journal";

    public override void Initialize() {
        Detour(typeof(SJournalItemManager), nameof(SJournalItemManager.IsAvailable), OnIsAvailable);
        Detour(typeof(SJournalItemManager), "GetItems", OnGetItems);
        Detour(typeof(SEnemyJournalManager), nameof(SEnemyJournalManager.GetKillData), OnGetKillData);
        Detour(typeof(SEnemyJournalManager), nameof(SEnemyJournalManager.SetJournalSeen), OnSetJournalSeen);
    }

    protected override void OnDeinitialize() {
        foreach (var record in records) UnityEngine.Object.Destroy(record);
        records.Clear();
        entries.Clear();
    }

    // The pane itself already requires hasJournal, which PlayerDataSyncModule mirrors from the Knight's.
    private static bool OnIsAvailable(Func<SJournalItemManager, bool> orig, SJournalItemManager self) => true;

    private static List<SEnemyJournalRecord> OnGetItems(Func<SJournalItemManager, List<SEnemyJournalRecord>> orig,
        SJournalItemManager self) {
        if (records.Count == 0) CreateRecords();
        return records.Where(record => record.IsVisible).ToList();
    }

    private static SKillData OnGetKillData(Func<SEnemyJournalRecord?, SKillData> orig, SEnemyJournalRecord? record) {
        if (record is null || !entries.TryGetValue(record, out var entry)) return orig(record);
        var pd = PlayerData.instance;
        return new SKillData {
            Kills = entry.Kills(pd),
            HasBeenSeen = !pd.GetBool("newData" + entry.PlayerDataName)
        };
    }

    private static void OnSetJournalSeen(Action<SEnemyJournalRecord?> orig, SEnemyJournalRecord? record) {
        if (record is not null && entries.TryGetValue(record, out var entry))
            PlayerData.instance.SetBool("newData" + entry.PlayerDataName, false);
        else orig(record);
    }

    // HK counts an entry's kills down from the number its new save starts with.
    private static void CreateRecords() {
        var journalList = GameManager.instance.inventoryFSM.GetComponentInChildren<JournalList>(true);
        if (!journalList) return;
        var newSave = (PlayerData)Activator.CreateInstance(typeof(PlayerData), true);
        foreach (var prefab in journalList.list) {
            if (!prefab || !prefab.TryGetComponent<JournalEntryStats>(out var stats)) continue;
            var record = ScriptableObject.CreateInstance<SEnemyJournalRecord>();
            record.name = "HK " + stats.playerDataName;
            record.SetFieldValue("iconSprite",
                prefab.transform.Find("Portrait")?.GetComponent<SpriteRenderer>().sprite);
            record.SetFieldValue("enemySprite", stats.sprite);
            record.SetFieldValue("displayName", Text("NAME_" + stats.convoName));
            record.SetFieldValue("description", Text("DESC_" + stats.convoName));
            record.SetFieldValue("notes", Text("NOTE_" + stats.convoName));
            var requiredKills = Math.Max(1, newSave.GetFieldValue<int>("kills" + stats.playerDataName));
            record.SetFieldValue("killsRequired", requiredKills);
            entries[record] = new HkJournalEntry(stats.playerDataName, requiredKills);
            records.Add(record);
        }
    }

    private static SLocalisedString Text(string key) => new() { Sheet = HkJournalSheet, Key = key };
}

internal sealed record HkJournalEntry(string PlayerDataName, int RequiredKills) {
    // An entry shows once killed; an entry HK starts at no kills to go (the tutorial's Crawler) counts as complete.
    internal int Kills(PlayerData pd) {
        if (!pd.GetBool("killed" + PlayerDataName)) return 0;
        var left = Math.Max(0, pd.GetInt("kills" + PlayerDataName));
        return Math.Max(1, RequiredKills - left);
    }
}
