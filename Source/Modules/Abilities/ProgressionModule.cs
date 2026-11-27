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
using static HornetInHallownest.Modules.ProgressionRules;
using SPlayerData = Silksong::PlayerData;
using SToolCrest = Silksong::ToolCrest;
using SToolItem = Silksong::ToolItem;
using SToolItemManager = Silksong::ToolItemManager;

namespace HornetInHallownest.Modules;

// Hornet's Silksong tools, crests and Eva upgrades are earned through HK's own progression.
public sealed class ProgressionModule : ModuleBase {
    private const int BaseCharmNotches = 3;
    private const int ExtraBlueSlotNotches = 8;
    private const int ExtraYellowSlotNotches = 11;
    private const string CharmGetMsg = "Charm Get Msg";
    private const string CharmTutorialMsg = "Charm Tutorial Msg";
    private const string CharmBenchMsg = "Charm Equip Msg";
    private const string CharmBenchMsgDown = "CHARM MSG DOWN";
    private static readonly string[] silkshots = ["WebShot Weaver", "WebShot Forge", "WebShot Architect"];
    private static readonly string[] hkItemMsgFsms = ["Relic Msg", "Journal Msg"];

    private static readonly string[] colosseumTrials =
        ["Room_Colosseum_Bronze", "Room_Colosseum_Silver", "Room_Colosseum_Gold"];

    private static readonly HashSet<Fsm> shownHkItemMsgs = [];
    private static readonly Queue<(Action<Action> Spawn, Action? AfterMsg)> heldMsgs = new();
    private static bool isHeldMsgShown;
    private const string GetItemMsgEnd = "GET ITEM MSG END";
    private const string MemoryLocket = "Crest Socket Unlocker";
    private const string PromptsBundle = "prompts_assets_all.bundle";
    private const string PowerUpMsgPath = "Assets/Prefabs/UI/Hornet UI/Ancestral_Art_Get_Prompt.prefab";
    private const string ExtraSlotMsgPath = "Assets/Prefabs/UI/Messages/Extra Tool Slot UI Msg.prefab";
    private const string SkillMsgPath = "Assets/Prefabs/UI/Hornet UI/Silk_Skill_Get_Prompt.prefab";
    private const string ItemMsgPath = "Assets/Prefabs/UI/Messages/UI Msg Get Item.prefab";
    private const string SilkHeartItem = "SilkHeart";
    private const string SilkHeartIcon = "prompt_silkheart";
    private const string SilkHeartState = "Set Silk Heart";
    private const string NeedleStrikeItem = "ChargeSlash";
    private const string NeedleStrikeIcon = "prompt_nail_art";
    private const string NailArtItemPrefix = "NA ";
    private const string PromptIconsBundle = "atlases_assets_assets/sprites/_atlases/prompts.spriteatlas.bundle";
    private const string PromptIconsPath = "Assets/Sprites/_Atlases/Prompts.spriteatlas";

    private static AssetBundle? promptsBundle;
    private static AssetBundle? promptIconsBundle;

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
        ["Dreamgate"] = ("Bellbeast Melody", "bellbeast_melody_prompts"), // Beastling Call
        ["Journal"] = ("Journal", "Journal_Prompt")
    };

    private static readonly HashSet<string> watchedFields = [
        .. ToolRules.Concat(CrestRules).Concat(LocketRules).Append(SylphsongRule).SelectMany(r => r.Flags)
            .Select(f => f.Field),
        nameof(PlayerData.charmSlots), nameof(PlayerData.hasCyclone), nameof(PlayerData.hasDashSlash),
        nameof(PlayerData.hasUpwardSlash), nameof(PlayerData.hasShadowDash)
    ];

    // PlayerDataSyncModule grants Needle Strike and the Silk Hearts as HK sets them; these are the ones already announced.
    private static bool isNeedleStrikeAnnounced;
    private static int announcedSilkHearts;

    private bool applyQueued;

    public override string Id => "progression";

    public override void Initialize() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
        ModHooks.SetPlayerIntHook += OnSetInt;
        Detour(typeof(CreateObject), nameof(CreateObject.OnEnter), OnCreateObject);
        Detour(typeof(CreateUIMsgGetItem), nameof(CreateUIMsgGetItem.OnEnter), OnCreateUIMsgGetItem);
        Detour(typeof(Fsm), nameof(Fsm.SwitchState), OnSwitchState, typeof(FsmState));
        Detour(typeof(PlayerData), nameof(PlayerData.CountJournalEntries), OnCountJournalEntries);
    }

    // HK counts the journal by writing its fields directly, past the SetInt hook.
    private void OnCountJournalEntries(Action<PlayerData> orig, PlayerData self) {
        orig(self);
        QueueApply(nameof(PlayerData.journalEntriesCompleted));
    }

    protected override void OnDeinitialize() {
        ModHooks.SetPlayerBoolHook -= OnSetBool;
        ModHooks.SetPlayerIntHook -= OnSetInt;
        shownHkItemMsgs.Clear();
        heldMsgs.Clear();
        isHeldMsgShown = false;
        Silksong::UIMsgPopupBaseBase.MinYPos = float.MinValue;
    }

    // HK's small item and journal messages sit where Silksong's item messages start their stack.
    private static void OnSwitchState(Action<Fsm, FsmState> orig, Fsm self, FsmState to) {
        orig(self, to);
        if (Array.IndexOf(hkItemMsgFsms, self.Name) < 0) return;
        if (to.Name == "Up") shownHkItemMsgs.Add(self);
        else shownHkItemMsgs.Remove(self);
        shownHkItemMsgs.RemoveWhere(msg => !msg.GameObject);
        Silksong::UIMsgPopupBaseBase.MinYPos = shownHkItemMsgs.Count == 0
            ? float.MinValue
            : Silksong::GlobalSettings.UI.UIMsgPopupStartPosition.y + Silksong::GlobalSettings.UI.UIMsgPopupStackOffset.y;
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
        if (HeroSwitch.HornetActive && (HasHornetScreen(item) || IsNailArt(item))) {
            var fsm = self.Fsm;
            ShowHornetScreen(item!,
                () => {
                    RunSkippedMsgEffects(item!);
                    fsm.Event(GetItemMsgEnd);
                    if (item == "Quake") ControlReminderModule.ShowFloorBreakReminder();
                });
            self.Finish();
            return;
        }

        orig(self);
    }

    // What the skipped HK message would do
    private static void RunSkippedMsgEffects(string item) {
        var pd = PlayerData.instance;
        if (IsNailArt(item)
            && pd.GetBool(nameof(PlayerData.hasCyclone))
            && pd.GetBool(nameof(PlayerData.hasUpwardSlash))
            && pd.GetBool(nameof(PlayerData.hasDashSlash)))
            pd.SetBool(nameof(PlayerData.hasAllNailArts), true);

        GameManager.instance.SaveGame();
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
        item != null && (powerUpScreens.ContainsKey(item) || skillScreens.ContainsKey(item) || itemScreens.ContainsKey(item));

    private static bool IsNailArt(string? item) =>
        item?.StartsWith(NailArtItemPrefix, StringComparison.Ordinal) == true;

    private static void ShowHornetScreen(string item, Action afterMsg) {
        if (IsNailArt(item)) ShowNailArtMsg(afterMsg);
        else if (powerUpScreens.TryGetValue(item, out var powerUp)) ShowPowerUpMsg(powerUp, afterMsg);
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

    // HK sets some of these fields partway through a conversation or a cutscene, and a reward's screen would cover it.
    // Both hold Hornet until they end, even between the boxes of a conversation. A Colosseum trial fills the Hunter's
    // Journal as it goes, and the Hunter Crest's evolution screen would stop the fight.
    private IEnumerator ApplyNextFrame() {
        yield return null;
        while (HeroSwitch.HornetActive && ShouldHoldRewards()) yield return null;
        applyQueued = false;
        Apply(showPopups: HeroSwitch.HornetActive);
    }

    private static bool ShouldHoldRewards() {
        var hero = HornetSpawner.Hornet;
        return (hero && hero.controlReqlinquished)
            || UnityApi.FindAll<DialogueBox>()
                .Any(box => !box.hidden)
            || colosseumTrials.Contains(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    internal static void Apply(bool showPopups = false) {
        var pd = PlayerData.instance;
        var ss = SPlayerData.instance;
        if (pd == null || ss == null) return;
        
        var settings = HornetInHallownest.LoadedInstance?.GlobalSettings;
        ss.SeenToolGetPrompt = true;
        ss.SeenToolWeaponGetPrompt = true;

        var allTools = pd.bossRushMode || settings?.UnlockAllTools == true;
        var earnedTools = new HashSet<string>(ToolRules.Where(r => allTools || r.IsEarned(pd)).Select(r => r.Reward));
        
        if (!allTools) KeepOneSilkshot(earnedTools);
        
        var upgrades = SToolItemManager.GetAllTools().Where(t => t && t.getReplaces).ToList();
        var replaceableTools = new HashSet<SToolItem?>(upgrades.Select(t => t.getReplaces));
        var replacedTools = new HashSet<SToolItem?>(upgrades.Where(t => earnedTools.Contains(t.name))
            .Select(t => t.getReplaces));

        foreach (var tool in SToolItemManager.GetAllTools()) {
            if (!tool) continue;
            var earned = earnedTools.Contains(tool.name);
            var replaced = replacedTools.Contains(tool);
            // A silk skill also counts as unlocked by its PlayerData flag, which PlayerDataSyncModule sets from the spell
            // before this runs.
            var skill = tool as Silksong::ToolItemSkill;
            if (earned && (!tool.IsUnlocked || (skill && !tool.SavedData.IsUnlocked))) {
                // A silk skill is announced in place of HK's screen for its spell (skillScreens).
                tool.Unlock(null,
                    showPopups && !skill && !replaced ? SToolItem.PopupFlags.ItemGet : SToolItem.PopupFlags.None);
                MarkUnlocked(tool);
            }
            else if (!earned && tool.IsUnlocked) {
                LockTool(tool);
            }

            // Silksong hides a tool only when its upgrade is unlocked after it, and Hornet can earn the upgrade first.
            if (earned && replaceableTools.Contains(tool) && tool.SavedData.IsHidden != replaced)
                SetHidden(tool, replaced);
        }

        var allCrests = settings?.UnlockAllCrests == true;
        var earnedCrests = new HashSet<string>(CrestRules.Where(r => allCrests || r.IsEarned(pd)).Select(r => r.Reward));

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

        if (SToolItemManager.GetCrestByName(ss.CurrentCrestID) is not { IsVisible: true }) {
            var hunter = SToolItemManager.GetCrestByName(StartingCrest);
            while (hunter.IsUpgradedVersionUnlocked) hunter = hunter.upgradedVersion;
            SToolItemManager.SetEquippedCrest(hunter.name);
        }

        var notches = pd.GetInt(nameof(PlayerData.charmSlots));
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
        if (showPopups) {
            if (ss.hasChargeSlash && !isNeedleStrikeAnnounced)
                ShowItemMsg(NeedleStrikeItem, NeedleStrikeIcon, () => { });
            for (var i = announcedSilkHearts; i < ss.silkRegenMax; i++)
                ShowItemMsg(SilkHeartItem, SilkHeartIcon, () => { });
        }

        isNeedleStrikeAnnounced = ss.hasChargeSlash;
        announcedSilkHearts = ss.silkRegenMax;
        // Last, so that its pop-up queues behind the full-screen messages above.
        GrantMemoryLockets(pd, ss, showPopups);

        BenchModule.RefreshToolHud();
    }

    // Silksong gives only one of its three Silkshots, so Hornet keeps the one from the first dreamer she defeats.
    private static void KeepOneSilkshot(HashSet<string> earnedTools) {
        var earnedSilkshots = silkshots.Where(earnedTools.Contains).ToList();
        if (earnedSilkshots.Count < 2) return;
        var keptSilkshot =
            earnedSilkshots.FirstOrDefault(name => SToolItemManager.GetToolByName(name) is { IsUnlocked: true }) ?? earnedSilkshots[0];
        earnedTools.ExceptWith(earnedSilkshots.Where(name => name != keptSilkshot));
    }

    // A tool also counts as unlocked while its alternate unlock test passes, so that test is failed too.
    private static void LockTool(SToolItem tool) {
        var test = tool.alternateUnlockedTest;
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

    private static void SetHidden(SToolItem tool, bool hidden) {
        if (hidden) {
            tool.Lock();
            return;
        }

        var data = tool.SavedData;
        data.IsHidden = false;
        tool.SavedData = data;
    }

    private static void LockCrest(SToolCrest crest) {
        var data = crest.SaveData;
        data.IsUnlocked = false;
        crest.SaveData = data;
    }

    internal static int EarnedMemoryLockets(PlayerData pd) =>
        Math.Max(0, pd.GetInt(nameof(PlayerData.charmSlots)) - BaseCharmNotches) + LocketRules.Count(r => r.IsEarned(pd));

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
            var newLockets = heldLockets - data.Amount;
            ShowAfterHeldMsgs(() =>
                Silksong::CollectableItemManager.GetItemByName(MemoryLocket).Collect(newLockets));
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

        // Silksong forges the Witch Crest from the Cursed one under a message of its own, so the line its title prefix
        // points at was never written. The other crests' prefix fits it.
        var prefix = crest.ItemNamePrefix;
        if (!SilksongLoc::TeamCherry.Localization.Language.Has(prefix.Key, prefix.Sheet))
            crest.itemNamePrefix = new SilksongLoc::TeamCherry.Localization.LocalisedString {
                Sheet = "Tools",
                Key = "UI_MSG_TITLE_CREST"
            };

        ShowHeldMsg(afterMsg => {
            Silksong::ToolCrestUIMsg.Spawn(crest, prefab, afterMsg);
            // Silksong evolves the Hunter Crest with another message, titled with this line.
            RetitleMsg("UI_MSG_UPG_TITLE_CREST", "Prompts", "UI_MSG_HCOMBO_PREFIX");
        });
    }

    // Some of these message prefabs have a title pointing at a line missing from Silksong's text; Silksong itself fills
    // that title in from the message that shows them.
    private static void RetitleMsg(string missingKey, string sheet, string key) {
        foreach (var title in UnityApi.FindAll<Silksong::SetTextMeshProGameText>())
            if (title.Text.Key == missingKey)
                title.Text = new SilksongLoc::TeamCherry.Localization.LocalisedString {
                    Sheet = sheet,
                    Key = key
                };
    }

    private static void ShowExtraSlotMsg(Silksong::ToolItemType slotType, int slotNumber) {
        var prefab = LoadPromptPrefab(ExtraSlotMsgPath);
        if (!prefab) return;
        ShowHeldMsg(afterMsg => {
            Silksong::ExtraToolSlotUIMsg.Spawn(slotType, prefab, afterMsg);
            RetitleMsg("UI_MSG_TITLE_EXTRASLOT_PREFIX", "Tools", $"UI_MSG_TITLE_EXTRASLOT_PREFIX_{slotNumber}");
        });
    }

    // Hornet has no nail arts: the first one HK teaches her is her Needle Strike, the next two each a Silk Heart, which
    // PlayerDataSyncModule granted when HK set the art.
    private static void ShowNailArtMsg(Action afterMsg) {
        var ss = SPlayerData.instance;
        if (ss.hasChargeSlash && !isNeedleStrikeAnnounced) {
            isNeedleStrikeAnnounced = true;
            ShowItemMsg(NeedleStrikeItem, NeedleStrikeIcon, afterMsg);
        }
        else if (ss.silkRegenMax > announcedSilkHearts) {
            announcedSilkHearts++;
            ShowItemMsg(SilkHeartItem, SilkHeartIcon, afterMsg);
        }
        else {
            afterMsg();
        }
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
        ShowHeldMsg(end => Silksong::SkillGetMsg.Spawn(msg,
                skill,
                () => {
                    Silksong::GameCameras.instance.HUDIn();
                    end();
                }),
            afterMsg);
    }

    private static void ShowPowerUpMsg(Silksong::PowerUpGetMsg.PowerUps powerUp, Action? afterMsg = null) {
        var prefab = LoadPromptPrefab(PowerUpMsgPath);
        var msg = prefab ? prefab.GetComponent<Silksong::PowerUpGetMsg>() : null;
        if (!msg) {
            afterMsg?.Invoke();
            return;
        }

        ShowHeldMsg(end => Silksong::PowerUpGetMsg.Spawn(msg,
                powerUp,
                () => {
                    Silksong::GameCameras.instance.HUDIn();
                    end();
                }),
            afterMsg);
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
                var control = msg.GetComponents<SilksongPM::PlayMakerFSM>().First(fsm => fsm.FsmName == "Msg Control");
                control.FsmVariables.FindFsmString("Item").Value = item;
                // Silksong shows its Silk Heart message before granting the heart, and skips it once Hornet has one;
                // PlayerDataSyncModule has granted it by now.
                if (item == SilkHeartItem)
                    foreach (var test in control.Fsm.GetState(SilkHeartState).Actions
                                 .OfType<Silksong::HutongGames.PlayMaker.Actions.PlayerDataVariableTest>())
                        test.Enabled = false;
                Silksong::EventRegister.GetRegisterGuaranteed(msg, GetItemMsgEnd).ReceivedEvent += () => {
                    UnityEngine.Object.Destroy(msg);
                    Silksong::GameCameras.instance.HUDIn();
                    end();
                };
            },
            afterMsg);
    }

    // Rewards earned together would open their screens in the same frame, one over the other.
    private static void ShowHeldMsg(Action<Action> spawn, Action? afterMsg = null) {
        heldMsgs.Enqueue((spawn, afterMsg));
        if (!isHeldMsgShown) ShowNextHeldMsg();
    }

    // A pop-up opened under a full-screen message is gone by the time the message closes.
    private static void ShowAfterHeldMsgs(Action show) {
        if (!isHeldMsgShown) {
            show();
            return;
        }

        heldMsgs.Enqueue((end => {
            show();
            end();
        }, null));
    }

    // A cutscene that already holds her lets her go itself, and expects her animation to stay its own until then.
    private static void ShowNextHeldMsg() {
        isHeldMsgShown = heldMsgs.Count > 0;
        if (!isHeldMsgShown) return;
        var (spawn, afterMsg) = heldMsgs.Dequeue();
        var hero = HeroSwitch.HornetActive ? HornetSpawner.Hornet : null;
        if (hero && hero.controlReqlinquished) hero = null;
        if (hero) hero.RelinquishControl();
        spawn(() => {
            if (hero) hero.RegainControl();
            afterMsg?.Invoke();
            ShowNextHeldMsg();
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
        AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b && b.Contains(path)) ?? AssetBundle.LoadFromFile(Paths.SilksongAddressablesBundle(file));

    private static bool IsSuperseded(SToolCrest crest) {
        var upgradedCrest = crest.upgradedVersion;
        return upgradedCrest && upgradedCrest.IsUnlocked;
    }
}
