extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using Modding;
using SPlayerData = Silksong::PlayerData;
using SToolCrest = Silksong::ToolCrest;
using SToolItem = Silksong::ToolItem;
using SToolItemManager = Silksong::ToolItemManager;

namespace HornetInHallownest.Modules;

// Hornet's Silksong tools, crests and Eva upgrades are earned through HK's own progression.
public sealed class ProgressionModule : ModuleBase {
    internal readonly record struct Flag(string Field, int Min = 0) {
        internal bool IsSet(PlayerData pd) => Min == 0 ? pd.GetBool(Field) : pd.GetInt(Field) >= Min;
    }

    internal sealed record Rule(string Reward, Flag[] Flags, bool AnyFlag = false) {
        internal bool IsEarned(PlayerData pd) =>
            Flags.Length == 0 || (AnyFlag ? Flags.Any(f => f.IsSet(pd)) : Flags.All(f => f.IsSet(pd)));
    }

    private const int BaseCharmNotches = 3;
    private const int ExtraBlueSlotNotches = 8;
    private const int ExtraYellowSlotNotches = 11;
    private const string StartingCrest = "Hunter";

    private static Flag Charm(int id) => new($"gotCharm_{id}");

    private static Rule Earned(string reward, params Flag[] flags) => new(reward, flags);

    internal static readonly Rule[] ToolRules = [
        Earned("Straight Pin", new Flag("falseKnightDefeated")),
        Earned("Tri Pin", Charm(25)), // Fragile Strength
        Earned("Sting Shard", new Flag("killedMimicSpider")), // Nosk
        Earned("Tack", new Flag("hasAcidArmour")), // Isma's Tear
        Earned("Harpoon", Charm(15)), // Heavy Blow
        Earned("Curve Claws", Charm(13)), // Mark of Pride
        Earned("Curve Claws Upgraded", new Flag("killedTraitorLord")),
        Earned("Shakra Ring", new Flag("killedMegaMossCharger")),
        Earned("Pimpilo", Charm(10)), // Defender's Crest
        Earned("Conch Drill", Charm(28)), // Shape of Unn
        Earned("WebShot Forge", new Flag("maskBrokenHegemol")),
        Earned("WebShot Architect", new Flag("maskBrokenMonomon")),
        Earned("WebShot Weaver", new Flag("maskBrokenLurien")),
        Earned("Silk Snare", Charm(22)), // Glowing Womb
        Earned("Screw Attack", new Flag("hasLantern")),
        Earned("Cogwork Saw", new Flag("royalCharmState", 3)), // Kingsoul
        Earned("Cogwork Flier", Charm(40)), // Grimmchild
        Earned("Rosary Cannon", new Flag("fragileGreed_unbreakable")),
        Earned("Lightning Rod", new Flag("killedMegaJellyfish")), // Uumuu
        Earned("Flintstone", new Flag("killedGrimm")), // Troupe Master Grimm
        Earned("Lifeblood Syringe", Charm(8)), // Lifeblood Heart
        Earned("Extractor", Charm(29)), // Hiveblood
        Earned("Flea Brew", Charm(32)), // Quick Slash

        Earned("Silk Spear", new Flag("fireballLevel", 1)),
        Earned("Silk Charge", new Flag("fireballLevel", 2)), // Sharpdart
        Earned("Parry", new Flag("quakeLevel", 1)), // Cross Stitch
        Earned("Silk Bomb", new Flag("quakeLevel", 2)), // Rune Rage
        Earned("Thread Sphere", new Flag("screamLevel", 1)), // Thread Storm
        Earned("Silk Boss Needle", new Flag("screamLevel", 2)), // Pale Nails

        Earned("Bell Bind", Charm(5)), // Baldur Shell
        Earned("Quickbind", Charm(7)), // Quick Focus
        Earned("Multibind", Charm(34)), // Deep Focus
        Earned("Reserve Bind", Charm(27)), // Joni's Blessing
        Earned("White Ring", Charm(20)), // Soul Catcher
        Earned("Spool Extender", Charm(21)), // Soul Eater
        Earned("Mosscreep Tool 1", Charm(3)), // Grubsong
        Earned("Mosscreep Tool 2", Charm(35)), // Grubberfly's Elegy
        Earned("Revenge Crystal", Charm(12)), // Thorns of Agony
        Earned("Brolly Spike", Charm(16)), // Sharp Shadow
        Earned("Dazzle Bind", new Flag("dreamNailUpgraded")),
        Earned("Dazzle Bind Upgraded", Charm(38)), // Dreamshield
        Earned("Flea Charm", Charm(33)), // Spell Twister
        Earned("Fractured Mask", Charm(23)), // Fragile Heart
        Earned("Longneedle", Charm(18)), // Longnail
        Earned("Pinstress Tool", Charm(26)), // Nailmaster's Glory
        Earned("Poison Pouch", Charm(17)), // Spore Shroom
        Earned("Quick Sling", Charm(19)), // Shaman Stone
        new("Wisp Lantern", [
            new Flag("killedGhostAladar"), new Flag("killedGhostXero"), new Flag("killedGhostHu"),
            new Flag("killedGhostMarmu"), new Flag("killedGhostNoEyes"), new Flag("killedGhostGalien"),
            new Flag("killedGhostMarkoth")
        ], AnyFlag: true),
        Earned("Zap Imbuement", Charm(11)), // Flukenest
        Earned("Thief Claw", new Flag("collectorDefeated")),

        Earned("Compass", Charm(2)), // Wayward Compass
        Earned("Rosary Magnet", Charm(1)), // Gathering Swarm
        Earned("Sprintmaster", Charm(37)),
        Earned("Scuttlebrace", Charm(31)), // Dashmaster
        Earned("Wallcling", new Flag("defeatedMegaBeamMiner")), // Crystal Guardian
        Earned("Weighted Anklet", Charm(14)), // Steady Body
        Earned("Barbed Wire", Charm(6)), // Fury of the Fallen
        Earned("Magnetite Dice", Charm(4)), // Stalwart Shell
        Earned("Thief Charm", Charm(24)), // Fragile Greed
        Earned("Dead Mans Purse", new Flag("jijiShadeOffered")),
        Earned("Musician Charm", Charm(30)), // Dream Wielder
        Earned("Shell Satchel", Charm(39)) // Weaversong
    ];

    internal static readonly Rule[] CrestRules = [
        Earned(StartingCrest),
        Earned("Hunter_v2", new Flag("journalEntriesCompleted", 60)),
        Earned("Hunter_v3", new Flag("hasHuntersMark")),
        Earned("Spell", new Flag("fireballLevel", 1), new Flag("quakeLevel", 1), new Flag("screamLevel", 1)),
        Earned("Warrior", new Flag("maskBrokenHegemol")),
        Earned("Reaper", new Flag("dreamReward2")),
        Earned("Wanderer", new Flag("quirrelEpilogueCompleted")),
        Earned("Toolmaster", new Flag("nailSmithUpgrades", 4)),
        Earned("Witch", Charm(9)) // Lifeblood Core
    ];

    internal static readonly Rule SylphsongRule = Earned("Sylphsong", new Flag("salubraBlessing"));

    private static readonly HashSet<string> watchedFields = [
        .. ToolRules.Concat(CrestRules).Append(SylphsongRule).SelectMany(r => r.Flags).Select(f => f.Field),
        "charmSlots"
    ];

    private bool applyQueued;

    public override string Id => "progression";

    public override void Initialize() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
        ModHooks.SetPlayerIntHook += OnSetInt;
    }

    protected override void OnDeinitialize() {
        ModHooks.SetPlayerBoolHook -= OnSetBool;
        ModHooks.SetPlayerIntHook -= OnSetInt;
    }

    private bool OnSetBool(string name, bool value) {
        QueueApply(name);
        return value;
    }

    private int OnSetInt(string name, int value) {
        QueueApply(name);
        return value;
    }

    // The hooks run before HK stores the new value, so apply on the next frame.
    private void QueueApply(string field) {
        if (applyQueued || !watchedFields.Contains(field) || !HornetSpawner.HornetRoot) return;
        applyQueued = true;
        StartCoroutine(ApplyNextFrame());
    }

    private IEnumerator ApplyNextFrame() {
        yield return null;
        applyQueued = false;
        Apply();
    }

    // Unlocks what HK's progression has earned and locks the rest, so a save from before this gating catches up too.
    internal static void Apply() {
        var pd = PlayerData.instance;
        var ss = SPlayerData.instance;
        if (pd == null || ss == null) return;
        ss.SeenToolGetPrompt = true;
        ss.SeenToolWeaponGetPrompt = true;

        var earnedTools = ToolRules.Where(r => r.IsEarned(pd)).Select(r => r.Reward).ToHashSet();
        foreach (var tool in SToolItemManager.GetAllTools()) {
            if (!tool) continue;
            var earned = earnedTools.Contains(tool.name);
            if (earned && !tool.IsUnlocked) {
                tool.Unlock(null, SToolItem.PopupFlags.None);
            }
            else if (!earned && tool.IsUnlocked) {
                LockTool(tool);
            }
        }

        var earnedCrests = CrestRules.Where(r => r.IsEarned(pd)).Select(r => r.Reward).ToHashSet();
        var lockedSlotsOpen = Math.Max(0, pd.GetInt("charmSlots") - BaseCharmNotches);
        foreach (var crest in SToolItemManager.GetAllCrests()) {
            if (!crest) continue;
            if (earnedCrests.Contains(crest.name)) {
                crest.Unlock();
                OpenLockedSlots(crest, lockedSlotsOpen);
            }
            else if (crest.IsUnlocked) {
                LockCrest(crest);
            }
        }

        if (!earnedCrests.Contains(ss.CurrentCrestID)) SToolItemManager.SetEquippedCrest(StartingCrest);

        var notches = pd.GetInt("charmSlots");
        ss.UnlockedExtraBlueSlot = notches >= ExtraBlueSlotNotches;
        ss.UnlockedExtraYellowSlot = notches >= ExtraYellowSlotNotches;
        ss.HasBoundCrestUpgrader = SylphsongRule.IsEarned(pd);

        BenchModule.RefreshToolHud();
    }

    // A tool also counts as unlocked while its alternate unlock test passes, so that test is failed too.
    private static void LockTool(SToolItem tool) {
        var test = tool.GetFieldValue<Silksong::PlayerDataTest>("alternateUnlockedTest");
        if (test is { IsDefined: true })
            foreach (var group in test.TestGroups)
            foreach (var condition in group.Tests)
                if (condition.Type == Silksong::PlayerDataTest.TestType.Bool)
                    SPlayerData.instance.SetFieldValue(condition.FieldName, !condition.BoolValue);

        var data = tool.SavedData;
        data.IsUnlocked = false;
        tool.SavedData = data;
        SToolItemManager.RemoveToolFromAllCrests(tool);
    }

    private static void LockCrest(SToolCrest crest) {
        var data = crest.SaveData;
        data.IsUnlocked = false;
        crest.SaveData = data;
    }

    // Silksong opens a crest's locked slots with Memory Lockets; here each charm notch above the starting three opens
    // one more on every crest. A slot that closes again drops its tool.
    private static void OpenLockedSlots(SToolCrest crest, int count) {
        var config = crest.Slots;
        if (config == null) return;
        var data = crest.SaveData;
        var slots = new List<Silksong::ToolCrestsData.SlotData>(config.Length);
        var openedCount = 0;
        for (var i = 0; i < config.Length; i++) {
            var isUnlocked = !config[i].IsLocked || openedCount++ < count;
            var equippedTool = isUnlocked && data.Slots != null && i < data.Slots.Count
                ? data.Slots[i].EquippedTool
                : null;
            slots.Add(new Silksong::ToolCrestsData.SlotData { EquippedTool = equippedTool, IsUnlocked = isUnlocked });
        }

        data.Slots = slots;
        crest.SaveData = data;
    }
}
