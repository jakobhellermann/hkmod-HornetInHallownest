extern alias Silksong;
extern alias SilksongLoc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker.Actions;
using Modding;
using UnityEngine;
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
    private const string CharmGetMsg = "Charm Get Msg";
    private static readonly string[] silkshots = ["WebShot Weaver", "WebShot Forge", "WebShot Architect"];
    private static readonly string[] hkItemMsgs = ["UI Msg Get Item(Clone)", "Relic Get Msg(Clone)"];
    private const string GetItemMsgEnd = "GET ITEM MSG END";
    private const string MemoryLocket = "Crest Socket Unlocker";
    private const string PromptsBundle = "prompts_assets_all.bundle";
    private const string PowerUpMsgPath = "Assets/Prefabs/UI/Hornet UI/Ancestral_Art_Get_Prompt.prefab";
    private const string ExtraSlotMsgPath = "Assets/Prefabs/UI/Messages/Extra Tool Slot UI Msg.prefab";
    private const string SkillMsgPath = "Assets/Prefabs/UI/Hornet UI/Silk_Skill_Get_Prompt.prefab";

    private static AssetBundle? promptsBundle;
    private static int heldMsgCount;
    private static readonly HashSet<GameObject> hiddenMsgs = [];

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

    // HK abilities whose Silksong counterpart (PlayerDataSyncModule) has a full-screen message of its own.
    private static readonly Dictionary<string, Silksong::PowerUpGetMsg.PowerUps> abilityMsgs = new() {
        ["hasDash"] = Silksong::PowerUpGetMsg.PowerUps.Sprint,
        ["hasWalljump"] = Silksong::PowerUpGetMsg.PowerUps.WallJump,
        ["hasSuperDash"] = Silksong::PowerUpGetMsg.PowerUps.HarpoonDash,
        ["hasKingsBrand"] = Silksong::PowerUpGetMsg.PowerUps.SuperJump
    };

    internal static readonly Rule[] LocketRules = [
        Earned("Grubfather 10", new Flag("grubsCollected", 10)),
        Earned("Grubfather 16", new Flag("grubsCollected", 16)),
        Earned("Grubfather 23", new Flag("grubsCollected", 23)),
        Earned("Grubfather 31", new Flag("grubsCollected", 31)),
        Earned("Grubfather 38", new Flag("grubsCollected", 38)),
        Earned("Wanderer's Journal", new Flag("foundTrinket1")),
        Earned("Hallownest Seal", new Flag("foundTrinket2")),
        Earned("King's Idol", new Flag("foundTrinket3")),
        Earned("Arcane Egg", new Flag("foundTrinket4")),
        Earned("Seer 100", new Flag("dreamReward1")),
        Earned("Seer 700", new Flag("dreamReward5")),
        Earned("Seer 1800", new Flag("dreamReward9"))
    ];

    private static readonly HashSet<string> watchedFields = [
        .. ToolRules.Concat(CrestRules).Concat(LocketRules).Append(SylphsongRule).SelectMany(r => r.Flags)
            .Select(f => f.Field),
        "charmSlots"
    ];

    private bool applyQueued;

    public override string Id => "progression";

    public override void Initialize() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
        ModHooks.SetPlayerIntHook += OnSetInt;
        Detour(typeof(CreateObject), nameof(CreateObject.OnEnter), OnCreateObject);
        Detour(typeof(CreateUIMsgGetItem), nameof(CreateUIMsgGetItem.OnEnter), OnCreateUIMsgGetItem);
    }

    protected override void OnDeinitialize() {
        ModHooks.SetPlayerBoolHook -= OnSetBool;
        ModHooks.SetPlayerIntHook -= OnSetInt;
    }

    private bool OnSetBool(string name, bool value) {
        QueueApply(name);
        if (value && HeroSwitch.HornetActive && abilityMsgs.TryGetValue(name, out var powerUp) &&
            !PlayerData.instance.GetBool(name))
            ShowPowerUpMsg(powerUp);
        return value;
    }

    private int OnSetInt(string name, int value) {
        QueueApply(name);
        return value;
    }

    // HK's message for a picked-up charm shows where Hornet's message for its reward does, so it isn't created while
    // she's active. The pickup carries on without waiting for it.
    private static void OnCreateObject(Action<CreateObject> orig, CreateObject self) {
        var prefab = self.gameObject.Value;
        if (HeroSwitch.HornetActive && prefab && prefab.name == CharmGetMsg) {
            self.Finish();
            return;
        }

        orig(self);
    }

    // Some pickups, such as the Abyss Shriek, bring HK's message up only after Hornet's message for the same reward.
    private static void OnCreateUIMsgGetItem(Action<CreateUIMsgGetItem> orig, CreateUIMsgGetItem self) {
        orig(self);
        if (heldMsgCount > 0) HideMsg(self.storeObject.Value);
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
        Apply(showPopups: HeroSwitch.HornetActive);
    }

    internal static void Apply(bool showPopups = false) {
        var pd = PlayerData.instance;
        var ss = SPlayerData.instance;
        if (pd == null || ss == null) return;
        ss.SeenToolGetPrompt = true;
        ss.SeenToolWeaponGetPrompt = true;

        var earnedTools = ToolRules.Where(r => r.IsEarned(pd)).Select(r => r.Reward).ToHashSet();
        KeepOneSilkshot(earnedTools);
        foreach (var tool in SToolItemManager.GetAllTools()) {
            if (!tool) continue;
            var earned = earnedTools.Contains(tool.name);
            // A silk skill also counts as unlocked by its PlayerData flag, which PlayerDataSyncModule sets from the spell
            // before this runs.
            var skill = tool as Silksong::ToolItemSkill;
            if (earned && (!tool.IsUnlocked || (skill && !tool.SavedData.IsUnlocked))) {
                // Silksong announces each silk skill with its own full-screen message.
                tool.Unlock(null, showPopups && !skill ? SToolItem.PopupFlags.ItemGet : SToolItem.PopupFlags.None);
                MarkUnlocked(tool);
                if (showPopups && skill) ShowSkillMsg(skill);
            }
            else if (!earned && tool.IsUnlocked) {
                LockTool(tool);
            }
        }

        var earnedCrests = CrestRules.Where(r => r.IsEarned(pd)).Select(r => r.Reward).ToHashSet();
        foreach (var crest in SToolItemManager.GetAllCrests()) {
            if (!crest) continue;
            if (earnedCrests.Contains(crest.name) && !crest.IsUnlocked && !IsSuperseded(crest)) {
                crest.Unlock();
                if (showPopups) ShowCrestMsg(crest);
            }
            else if (!earnedCrests.Contains(crest.name) && crest.IsUnlocked) {
                LockCrest(crest);
            }
        }

        if (!earnedCrests.Contains(ss.CurrentCrestID)) SToolItemManager.SetEquippedCrest(StartingCrest);
        GrantMemoryLockets(pd, ss, showPopups);

        var notches = pd.GetInt("charmSlots");
        var extraBlueSlot = notches >= ExtraBlueSlotNotches;
        var extraYellowSlot = notches >= ExtraYellowSlotNotches;
        var extraSlotCount = (ss.UnlockedExtraBlueSlot ? 1 : 0) + (ss.UnlockedExtraYellowSlot ? 1 : 0);
        if (showPopups && extraBlueSlot && !ss.UnlockedExtraBlueSlot)
            ShowExtraSlotMsg(Silksong::ToolItemType.Blue, ++extraSlotCount);
        if (showPopups && extraYellowSlot && !ss.UnlockedExtraYellowSlot)
            ShowExtraSlotMsg(Silksong::ToolItemType.Yellow, ++extraSlotCount);
        ss.UnlockedExtraBlueSlot = extraBlueSlot;
        ss.UnlockedExtraYellowSlot = extraYellowSlot;
        var sylphsong = SylphsongRule.IsEarned(pd);
        if (showPopups && sylphsong && !ss.HasBoundCrestUpgrader)
            ShowPowerUpMsg(Silksong::PowerUpGetMsg.PowerUps.EvaHeal);
        ss.HasBoundCrestUpgrader = sylphsong;

        BenchModule.RefreshToolHud();
    }

    // Silksong gives only one of its three Silkshots, so Hornet keeps the one from the first dreamer she defeats.
    private static void KeepOneSilkshot(HashSet<string> earnedTools) {
        var earnedSilkshots = silkshots.Where(earnedTools.Contains).ToList();
        if (earnedSilkshots.Count < 2) return;
        var keptSilkshot =
            earnedSilkshots.FirstOrDefault(name => SToolItemManager.GetToolByName(name) is { IsUnlocked: true }) ??
            earnedSilkshots[0];
        earnedTools.ExceptWith(earnedSilkshots.Where(name => name != keptSilkshot));
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

    // Unlock leaves the saved data alone for a tool that already counts as unlocked.
    private static void MarkUnlocked(SToolItem tool) {
        var data = tool.SavedData;
        if (data.IsUnlocked) return;
        data.IsUnlocked = true;
        tool.SavedData = data;
    }

    private static void LockCrest(SToolCrest crest) {
        var data = crest.SaveData;
        data.IsUnlocked = false;
        crest.SaveData = data;
    }

    internal static int EarnedMemoryLockets(PlayerData pd) =>
        Math.Max(0, pd.GetInt("charmSlots") - BaseCharmNotches) + LocketRules.Count(r => r.IsEarned(pd));

    // An upgraded crest carries over its previous version's slots, so only the newest version counts.
    private static void GrantMemoryLockets(PlayerData pd, SPlayerData ss, bool showPopups) {
        var openedSlots = 0;
        foreach (var crest in SToolItemManager.GetAllCrests()) {
            if (!crest || !crest.IsUnlocked || IsSuperseded(crest)) continue;
            var config = crest.Slots;
            var slots = crest.SaveData.Slots;
            if (config == null || slots == null) continue;
            for (var i = 0; i < config.Length && i < slots.Count; i++)
                if (config[i].IsLocked && slots[i].IsUnlocked)
                    openedSlots++;
        }

        var data = ss.Collectables.GetData(MemoryLocket);
        var heldLockets = Math.Max(0, EarnedMemoryLockets(pd) - openedSlots);
        if (showPopups && heldLockets > data.Amount) {
            Silksong::CollectableItemManager.GetItemByName(MemoryLocket).Collect(heldLockets - data.Amount);
            return;
        }

        data.Amount = heldLockets;
        ss.Collectables.SetData(MemoryLocket, data);
        // The inventory keeps listing the previous save's items until this changes; Silksong's own save load bumps it.
        Silksong::CollectableItemManager.IncrementVersion();
    }

    private static void ShowCrestMsg(SToolCrest crest) {
        var prefabName = crest.name switch {
            "Hunter_v2" or "Hunter_v3" => "Tool Crest Upgrade UI Msg",
            "Toolmaster" => "Tool Crest UI Msg Architect Variant",
            _ => "Tool Crest UI Msg"
        };
        var prefab = LoadPromptPrefab($"Assets/Prefabs/UI/Messages/{prefabName}.prefab");
        if (!prefab || !prefab.GetComponent<Silksong::ToolCrestUIMsg>()) {
            Silksong::CollectableUIMsg.Spawn(crest);
            return;
        }

        ShowHeldMsg(afterMsg => Silksong::ToolCrestUIMsg.Spawn(crest, prefab, afterMsg));
        // Silksong evolves the Hunter Crest with another message, titled with this line.
        RetitleMsg("UI_MSG_UPG_TITLE_CREST", "Prompts", "UI_MSG_HCOMBO_PREFIX");
    }

    // Some of these message prefabs have a title pointing at a line missing from Silksong's text; Silksong itself fills
    // that title in from the message that shows them.
    private static void RetitleMsg(string missingKey, string sheet, string key) {
        foreach (var title in UnityEngine.Object.FindObjectsByType<Silksong::SetTextMeshProGameText>(
                     FindObjectsSortMode.None))
            if (title.Text.Key == missingKey)
                title.Text = new SilksongLoc::TeamCherry.Localization.LocalisedString { Sheet = sheet, Key = key };
    }

    private static void ShowExtraSlotMsg(Silksong::ToolItemType slotType, int slotNumber) {
        var prefab = LoadPromptPrefab(ExtraSlotMsgPath);
        if (!prefab) return;
        ShowHeldMsg(afterMsg => Silksong::ExtraToolSlotUIMsg.Spawn(slotType, prefab, afterMsg));
        RetitleMsg("UI_MSG_TITLE_EXTRASLOT_PREFIX", "Tools", $"UI_MSG_TITLE_EXTRASLOT_PREFIX_{slotNumber}");
    }

    // Given an end callback, the skill message leaves bringing the HUD back to it.
    private static void ShowSkillMsg(Silksong::ToolItemSkill skill) {
        var prefab = LoadPromptPrefab(SkillMsgPath);
        var msg = prefab ? prefab.GetComponent<Silksong::SkillGetMsg>() : null;
        if (!msg) return;
        ShowHeldMsg(afterMsg => Silksong::SkillGetMsg.Spawn(msg, skill, () => {
            Silksong::GameCameras.instance.HUDIn();
            afterMsg();
        }));
    }

    private static void ShowPowerUpMsg(Silksong::PowerUpGetMsg.PowerUps powerUp) {
        var prefab = LoadPromptPrefab(PowerUpMsgPath);
        var msg = prefab ? prefab.GetComponent<Silksong::PowerUpGetMsg>() : null;
        if (!msg) return;
        ShowHeldMsg(afterMsg => Silksong::PowerUpGetMsg.Spawn(msg, powerUp, afterMsg));
    }

    // Silksong's full-screen messages hold the hero until a button press dismisses them.
    private static void ShowHeldMsg(Action<Action> spawn) {
        var hero = HeroSwitch.HornetActive ? HornetSpawner.Hornet : null;
        if (hero) hero.RelinquishControl();
        heldMsgCount++;
        HideHkItemMsgs();
        spawn(() => {
            if (--heldMsgCount == 0) EndHiddenMsgs();
            if (hero) hero.RegainControl();
        });
    }

    // Hornet's full-screen message stands in for HK's screen for the same pickup. HK's is hidden, then ended with
    // Hornet's, since the pickup waits for it.
    private static void HideHkItemMsgs() {
        foreach (var msg in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (Array.IndexOf(hkItemMsgs, msg.name) >= 0)
                HideMsg(msg);
    }

    // HK's message switches its parts' renderers back on as it fades them in, but leaves its own scale alone.
    private static void HideMsg(GameObject? msg) {
        if (!msg) return;
        msg.transform.localScale = Vector3.zero;
        hiddenMsgs.Add(msg);
    }

    // HK's message ends in the state that tells the pickup it's done and destroys the message.
    private static void EndHiddenMsgs() {
        foreach (var msg in hiddenMsgs.Where(msg => msg))
        foreach (var fsm in msg.GetComponents<PlayMakerFSM>()) {
            var end = fsm.FsmStates.FirstOrDefault(state =>
                state.Actions.OfType<SendEventByName>().Any(action => action.sendEvent.Value == GetItemMsgEnd));
            if (end != null) fsm.SetState(end.Name);
        }

        hiddenMsgs.Clear();
    }

    // The message prefabs have no Addressables key. Their bundle may already be mounted by Addressables, and a second
    // LoadFromFile of a mounted bundle throws.
    private static GameObject? LoadPromptPrefab(string path) {
        if (!promptsBundle)
            promptsBundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b && b.Contains(path)) ??
                            AssetBundle.LoadFromFile(Paths.SilksongAddressablesBundle(PromptsBundle));
        return promptsBundle ? promptsBundle.LoadAsset<GameObject>(path) : null;
    }

    private static bool IsSuperseded(SToolCrest crest) {
        var upgradedCrest = crest.GetFieldValue<SToolCrest>("upgradedVersion");
        return upgradedCrest && upgradedCrest.IsUnlocked;
    }
}
