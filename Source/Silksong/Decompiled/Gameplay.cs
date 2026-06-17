using System.Collections.Generic;
using TeamCherry.SharedUtils;
using UnityEngine;

namespace Silksong;

[CreateAssetMenu(menuName = "Hornet/Global Settings/Global Gameplay Settings")]
public class Gameplay : GlobalSettingsBase<Gameplay>
{
	[Header("System")]
	[Space]
	[SerializeField]
	private int toolReplenishCost;

	[SerializeField]
	private float toolPouchUpgradeIncrease;

	[SerializeField]
	private float toolKitDamageIncrease;

	[Space]
	[SerializeField]
	private float toolCameraDistanceBreak;

	[Space]
	[SerializeField]
	[ArrayForEnum(typeof(CurrencyType))]
	private int[] currencyCaps;

	[SerializeField]
	private int consumableItemCap;

	[Space]
	[SerializeField]
	private CostReference smallGeoValue;

	[SerializeField]
	private CostReference mediumGeoValue;

	[SerializeField]
	private CostReference largeGeoValue;

	[Space]
	[SerializeField]
	private GameObject smallGeoPrefab;

	[SerializeField]
	private GameObject mediumGeoPrefab;

	[SerializeField]
	private GameObject largeGeoPrefab;

	[SerializeField]
	private GameObject largeSmoothGeoPrefab;

	[Space]
	[SerializeField]
	private List<GameObject> shellShardPrefabs = new List<GameObject>();

	[Space]
	[SerializeField]
	private GameObject shellShardMidPrefab;

	[Space]
	[SerializeField]
	[ArrayForEnum(typeof(CurrencyType))]
	private int[] maxCurrencyObjects;

	[Space]
	[SerializeField]
	private CollectableItemPickup collectableItemPickupPrefab;

	[SerializeField]
	private CollectableItemPickup collectableItemPickupInstantPrefab;

	[SerializeField]
	private GenericPickup genericPickupPrefab;

	[SerializeField]
	private CollectableItemPickup collectableItemPickupMeatPrefab;

	[Space]
	[SerializeField]
	private GameObject reaperBundlePrefab;

	[Space]
	[SerializeField]
	private FsmTemplate hornetMultiWounderFsmTemplate;

	[Space]
	[SerializeField]
	private CollectableItemMementoList mementoList;

	[Header("Crest Config (for reference in code)")]
	[Space]
	[SerializeField]
	private ToolCrest hunterCrest;

	[SerializeField]
	private ToolCrest hunterCrest2;

	[SerializeField]
	private int hunterComboHits;

	[SerializeField]
	private float hunterComboDamageMult;

	[SerializeField]
	private ToolCrest hunterCrest3;

	[SerializeField]
	private int hunterCombo2Hits;

	[SerializeField]
	private float hunterCombo2DamageMult;

	[SerializeField]
	private GameObject hunterComboDamageEffect;

	[SerializeField]
	private int hunterCombo2ExtraHits;

	[SerializeField]
	private float hunterCombo2ExtraDamageMult;

	[SerializeField]
	private Vector2 hunterCombo2DamageExtraScale;

	[Space]
	[SerializeField]
	private ToolCrest warriorCrest;

	[SerializeField]
	private float warriorRageDuration;

	[SerializeField]
	private float warriorRageGraceTime;

	[SerializeField]
	private float[] warriorRageHitAddTimePerHit;

	[SerializeField]
	private float warriorDamageMultiplier;

	[SerializeField]
	private float warriorRageDamagedRemoveTime;

	[Space]
	[SerializeField]
	private ToolCrest reaperCrest;

	[SerializeField]
	private float reaperModeDuration;

	[Space]
	[SerializeField]
	private ToolCrest wandererCrest;

	[SerializeField]
	[Range(0f, 1f)]
	private float wandererCritChance = 1f;

	[SerializeField]
	private float wandererCritMultiplier = 2f;

	[SerializeField]
	private float wandererCritMagnitudeMult = 2f;

	[SerializeField]
	private GameObject wandererCritEffect;

	[Space]
	[SerializeField]
	private ToolCrest cursedCrest;

	[SerializeField]
	private ToolCrest witchCrest;

	[SerializeField]
	private int witchCrestRecoilSteps = 2;

	[Space]
	[SerializeField]
	private ToolCrest toolmasterCrest;

	[SerializeField]
	private int toolmasterQuickCraftNoneUsage;

	[Space]
	[SerializeField]
	private ToolCrest spellCrest;

	[SerializeField]
	private float spellCrestRuneDamageMult;

	[Space]
	[SerializeField]
	private ToolCrest cloaklessCrest;

	[Header("Tool Config (for reference in code)")]
	[Space]
	[SerializeField]
	private ToolItem defaultSkillTool;

	[Space]
	[SerializeField]
	private ToolItem luckyDiceTool;

	[Space]
	[SerializeField]
	private ToolItem barbedWireTool;

	[SerializeField]
	private float barbedWireDamageDealtMultiplier = 1.5f;

	[SerializeField]
	private float barbedWireDamageTakenMultiplier = 2f;

	[Space]
	[SerializeField]
	private ToolItem weightedAnkletTool;

	[SerializeField]
	private float weightedAnkletDmgKnockbackMult = 0.5f;

	[SerializeField]
	private float weightedAnkletDmgInvulnMult = 1.2f;

	[SerializeField]
	private int weightedAnkletRecoilSteps = 2;

	[Space]
	[SerializeField]
	private ToolItem boneNecklaceTool;

	[SerializeField]
	private float boneNecklaceShellshardIncrease = 0.2f;

	[Space]
	[SerializeField]
	private ToolItem longNeedleTool;

	[SerializeField]
	private Vector2 longNeedleMultiplier = new Vector2(1.25f, 1.25f);

	[Space]
	[SerializeField]
	private ToolItem deadPurseTool;

	[SerializeField]
	[Range(0f, 1f)]
	private float deadPurseHoldPercent;

	[Space]
	[SerializeField]
	private ToolItem shellSatchelTool;

	[SerializeField]
	private float shellSatchelToolIncrease;

	[Space]
	[SerializeField]
	private ToolItem spoolExtenderTool;

	[SerializeField]
	private int spoolExtenderSilk;

	[Space]
	[SerializeField]
	private ToolItem whiteRingTool;

	[SerializeField]
	private float whiteRingSilkRegenTimeMultiplier;

	[SerializeField]
	private int whiteRingSilkRegenIncrease;

	[Space]
	[SerializeField]
	private ToolItem lavaBellTool;

	[SerializeField]
	private float lavaBellCooldownTime;

	[Space]
	[SerializeField]
	private ToolItem thiefCharmTool;

	[SerializeField]
	private float thiefCharmGeoSmallIncrease;

	[SerializeField]
	private float thiefCharmGeoMedIncrease;

	[SerializeField]
	private float thiefCharmGeoLargeIncrease;

	[SerializeField]
	private MinMaxFloat thiefCharmGeoLoss;

	[SerializeField]
	private MinMaxInt thiefCharmGeoLossCap;

	[SerializeField]
	private float thiefCharmGeoLossLooseChance;

	[SerializeField]
	private MinMaxInt thiefCharmGeoLossLooseAmount;

	[SerializeField]
	private GameObject thiefCharmHeroHitPrefab;

	[SerializeField]
	private AudioEvent thiefCharmEnemyDeathAudio;

	[Space]
	[SerializeField]
	private ToolItem thiefPickTool;

	[SerializeField]
	private MinMaxFloat thiefPickGeoSteal;

	[SerializeField]
	private MinMaxInt thiefPickGeoStealMin;

	[SerializeField]
	private MinMaxInt thiefPickShardSteal;

	[SerializeField]
	private ThiefSnatchEffect thiefSnatchEffectPrefab;

	[SerializeField]
	private float thiefPickLooseChance;

	[SerializeField]
	private MinMaxInt thiefPickGeoLoose;

	[SerializeField]
	private MinMaxInt thiefPickShardLoose;

	[Space]
	[SerializeField]
	private ToolItem maggotCharm;

	[SerializeField]
	private float maggotCharmHealthLossTime;

	[SerializeField]
	private float maggotCharmEnterWaterAddTime;

	[SerializeField]
	private GameObject maggotCharmHitSinglePrefab;

	[Space]
	[SerializeField]
	private ToolItem poisonPouchTool;

	[SerializeField]
	private DamageTag poisonPouchDamageTag;

	[SerializeField]
	private Color poisonPouchTintColour;

	[SerializeField]
	private Color poisonPouchHeroTintColour;

	[Space]
	[SerializeField]
	private ToolItem zapImbuementTool;

	[SerializeField]
	private DamageTag zapDamageTag;

	[SerializeField]
	private Color zapDamageTintColour;

	[SerializeField]
	private float zapDamageMult;

	[Space]
	[SerializeField]
	private ToolItem musicianCharmTool;

	[SerializeField]
	private float musicianCharmSilkDrainTimeMult;

	[SerializeField]
	private float musicianCharmNeedolinRangeMult;

	[SerializeField]
	private float musicianCharmFovOffset;

	[SerializeField]
	private float musicianCharmFovStartDuration;

	[SerializeField]
	private AnimationCurve musicianCharmFovStartCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

	[SerializeField]
	private float musicianCharmFovEndDuration;

	[SerializeField]
	private AnimationCurve musicianCharmFovEndCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

	[Space]
	[SerializeField]
	private ToolItem scuttleCharmTool;

	[SerializeField]
	private GameObject scuttleEvadeEffect;

	[Space]
	[SerializeField]
	private ToolItem brollySpikeTool;

	[SerializeField]
	private GameObject brollySpikeObject_dj;

	[Space]
	[SerializeField]
	private ToolItem compassTool;

	[SerializeField]
	private ToolItem mossCreep1Tool;

	[SerializeField]
	private ToolItem mossCreep2Tool;

	[SerializeField]
	private ToolItem fracturedMaskTool;

	[SerializeField]
	private ToolItem curveclawTool;

	[SerializeField]
	private ToolItem curveclawUpgradedTool;

	[SerializeField]
	private ToolItem sprintmasterTool;

	[SerializeField]
	private ToolItem multibindTool;

	[SerializeField]
	private ToolItem quickSlingTool;

	[SerializeField]
	private ToolItem wallClingTool;

	[SerializeField]
	private ToolItem wispLanternTool;

	[SerializeField]
	private ToolItem fleaCharmTool;

	[Header("World")]
	[SerializeField]
	private FullQuestBase huntressQuest;

	[SerializeField]
	private ShopItemList mapperStock;

	[SerializeField]
	private QuestTargetPlayerDataBools fleasCollectedCounter;

	[SerializeField]
	private QuestBoardList enclaveQuestBoard;

	public static int ToolReplenishCost => Get().toolReplenishCost;

	public static float ToolPouchUpgradeIncrease => Get().toolPouchUpgradeIncrease;

	public static float ToolKitDamageIncrease => Get().toolKitDamageIncrease;

	public static float ToolCameraDistanceBreak => Get().toolCameraDistanceBreak;

	public static int ConsumableItemCap => Get().consumableItemCap;

	public static CostReference SmallGeoValue => Get().smallGeoValue;

	public static CostReference MediumGeoValue => Get().mediumGeoValue;

	public static CostReference LargeGeoValue => Get().largeGeoValue;

	public static GameObject SmallGeoPrefab => Get().smallGeoPrefab;

	public static GameObject MediumGeoPrefab => Get().mediumGeoPrefab;

	public static GameObject LargeGeoPrefab => Get().largeGeoPrefab;

	public static GameObject LargeSmoothGeoPrefab => Get().largeSmoothGeoPrefab;

	public static GameObject ShellShardPrefab
	{
		get
		{
			List<GameObject> list = Get().shellShardPrefabs;
			return list[Random.Range(0, list.Count)];
		}
	}

	public static GameObject ShellShardMidPrefab => Get().shellShardMidPrefab;

	public static CollectableItemPickup CollectableItemPickupPrefab => Get().collectableItemPickupPrefab;

	public static CollectableItemPickup CollectableItemPickupInstantPrefab => Get().collectableItemPickupInstantPrefab;

	public static GenericPickup GenericPickupPrefab => Get().genericPickupPrefab;

	public static CollectableItemPickup CollectableItemPickupMeatPrefab => Get().collectableItemPickupMeatPrefab;

	public static GameObject ReaperBundlePrefab => Get().reaperBundlePrefab;

	public static FsmTemplate HornetMultiWounderFsmTemplate => Get().hornetMultiWounderFsmTemplate;

	public static CollectableItemMementoList MementoList => Get().mementoList;

	public static ToolCrest HunterCrest => Get().hunterCrest;

	public static ToolCrest HunterCrest2 => Get().hunterCrest2;

	public static int HunterComboHits => Get().hunterComboHits;

	public static float HunterComboDamageMult => Get().hunterComboDamageMult;

	public static ToolCrest HunterCrest3 => Get().hunterCrest3;

	public static int HunterCombo2Hits => Get().hunterCombo2Hits;

	public static float HunterCombo2DamageMult => Get().hunterCombo2DamageMult;

	public static GameObject HunterComboDamageEffect => Get().hunterComboDamageEffect;

	public static int HunterCombo2ExtraHits => Get().hunterCombo2ExtraHits;

	public static float HunterCombo2ExtraDamageMult => Get().hunterCombo2ExtraDamageMult;

	public static Vector2 HunterCombo2DamageExtraScale => Get().hunterCombo2DamageExtraScale;

	public static ToolCrest WarriorCrest => Get().warriorCrest;

	public static float WarriorRageDuration => Get().warriorRageDuration;

	public static float WarriorRageGraceTime => Get().warriorRageGraceTime;

	public static IReadOnlyList<float> WarriorRageHitAddTimePerHit => Get().warriorRageHitAddTimePerHit;

	public static float WarriorDamageMultiplier => Get().warriorDamageMultiplier;

	public static float WarriorRageDamagedRemoveTime => Get().warriorRageDamagedRemoveTime;

	public static ToolCrest ReaperCrest => Get().reaperCrest;

	public static float ReaperModeDuration => Get().reaperModeDuration;

	public static ToolCrest WandererCrest => Get().wandererCrest;

	public static float WandererCritChance => Get().wandererCritChance;

	public static float WandererCritMultiplier => Get().wandererCritMultiplier;

	public static float WandererCritMagnitudeMult => Get().wandererCritMagnitudeMult;

	public static GameObject WandererCritEffect => Get().wandererCritEffect;

	public static ToolCrest CursedCrest => Get().cursedCrest;

	public static ToolCrest WitchCrest => Get().witchCrest;

	public static int WitchCrestRecoilSteps => Get().witchCrestRecoilSteps;

	public static ToolCrest ToolmasterCrest => Get().toolmasterCrest;

	public static int ToolmasterQuickCraftNoneUsage => Get().toolmasterQuickCraftNoneUsage;

	public static ToolCrest SpellCrest => Get().spellCrest;

	public static float SpellCrestRuneDamageMult => Get().spellCrestRuneDamageMult;

	public static ToolCrest CloaklessCrest => Get().cloaklessCrest;

	public static ToolItem DefaultSkillTool => Get().defaultSkillTool;

	public static ToolItem LuckyDiceTool => Get().luckyDiceTool;

	public static ToolItem BarbedWireTool => Get().barbedWireTool;

	public static float BarbedWireDamageDealtMultiplier => Get().barbedWireDamageDealtMultiplier;

	public static float BarbedWireDamageTakenMultiplier => Get().barbedWireDamageTakenMultiplier;

	public static ToolItem WeightedAnkletTool => Get().weightedAnkletTool;

	public static float WeightedAnkletDmgKnockbackMult => Get().weightedAnkletDmgKnockbackMult;

	public static float WeightedAnkletDmgInvulnMult => Get().weightedAnkletDmgInvulnMult;

	public static int WeightedAnkletRecoilSteps => Get().weightedAnkletRecoilSteps;

	public static ToolItem BoneNecklaceTool => Get().boneNecklaceTool;

	public static float BoneNecklaceShellshardIncrease => Get().boneNecklaceShellshardIncrease;

	public static ToolItem LongNeedleTool => Get().longNeedleTool;

	public static Vector2 LongNeedleMultiplier => Get().longNeedleMultiplier;

	public static ToolItem DeadPurseTool => Get().deadPurseTool;

	public static float DeadPurseHoldPercent => Get().deadPurseHoldPercent;

	public static ToolItem ShellSatchelTool => Get().shellSatchelTool;

	public static float ShellSatchelToolIncrease => Get().shellSatchelToolIncrease;

	public static ToolItem SpoolExtenderTool => Get().spoolExtenderTool;

	public static int SpoolExtenderSilk => Get().spoolExtenderSilk;

	public static ToolItem WhiteRingTool => Get().whiteRingTool;

	public static float WhiteRingSilkRegenTimeMultiplier => Get().whiteRingSilkRegenTimeMultiplier;

	public static int WhiteRingSilkRegenIncrease => Get().whiteRingSilkRegenIncrease;

	public static ToolItem LavaBellTool => Get().lavaBellTool;

	public static float LavaBellCooldownTime => Get().lavaBellCooldownTime;

	public static ToolItem ThiefCharmTool => Get().thiefCharmTool;

	public static float ThiefCharmGeoSmallIncrease => Get().thiefCharmGeoSmallIncrease;

	public static float ThiefCharmGeoMedIncrease => Get().thiefCharmGeoMedIncrease;

	public static float ThiefCharmGeoLargeIncrease => Get().thiefCharmGeoLargeIncrease;

	public static MinMaxFloat ThiefCharmGeoLoss => Get().thiefCharmGeoLoss;

	public static MinMaxInt ThiefCharmGeoLossCap => Get().thiefCharmGeoLossCap;

	public static float ThiefCharmGeoLossLooseChance => Get().thiefCharmGeoLossLooseChance;

	public static MinMaxInt ThiefCharmGeoLossLooseAmount => Get().thiefCharmGeoLossLooseAmount;

	public static GameObject ThiefCharmHeroHitPrefab => Get().thiefCharmHeroHitPrefab;

	public static AudioEvent ThiefCharmEnemyDeathAudio => Get().thiefCharmEnemyDeathAudio;

	public static ToolItem ThiefPickTool => Get().thiefPickTool;

	public static MinMaxFloat ThiefPickGeoSteal => Get().thiefPickGeoSteal;

	public static MinMaxInt ThiefPickGeoStealMin => Get().thiefPickGeoStealMin;

	public static MinMaxInt ThiefPickShardSteal => Get().thiefPickShardSteal;

	public static ThiefSnatchEffect ThiefSnatchEffectPrefab => Get().thiefSnatchEffectPrefab;

	public static float ThiefPickLooseChance => Get().thiefPickLooseChance;

	public static MinMaxInt ThiefPickGeoLoose => Get().thiefPickGeoLoose;

	public static MinMaxInt ThiefPickShardLoose => Get().thiefPickShardLoose;

	public static ToolItem MaggotCharm => Get().maggotCharm;

	public static float MaggotCharmHealthLossTime => Get().maggotCharmHealthLossTime;

	public static float MaggotCharmEnterWaterAddTime => Get().maggotCharmEnterWaterAddTime;

	public static GameObject MaggotCharmHitSinglePrefab => Get().maggotCharmHitSinglePrefab;

	public static ToolItem PoisonPouchTool => Get().poisonPouchTool;

	public static DamageTag PoisonPouchDamageTag => Get().poisonPouchDamageTag;

	public static Color PoisonPouchTintColour => Get().poisonPouchTintColour;

	public static Color PoisonPouchHeroTintColour => Get().poisonPouchHeroTintColour;

	public static ToolItem ZapImbuementTool => Get().zapImbuementTool;

	public static DamageTag ZapDamageTag => Get().zapDamageTag;

	public static Color ZapDamageTintColour => Get().zapDamageTintColour;

	public static float ZapDamageMult => Get().zapDamageMult;

	public static ToolItem MusicianCharmTool => Get().musicianCharmTool;

	public static float MusicianCharmSilkDrainTimeMult => Get().musicianCharmSilkDrainTimeMult;

	public static float MusicianCharmNeedolinRangeMult => Get().musicianCharmNeedolinRangeMult;

	public static float MusicianCharmFovOffset => Get().musicianCharmFovOffset;

	public static float MusicianCharmFovStartDuration => Get().musicianCharmFovStartDuration;

	public static AnimationCurve MusicianCharmFovStartCurve => Get().musicianCharmFovStartCurve;

	public static float MusicianCharmFovEndDuration => Get().musicianCharmFovEndDuration;

	public static AnimationCurve MusicianCharmFovEndCurve => Get().musicianCharmFovEndCurve;

	public static ToolItem ScuttleCharmTool => Get().scuttleCharmTool;

	public static GameObject ScuttleEvadeEffect => Get().scuttleEvadeEffect;

	public static ToolItem BrollySpikeTool => Get().brollySpikeTool;

	public static GameObject BrollySpikeObject_dj => Get().brollySpikeObject_dj;

	public static ToolItem CompassTool => Get().compassTool;

	public static ToolItem MossCreep1Tool => Get().mossCreep1Tool;

	public static ToolItem MossCreep2Tool => Get().mossCreep2Tool;

	public static ToolItem FracturedMaskTool => Get().fracturedMaskTool;

	public static ToolItem CurveclawTool => Get().curveclawTool;

	public static ToolItem CurveclawUpgradedTool => Get().curveclawUpgradedTool;

	public static ToolItem SprintmasterTool => Get().sprintmasterTool;

	public static ToolItem MultibindTool => Get().multibindTool;

	public static ToolItem QuickSlingTool => Get().quickSlingTool;

	public static ToolItem WallClingTool => Get().wallClingTool;

	public static ToolItem WispLanternTool => Get().wispLanternTool;

	public static ToolItem FleaCharmTool => Get().fleaCharmTool;

	public static FullQuestBase HuntressQuest => Get().huntressQuest;

	public static ShopItemList MapperStock => Get().mapperStock;

	public static QuestTargetPlayerDataBools FleasCollectedCounter => Get().fleasCollectedCounter;

	public static int FleasCollectedCount
	{
		get
		{
			QuestTargetPlayerDataBools questTargetPlayerDataBools = Get().fleasCollectedCounter;
			if (!questTargetPlayerDataBools)
			{
				return 0;
			}
			return questTargetPlayerDataBools.CountCompleted();
		}
	}

	public static QuestBoardList EnclaveQuestBoard => Get().enclaveQuestBoard;

	public static bool IsShellShardPrefab(GameObject prefab)
	{
		return Get().shellShardPrefabs.Contains(prefab);
	}

	[RuntimeInitializeOnLoadMethod]
	public static void PreWarm()
	{
		GlobalSettingsBase<Gameplay>.StartPreloadAddressable("Global Gameplay Settings");
	}

	public static void Unload()
	{
		GlobalSettingsBase<Gameplay>.StartUnload();
	}

	private static Gameplay Get()
	{
		return GlobalSettingsBase<Gameplay>.Get("Global Gameplay Settings");
	}

	private void OnValidate()
	{
		ArrayForEnumAttribute.EnsureArraySize(ref currencyCaps, typeof(CurrencyType));
		ArrayForEnumAttribute.EnsureArraySize(ref maxCurrencyObjects, typeof(CurrencyType));
	}

	private void Awake()
	{
		OnValidate();
	}

	public static int GetCurrencyCap(CurrencyType type)
	{
		int num = Get().currencyCaps[(int)type];
		num += Mathf.FloorToInt((float)num * ToolPouchUpgradeIncrease * (float)PlayerData.instance.ToolPouchUpgrades);
		if (num <= 0)
		{
			return 9999999;
		}
		return num;
	}

	public static int GetMaxCurrencyObjects(CurrencyType type)
	{
		return Get().maxCurrencyObjects[(int)type];
	}
}
