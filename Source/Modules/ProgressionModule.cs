extern alias Silksong;
extern alias SilksongLoc;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Modding;
using UnityEngine;
using UnityEngine.U2D;
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
    private const string CharmTutorialMsg = "Charm Tutorial Msg";
    private const string CharmBenchMsg = "Charm Equip Msg";
    private const string CharmBenchMsgDown = "CHARM MSG DOWN";
    private static readonly string[] silkshots = ["WebShot Weaver", "WebShot Forge", "WebShot Architect"];
    private const string GetItemMsgEnd = "GET ITEM MSG END";
    private const string MemoryLocket = "Crest Socket Unlocker";
    private const string PromptsBundle = "prompts_assets_all.bundle";
    private const string PowerUpMsgPath = "Assets/Prefabs/UI/Hornet UI/Ancestral_Art_Get_Prompt.prefab";
    private const string ExtraSlotMsgPath = "Assets/Prefabs/UI/Messages/Extra Tool Slot UI Msg.prefab";
    private const string SkillMsgPath = "Assets/Prefabs/UI/Hornet UI/Silk_Skill_Get_Prompt.prefab";
    private const string ItemMsgPath = "Assets/Prefabs/UI/Messages/UI Msg Get Item.prefab";
    private const string PromptIconsBundle = "atlases_assets_assets/sprites/_atlases/prompts.spriteatlas.bundle";
    private const string PromptIconsPath = "Assets/Sprites/_Atlases/Prompts.spriteatlas";

    private static AssetBundle? promptsBundle;
    private static AssetBundle? promptIconsBundle;

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

    // Keyed by HK's item screens (CreateUIMsgGetItem's "Item").
    private static readonly Dictionary<string, Silksong::PowerUpGetMsg.PowerUps> powerUpScreens = new() {
        ["Dash"] = Silksong::PowerUpGetMsg.PowerUps.Sprint,
        ["Walljump"] = Silksong::PowerUpGetMsg.PowerUps.WallJump,
        ["Super Dash"] = Silksong::PowerUpGetMsg.PowerUps.HarpoonDash,
        ["Kings Brand"] = Silksong::PowerUpGetMsg.PowerUps.SuperJump
    };

    private static readonly Dictionary<string, string> skillScreens = new() {
        ["Fireball"] = "Silk Spear",
        ["Fireball2"] = "Silk Charge",
        ["Quake"] = "Parry",
        ["Quake 2"] = "Silk Bomb",
        ["Scream"] = "Thread Sphere",
        ["Scream 2"] = "Silk Boss Needle"
    };

    private static readonly Dictionary<string, (string Item, string Icon)> itemScreens = new() {
        ["Double Jump"] = ("Double Jump", "Hornet_Double_Jump_Prompt"), // Faydown Cloak
        ["Dream Nail"] = ("Memory Melody", "deep_melody_prompts"), // Elegy of the Deep
        ["Dreamgate"] = ("Bellbeast Melody", "bellbeast_melody_prompts") // Beastling Call
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
        return value;
    }

    private int OnSetInt(string name, int value) {
        QueueApply(name);
        return value;
    }

    // HK's message for a picked-up charm shows where Hornet's message for its reward does, so it isn't created while
    // she's active. The pickup carries on without waiting for it. The pickup closes its first charm's tutorial on a
    // button press, which also dismisses Silksong's tutorial for the first tool shown in its place.
    private static void OnCreateObject(Action<CreateObject> orig, CreateObject self) {
        var prefab = self.gameObject.Value;
        if (HeroSwitch.HornetActive && prefab && prefab.name == CharmGetMsg) {
            self.Finish();
            return;
        }

        if (HeroSwitch.HornetActive && prefab && prefab.name == CharmBenchMsg) {
            self.Fsm.Event(CharmBenchMsgDown);
            self.Finish();
            return;
        }

        if (HeroSwitch.HornetActive && prefab && prefab.name == CharmTutorialMsg && CharmTool(self) is { } tool) {
            Silksong::ToolTutorialMsg.Spawn(tool);
            self.Finish();
            return;
        }

        orig(self);
    }

    private static SToolItem? CharmTool(CreateObject create) {
        var charm = FindLaterAction<SetFsmInt>(create, setId => setId.variableName.Value == "ID");
        var rule = charm == null ? null : ToolRules.FirstOrDefault(r => r.Flags.Contains(Charm(charm.setValue.Value)));
        return rule == null ? null : SToolItemManager.GetToolByName(rule.Reward);
    }

    private static void OnCreateUIMsgGetItem(Action<CreateUIMsgGetItem> orig, CreateUIMsgGetItem self) {
        var item = FindLaterAction<SetFsmString>(self, setItem => setItem.variableName.Value == "Item")?.setValue.Value;
        if (HeroSwitch.HornetActive && HasHornetScreen(item)) {
            var fsm = self.Fsm;
            ShowHornetScreen(item!, () => fsm.Event(GetItemMsgEnd));
            self.Finish();
            return;
        }

        orig(self);
    }

    // HK's messages are filled in by later actions of the state that creates them.
    private static T? FindLaterAction<T>(FsmStateAction action, Func<T, bool> match) where T : FsmStateAction {
        var actions = action.State.Actions;
        for (var i = Array.IndexOf(actions, action) + 1; i < actions.Length; i++)
            if (actions[i] is T later && match(later))
                return later;
        return null;
    }

    private static bool HasHornetScreen(string? item) =>
        item != null &&
        (powerUpScreens.ContainsKey(item) || skillScreens.ContainsKey(item) || itemScreens.ContainsKey(item));

    private static void ShowHornetScreen(string item, Action afterMsg) {
        if (powerUpScreens.TryGetValue(item, out var powerUp)) ShowPowerUpMsg(powerUp, afterMsg);
        else if (itemScreens.TryGetValue(item, out var itemScreen))
            ShowItemMsg(itemScreen.Item, itemScreen.Icon, afterMsg);
        else ShowSkillMsg(SToolItemManager.GetToolByName(skillScreens[item]) as Silksong::ToolItemSkill, afterMsg);
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
                // A silk skill is announced in place of HK's screen for its spell (skillScreens).
                tool.Unlock(null, showPopups && !skill ? SToolItem.PopupFlags.ItemGet : SToolItem.PopupFlags.None);
                MarkUnlocked(tool);
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

    // Given an end callback, the skill and power-up messages leave bringing the HUD back to it.
    private static void ShowSkillMsg(Silksong::ToolItemSkill? skill, Action? afterMsg = null) {
        var prefab = LoadPromptPrefab(SkillMsgPath);
        var msg = prefab ? prefab.GetComponent<Silksong::SkillGetMsg>() : null;
        if (!msg || !skill) {
            afterMsg?.Invoke();
            return;
        }

        // Silksong equips a new skill before announcing it, and the message reads the crest's slots that equipping
        // fills in: a new save's crest has none.
        SToolItemManager.AutoEquip(skill);
        ShowHeldMsg(end => Silksong::SkillGetMsg.Spawn(msg, skill, () => {
            Silksong::GameCameras.instance.HUDIn();
            end();
        }), afterMsg);
    }

    private static void ShowPowerUpMsg(Silksong::PowerUpGetMsg.PowerUps powerUp, Action? afterMsg = null) {
        var prefab = LoadPromptPrefab(PowerUpMsgPath);
        var msg = prefab ? prefab.GetComponent<Silksong::PowerUpGetMsg>() : null;
        if (!msg) {
            afterMsg?.Invoke();
            return;
        }

        ShowHeldMsg(end => Silksong::PowerUpGetMsg.Spawn(msg, powerUp, () => {
            Silksong::GameCameras.instance.HUDIn();
            end();
        }), afterMsg);
    }

    // The screen's own FSM picks its text by the item and reports its end to every register of the event; a register
    // stays subscribed until destroyed, so the screen goes with it.
    private static void ShowItemMsg(string item, string icon, Action afterMsg) {
        var prefab = LoadPromptPrefab(ItemMsgPath);
        if (!prefab) {
            afterMsg();
            return;
        }

        ShowHeldMsg(end => {
            Silksong::GameCameras.instance.HUDOut();
            var msg = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            if (msg.transform.Find("Icon") is { } iconSlot)
                iconSlot.GetComponent<SpriteRenderer>().sprite = LoadPromptIcon(icon);
            msg.SetActive(true);
            msg.GetComponents<SilksongPM::PlayMakerFSM>().First(fsm => fsm.FsmName == "Msg Control").FsmVariables
                .FindFsmString("Item").Value = item;
            Silksong::EventRegister.GetRegisterGuaranteed(msg, GetItemMsgEnd).ReceivedEvent += () => {
                UnityEngine.Object.Destroy(msg);
                Silksong::GameCameras.instance.HUDIn();
                end();
            };
        }, afterMsg);
    }

    // A cutscene that already holds her lets her go itself, and expects her animation to stay its own until then.
    private static void ShowHeldMsg(Action<Action> spawn, Action? afterMsg = null) {
        var hero = HeroSwitch.HornetActive ? HornetSpawner.Hornet : null;
        if (hero && hero.controlReqlinquished) hero = null;
        if (hero) hero.RelinquishControl();
        spawn(() => {
            if (hero) hero.RegainControl();
            afterMsg?.Invoke();
        });
    }

    private static GameObject? LoadPromptPrefab(string path) {
        if (!promptsBundle) promptsBundle = FindOrLoadBundle(PromptsBundle, path);
        return promptsBundle ? promptsBundle.LoadAsset<GameObject>(path) : null;
    }

    private static Sprite? LoadPromptIcon(string name) {
        if (!promptIconsBundle) promptIconsBundle = FindOrLoadBundle(PromptIconsBundle, PromptIconsPath);
        var atlas = promptIconsBundle ? promptIconsBundle.LoadAsset<SpriteAtlas>(PromptIconsPath) : null;
        return atlas ? atlas.GetSprite(name) : null;
    }

    // The message prefabs and their icons have no Addressables key. Their bundle may already be mounted by Addressables,
    // and a second LoadFromFile of a mounted bundle throws.
    private static AssetBundle? FindOrLoadBundle(string file, string path) =>
        AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b && b.Contains(path)) ??
        AssetBundle.LoadFromFile(Paths.SilksongAddressablesBundle(file));

    private static bool IsSuperseded(SToolCrest crest) {
        var upgradedCrest = crest.GetFieldValue<SToolCrest>("upgradedVersion");
        return upgradedCrest && upgradedCrest.IsUnlocked;
    }
}
