extern alias Silksong;
extern alias SilksongLoc;
using System;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using Modding;
using UnityEngine;
using SLocalisedString = SilksongLoc::TeamCherry.Localization.LocalisedString;
using SToolItem = Silksong::ToolItem;

namespace HornetInHallownest.Modules;

public sealed class ToolCharmBridgeModule : ModuleBase {
    private const string CompassCharm = "equippedCharm_2";
    private const string GrimmchildCharm = "equippedCharm_40";

    private static readonly Dictionary<string, string> tools = new() {
        ["equippedCharm_1"] = "Rosary Magnet", // Gathering Swarm
        [CompassCharm] = "Compass", // Wayward Compass
        [GrimmchildCharm] = GrimmchildTool.ToolName
    };

    // HK's enemy detectors, such as the Grimmchild's range, see layer 27, which HK leaves to its UI; Silksong puts the
    // physics pusher that follows Hornet there, which the Grimmchild would attack.
    private const int EnemyDetectorLayer = (int)GlobalEnums.PhysLayers.ENEMY_DETECTOR;
    private const int PhysicalPusherLayer = (int)Silksong::GlobalEnums.PhysLayers.PHYSICAL_PUSHER;

    private static GrimmchildTool? grimmchild;
    private static bool detectorsIgnoredPusher;

    public override string Id => "tool-charm-bridge";

    public override void Initialize() {
        ModHooks.GetPlayerBoolHook += OnGetBool;
        Detour(typeof(GameMap), nameof(GameMap.WorldMap), OnWorldMap);
        Detour(typeof(GameMap), nameof(GameMap.PositionCompass), OnPositionCompass);
        Detour(typeof(FlamePin), "OnEnable", OnFlamePinEnable);
        Detour(typeof(Silksong::ToolItemManager), "Awake", OnToolItemManagerAwake);
        Silksong::ToolItemManager.OnEquippedStateChanged += OnToolEquipsChanged;
        detectorsIgnoredPusher = Physics2D.GetIgnoreLayerCollision(EnemyDetectorLayer, PhysicalPusherLayer);
        Physics2D.IgnoreLayerCollision(EnemyDetectorLayer, PhysicalPusherLayer);
        AddGrimmchild(ToolList(Silksong::ToolItemManager.SilentInstance));
    }

    protected override void OnDeinitialize() {
        ModHooks.GetPlayerBoolHook -= OnGetBool;
        Silksong::ToolItemManager.OnEquippedStateChanged -= OnToolEquipsChanged;
        Physics2D.IgnoreLayerCollision(EnemyDetectorLayer, PhysicalPusherLayer, detectorsIgnoredPusher);
        if (!grimmchild) return;
        ToolList(Silksong::ToolItemManager.SilentInstance)?.Remove(grimmchild);
        UnityEngine.Object.Destroy(grimmchild);
        grimmchild = null;
    }

    private static bool OnGetBool(string name, bool orig) =>
        HeroSwitch.HornetActive && tools.TryGetValue(name, out var tool)
            ? Silksong::ToolItemManager.IsToolEquipped(tool)
            : orig;

    // HK's charm screen broadcasts this as charms change; the Grimmchild spawner and the Grimmchild re-check their charm
    // on it.
    private static void OnToolEquipsChanged() {
        if (HeroSwitch.HornetActive) PlayMakerFSM.BroadcastEvent("CHARM EQUIP CHECK");
    }

    // Switching heroes switches whose charm or tool counts.
    public override void HornetToggled(bool active) => PlayMakerFSM.BroadcastEvent("CHARM EQUIP CHECK");

    private static void OnToolItemManagerAwake(Action<Silksong::ToolItemManager> orig,
        Silksong::ToolItemManager self) {
        orig(self);
        AddGrimmchild(ToolList(self));
    }

    // Silksong has no Grimmchild, so Hornet gets one of her own among her tools.
    private static void AddGrimmchild(Silksong::ToolItemList? list) {
        if (list == null || list.GetByName(GrimmchildTool.ToolName)) return;
        if (!grimmchild) {
            grimmchild = ScriptableObject.CreateInstance<GrimmchildTool>();
            grimmchild.name = GrimmchildTool.ToolName;
            grimmchild.SetFieldValue("type", Silksong::ToolItemType.Blue);
            // A deserialized tool always has one, and ToolItem reads it unguarded; CreateInstance leaves it null.
            grimmchild.SetFieldValue("alternateUnlockedTest", new Silksong::PlayerDataTest());
        }

        list.Add(grimmchild);
    }

    private static Silksong::ToolItemList? ToolList(Silksong::ToolItemManager? manager) =>
        manager ? manager.GetFieldValue<Silksong::ToolItemList>("toolItems") : null;

    private static void OnWorldMap(Action<GameMap> orig, GameMap self) =>
        WithHornetCharm(CompassCharm, () => orig(self));

    private static void OnPositionCompass(Action<GameMap, bool> orig, GameMap self, bool posShade) =>
        WithHornetCharm(CompassCharm, () => orig(self, posShade));

    private static void OnFlamePinEnable(Action<FlamePin> orig, FlamePin self) =>
        WithHornetCharm(GrimmchildCharm, () => orig(self));

    // GameMap and FlamePin read the charm's field directly rather than through GetBool.
    private static void WithHornetCharm(string charm, Action call) {
        var pd = PlayerData.instance;
        if (!HeroSwitch.HornetActive || pd == null) {
            call();
            return;
        }

        var knightCharm = pd.GetFieldValue<bool>(charm);
        pd.SetFieldValue(charm, Silksong::ToolItemManager.IsToolEquipped(tools[charm]));
        try {
            call();
        } finally {
            pd.SetFieldValue(charm, knightCharm);
        }
    }
}

// HK's Grimmchild charm as a blue tool: its name, text and icon follow the charm through its forms.
internal sealed class GrimmchildTool : SToolItem {
    internal const string ToolName = "HK Grimmchild";
    private const string Sheet = LanguageSyncModule.HkSheetPrefix + "UI";

    // 5 is Carefree Melody, after the troupe is banished.
    private static int Level => PlayerData.instance.grimmChildLevel;

    public override UsageOptions Usage => default;

    public override SLocalisedString DisplayName => Text(Level == 5 ? "CHARM_NAME_40_N" : "CHARM_NAME_40");

    public override SLocalisedString Description =>
        Text(Level switch { 5 => "CHARM_DESC_40_N", 4 => "CHARM_DESC_40_F", _ => "CHARM_DESC_40" });

    public override Sprite GetInventorySprite(IconVariants iconVariant) => CharmIcon();

    public override Sprite GetHudSprite(IconVariants iconVariant) => CharmIcon();

    private static SLocalisedString Text(string key) => new() { Sheet = Sheet, Key = key };

    // HK's charm pane sets the icon list's instance as it wakes, and it may not have yet.
    private static Sprite CharmIcon() {
        var icons = CharmIconList.Instance
            ? CharmIconList.Instance
            : Resources.FindObjectsOfTypeAll<CharmIconList>().FirstOrDefault();
        return icons ? icons.GetSprite(40) : null!;
    }
}
