extern alias Silksong;
extern alias SilksongLoc;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using HkLanguage = TeamCherry.Localization.Language;
using SCollectableItem = Silksong::CollectableItem;
using SLocalisedString = SilksongLoc::TeamCherry.Localization.LocalisedString;

namespace HornetInHallownest.Modules;

// Lists the Knight's key items, relics and Dream Nail essence among Hornet's items, so HK progress shows in her
// inventory. Each reads the PlayerData, text and sprite HK's own inventory pane shows for it.
public sealed class HkInventoryItemsModule : ModuleBase {
    private static readonly HkItemInfo[] itemInfos = [
        new("XunFlower", pd => Has(pd is { hasXunFlower: true, xunFlowerGiven: false }),
            pd => pd.xunFlowerBroken ? "FLOWER_BROKEN" : "FLOWER",
            pd => pd.xunFlowerBroken ? "White_Flower_Half" : "White_Flower_Full",
            DescriptionKey: pd =>
                (pd.xunFlowerBroken ? "FLOWER_BROKEN" : "FLOWER") + (pd.extraFlowerAppear ? "_QG" : "")),
        new("SimpleKey", pd => pd.simpleKeys, _ => "SIMPLEKEY", _ => "inv_item__00014_graveyard_key", true),
        new("StoreKey", pd => Has(pd.hasSlykey), _ => "STOREKEY", _ => "inv_item__0002_storeroom_key"),
        // HK hides the Elegant Key while the Shopkeeper's Key is held.
        new("WhiteKey", pd => Has(pd is { hasWhiteKey: true, usedWhiteKey: false, hasSlykey: false }), _ => "WHITEKEY",
            _ => "Elegant_Key"),
        new("CityKey", pd => Has(pd.hasCityKey), _ => "CITYKEY", _ => "inv_item_city_key"),
        new("LoveKey", pd => Has(pd.hasLoveKey), _ => "LOVEKEY", _ => "inv_Love_Key"),
        new("WaterwaysKey", pd => Has(pd.hasWaterwaysKey), _ => "WATERWAYSKEY", _ => "inv_Waterways_Key"),
        new("KingsBrand", pd => Has(pd.hasKingsBrand), _ => "KINGSBRAND", _ => "inv_white_brand"),
        new("TramPass", pd => Has(pd.hasTramPass), _ => "TRAM_PASS", _ => "inv_item__0001_tram_pass"),
        new("Lantern", pd => Has(pd.hasLantern), _ => "LANTERN", _ => "Lumafly_Lantern0000"),
        new("AcidArmour", pd => Has(pd.hasAcidArmour), _ => "ACIDARMOUR", _ => "items__0001_acid-armour"),
        new("SuperDash", pd => Has(pd.hasSuperDash), _ => "SUPERDASH", _ => "items__0000_crystal-heart"),
        new("ShadowDash", pd => Has(pd.hasShadowDash), _ => "SHADOWDASH", _ => "items__0004_shade_cloak"),
        // Desolate Dive is what breaks floors for her; Descending Dark earns her a tool instead.
        new("Quake", pd => Has(pd.quakeLevel > 0), _ => "SPELL_QUAKE1", _ => "Inv_0026_spell_quake_01"),
        new("MapQuill", pd => Has(pd.hasMap || pd.hasQuill),
            pd => pd.hasMap && pd.hasQuill ? "MAPQUILL" : pd.hasMap ? "MAP" : "QUILL",
            pd => pd.hasMap && pd.hasQuill ? "inv_item_map_quill_combined" :
                pd.hasMap ? "inv_item__0008_jar_col_map" : "inv_item__0004_quill"),
        new("Trinket1", pd => pd.trinket1, _ => "TRINKET1", _ => "inv_item__0013_wanderers-journal", true),
        new("Trinket2", pd => pd.trinket2, _ => "TRINKET2", _ => "inv_item__0012_hallownest-seal", true),
        new("Trinket3", pd => pd.trinket3, _ => "TRINKET3", _ => "inv_item__0010_Kings_idol", true),
        new("Trinket4", pd => pd.trinket4, _ => "TRINKET4", _ => "inv_item__0011_arcane-egg", true),
        new("RancidEgg", pd => pd.rancidEggs, _ => "RANCIDEGG", _ => "inv_rancid_egg", true),
        new("Ore", pd => pd.ore, _ => "ORE", _ => "inv_item__0009_ore", true),
        new("DreamNail", pd => pd.hasDreamNail ? pd.dreamOrbs : 0,
            pd => pd.dreamNailUpgraded ? "DREAMNAIL_B" : "DREAMNAIL_A", _ => "dream_nail_0003_1", true)
    ];

    private static readonly List<HkCollectableItem> items = [];

    public override string Id => "hk-inventory-items";

    public override void Initialize() {
        Detour(typeof(Silksong::InventoryItemCollectableManager), "GetItems", OnGetItems);
    }

    protected override void OnDeinitialize() {
        var masterList = MasterList();
        foreach (var item in items) {
            masterList?.Remove(item);
            UnityEngine.Object.Destroy(item);
        }

        items.Clear();
        HkCollectableItem.ClearSprites();
    }

    // The collected items are cached until CollectableItemManager's version changes, which HK progress never bumps.
    private static List<SCollectableItem> OnGetItems(Func<Silksong::InventoryItemCollectableManager,
        List<SCollectableItem>> orig, Silksong::InventoryItemCollectableManager self) {
        if (items.Count == 0 && MasterList() is { } masterList)
            foreach (var info in itemInfos) {
                var item = ScriptableObject.CreateInstance<HkCollectableItem>();
                item.name = "HK " + info.Id;
                item.Info = info;
                items.Add(item);
                masterList.Add(item);
            }

        Silksong::CollectableItemManager.IncrementVersion();
        ShowCompletion(self);
        return orig(self);
    }

    // HK shows its completion rate in the inventory once it's unlocked. Hornet's pane has Silksong's own, which its FSM
    // fills only at start-up, from Silksong's PlayerData.
    private static void ShowCompletion(Component pane) {
        var text = pane.transform.Find("Text Completion");
        var percentage = text ? text.Find("Percentage") : null;
        if (!percentage) return;
        var pd = PlayerData.instance;
        text!.gameObject.SetActive(pd.unlockedCompletionRate);
        if (pd.unlockedCompletionRate)
            percentage.GetComponent<Silksong::TMProOld.TextMeshPro>().text = $"{pd.completionPercentage}%";
    }

    private static Silksong::CollectableItemList? MasterList() =>
        Silksong::CollectableItemManager.SilentInstance?.GetFieldValue<Silksong::CollectableItemList>("masterList");

    private static int Has(bool held) => held ? 1 : 0;
}

internal sealed record HkItemInfo(
    string Id,
    Func<PlayerData, int> Amount,
    Func<PlayerData, string> TextKey,
    Func<PlayerData, string> SpriteName,
    bool ShowsAmount = false,
    Func<PlayerData, string>? DescriptionKey = null);

internal sealed class HkCollectableItem : SCollectableItem {
    private const string Sheet = "UI";

    private static Dictionary<string, Sprite>? spritesByName;

    internal HkItemInfo Info = null!;

    public override bool DisplayAmount => Info.ShowsAmount;

    public override int CollectedAmount => PlayerData.instance is { } pd ? Info.Amount(pd) : 0;

    protected override int IsSeenIndex => -1;

    public override string GetDisplayName(ReadSource readSource) =>
        Text("INV_NAME_");

    public override string GetDescription(ReadSource readSource) =>
        Text("INV_DESC_", Info.DescriptionKey ?? Info.TextKey);

    // HK's text breaks lines with <br>, which only HK's text renderer reads.
    private string Text(string prefix, Func<PlayerData, string>? key = null) =>
        SLocalisedString.ReplaceTags(HkLanguage.Get(prefix + (key ?? Info.TextKey)(PlayerData.instance), Sheet));

    // HK's inventory keeps its sprites loaded, including the ones its FSMs swap in, such as the Shade Cloak's.
    public override Sprite GetIcon(ReadSource readSource) {
        if (spritesByName == null) {
            spritesByName = [];
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                spritesByName.TryAdd(sprite.name, sprite);
        }

        return spritesByName.TryGetValue(Info.SpriteName(PlayerData.instance), out var found) ? found : null!;
    }

    protected override IEnumerable<UseResponse> GetUseResponses() => [];

    internal static void ClearSprites() => spritesByName = null;
}
