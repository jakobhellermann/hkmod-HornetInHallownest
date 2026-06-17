using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using GlobalEnums;
using GlobalSettings;
using Newtonsoft.Json;
using UnityEngine;

namespace Silksong;

[Serializable]
[JsonObject(MemberSerialization.Fields)]
public class PlayerData : PlayerDataBase
{
	private class MapBoolList
	{
		private List<Func<bool>> mapGetters = new List<Func<bool>>();

		public bool HasAnyMap
		{
			get
			{
				foreach (Func<bool> mapGetter in mapGetters)
				{
					if (mapGetter != null && mapGetter())
					{
						return true;
					}
				}
				return false;
			}
		}

		public bool HasAllMaps
		{
			get
			{
				foreach (Func<bool> mapGetter in mapGetters)
				{
					if (mapGetter != null && !mapGetter())
					{
						return false;
					}
				}
				return true;
			}
		}

		public int HasCount
		{
			get
			{
				int num = 0;
				foreach (Func<bool> mapGetter in mapGetters)
				{
					if (mapGetter != null && mapGetter())
					{
						num++;
					}
				}
				return num;
			}
		}

		public MapBoolList(PlayerData playerData)
		{
			if (playerData != null)
			{
				BuildTargetList(playerData);
			}
		}

		private void BuildTargetList(PlayerData dataSource)
		{
			if (dataSource == null)
			{
				return;
			}
			Type type = dataSource.GetType();
			Type typeFromHandle = typeof(bool);
			FieldInfo[] fields = type.GetFields();
			PropertyInfo[] properties = type.GetProperties();
			Func<string, bool> func = (string varName) => varName.StartsWith("Has") && varName.EndsWith("Map") && !varName.Equals("HasAnyMap");
			FieldInfo[] array = fields;
			foreach (FieldInfo fieldInfo in array)
			{
				if (fieldInfo.FieldType == typeFromHandle && func(fieldInfo.Name))
				{
					mapGetters.Add(() => (bool)fieldInfo.GetValue(dataSource));
				}
			}
			PropertyInfo[] array2 = properties;
			foreach (PropertyInfo propertyInfo in array2)
			{
				if (propertyInfo.PropertyType == typeFromHandle && func(propertyInfo.Name))
				{
					mapGetters.Add(() => (bool)propertyInfo.GetValue(dataSource, null));
				}
			}
		}
	}

	public string LastSetFieldName;

	[DefaultValue("1.0.30000")]
	public string version;

	[DefaultValue(28104)]
	public int RevisionBreak;

	public string date;

	public int profileID;

	public float playTime;

	public bool openingCreditsPlayed;

	public PermadeathModes permadeathMode;

	public bool CollectedDockDemoKey;

	public HeroItemsState PreMemoryState;

	public bool HasStoredMemoryState;

	[DefaultValue(5)]
	public int health;

	[DefaultValue(5)]
	public int maxHealth;

	[DefaultValue(5)]
	public int maxHealthBase;

	public int healthBlue;

	[NonSerialized]
	public bool damagedBlue;

	[NonSerialized]
	public bool damagedPurple;

	[DefaultValue(5)]
	public int prevHealth;

	public int heartPieces;

	public bool SeenBindPrompt;

	public int geo;

	public int silk;

	[DefaultValue(9)]
	public int silkMax;

	public int silkRegenMax;

	[NonSerialized]
	public int silkParts;

	[DefaultValue(true)]
	public bool IsSilkSpoolBroken;

	[NonSerialized]
	public bool UnlockSilkFinalCutscene;

	public int silkSpoolParts;

	public bool atBench;

	[DefaultValue("Tut_01")]
	public string respawnScene;

	[DefaultValue(MapZone.MOSS_CAVE)]
	public MapZone mapZone;

	public ExtraRestZones extraRestZone;

	[DefaultValue("Death Respawn Marker Init")]
	public string respawnMarkerName;

	public int respawnType;

	[NonSerialized]
	public string tempRespawnScene;

	[NonSerialized]
	public string tempRespawnMarker;

	[NonSerialized]
	public int tempRespawnType;

	[NonSerialized]
	public string nonLethalRespawnScene;

	[NonSerialized]
	public string nonLethalRespawnMarker;

	[NonSerialized]
	public int nonLethalRespawnType;

	[NonSerialized]
	public Vector3 hazardRespawnLocation;

	public HazardRespawnMarker.FacingDirection hazardRespawnFacing;

	public string HeroCorpseScene;

	public Vector2 HeroDeathScenePos;

	public Vector2 HeroDeathSceneSize;

	public byte[] HeroCorpseMarkerGuid;

	public HeroDeathCocoonTypes HeroCorpseType;

	public int HeroCorpseMoneyPool;

	public int nailRange;

	public int beamDamage;

	public int nailUpgrades;

	public bool InvNailHasNew;

	public bool hasSilkSpecial;

	public int silkSpecialLevel;

	public bool hasNeedleThrow;

	public bool hasThreadSphere;

	public bool hasParry;

	public bool hasHarpoonDash;

	public bool hasSilkCharge;

	public bool hasSilkBomb;

	public bool hasSilkBossNeedle;

	public bool hasNeedolin;

	public int attunement;

	[DefaultValue(1)]
	public int attunementLevel;

	public bool hasNeedolinMemoryPowerup;

	public bool hasDash;

	public bool hasBrolly;

	public bool hasWalljump;

	public bool hasDoubleJump;

	public bool hasQuill;

	public bool hasChargeSlash;

	public bool hasSuperJump;

	public int QuillState;

	public bool HasSeenDash;

	public bool HasSeenWalljump;

	public bool HasSeenSuperJump;

	public bool HasSeenNeedolin;

	public bool HasSeenNeedolinUp;

	public bool HasSeenNeedolinDown;

	public bool HasSeenHarpoon;

	public bool HasSeenEvaHeal;

	public int cloakOdour_slabFly;

	public bool HasSeenSilkHearts;

	public bool hasKilled;

	public SaveSlotCompletionIcons.CompletionState CompletedEndings;

	public SaveSlotCompletionIcons.CompletionState LastCompletedEnding;

	public bool fixerQuestBoardConvo;

	public bool fixerAcceptedQuestConvo;

	public bool fixerBridgeConstructed;

	public bool fixerBridgeBreaking;

	public bool fixerBridgeBroken;

	public bool fixerStatueConstructed;

	public bool fixerStatueConvo;

	public bool metSherma;

	public bool seenBellBeast;

	public int shermaPos;

	public bool shermaConvoBellBeast;

	public bool metShermaPilgrimsRest;

	public bool shermaInBellhart;

	public bool shermaSeenInBellhart;

	public bool shermaSeenInSteps;

	public bool shermaAtSteps;

	public bool shermaConvoCoralBench;

	public bool shermaConvoCoralJudges;

	public bool hasActivatedBellBench;

	public bool shermaWokeInSteps;

	public bool enteredCoral_10;

	public bool shermaCitadelEntrance_Visiting;

	public bool shermaCitadelEntrance_Seen;

	public bool shermaCitadelEntrance_Left;

	public bool openedCitadelSpaLeft;

	public bool openedCitadelSpaRight;

	[DefaultValue(true)]
	public bool shermaCitadelSpa_Visiting;

	public bool shermaCitadelSpa_Seen;

	public bool shermaCitadelSpa_Left;

	public bool shermaCitadelSpa_ExtraConvo;

	public bool shermaInEnclave;

	public bool shermaCitadelEnclave_Seen;

	public bool metShermaEnclave;

	public bool shermaEnclaveHealingConvo;

	public bool shermaQuestActive;

	public bool shermaHealerActive;

	[DefaultValue(1)]
	public int shermaWoundedPilgrim;

	public bool shermaCaretakerConvo1;

	public bool shermaCaretakerConvoFinal;

	public bool metMapper;

	public bool mapperRosaryConvo;

	public bool mapperMentorConvo;

	public bool mapperQuillConvo;

	public bool mapperMappingConvo;

	public bool mapperCalledConvo;

	public bool mapperHauntedBellhartConvo;

	public bool mapperBellhartConvo;

	public bool mapperBellhartConvoTimePassed;

	public bool mapperBellhartConvo2;

	public bool mapperAway;

	public bool mapperMetInAnt04;

	public bool mapperTubeConvo;

	public bool mapperBrokenBenchConvo;

	public bool mapperCursedConvo;

	public bool mapperMaggottedConvo;

	public bool mapperSellingTubePins;

	public bool mapperMasterAfterConvo;

	public bool mapperReactedToBrokenBellBench;

	public bool SeenMapperBonetown;

	public bool MapperLeftBonetown;

	public bool MapperAppearInBellhart;

	public bool SeenMapperBoneForest;

	public bool MapperLeftBoneForest;

	public bool SeenMapperDocks;

	public bool MapperLeftDocks;

	public bool SeenMapperWilds;

	public bool MapperLeftWilds;

	public bool SeenMapperCrawl;

	public bool MapperLeftCrawl;

	public bool SeenMapperGreymoor;

	public bool MapperLeftGreymoor;

	public bool SeenMapperBellhart;

	public bool MapperLeftBellhart;

	public bool SeenMapperShellwood;

	public bool MapperLeftShellwood;

	public bool SeenMapperHuntersNest;

	public bool MapperLeftHuntersNest;

	public bool SeenMapperJudgeSteps;

	public bool MapperLeftJudgeSteps;

	public bool SeenMapperDustpens;

	public bool MapperLeftDustpens;

	public bool SeenMapperPeak;

	public bool MapperLeftPeak;

	public bool SeenMapperShadow;

	public bool MapperLeftShadow;

	public bool SeenMapperCoralCaverns;

	public bool MapperLeftCoralCaverns;

	public bool mapperSparIntro;

	public int mapperLocationAct3;

	public bool seenMapperAct3;

	[DefaultValue(true)]
	public bool mapperIsFightingAct3;

	[DefaultValue(1)]
	public int mapperFightGroup;

	public bool mapperConvo_Act3Intro;

	public bool mapperConvo_Act3IntroTimePassed;

	public bool mapperConvo_Act3NoStock;

	public bool mapperConvo_WhiteFlower;

	public bool metDruid;

	public bool druidTradeIntro;

	public int druidMossBerriesSold;

	public int[] mossBerryValueList;

	public bool druidAct3Intro;

	public bool metLearnedPilgrim;

	public bool metLearnedPilgrimAct3;

	public bool metDicePilgrim;

	public bool dicePilgrimDefeated;

	public int dicePilgrimState;

	public bool dicePilgrimGameExplained;

	[DefaultValue(16)]
	public int dicePilgrimBank;

	public bool metGarmond;

	public bool garmondMoorwingConvo;

	public bool garmondMoorwingConvoReady;

	public bool garmondPurposeConvo;

	public bool garmondSeenInGreymoor10;

	public bool garmondInDust05;

	public bool garmondSeenInDust05;

	public bool garmondEncounterCooldown;

	public bool enteredSong_19;

	public bool enteredSong_01;

	public bool enteredSong_02;

	public bool garmondInSong01;

	public bool garmondSeenInSong01;

	public bool garmondInSong02;

	public bool garmondSeenInSong02;

	public bool enteredSong_13;

	public bool garmondInSong13;

	public bool garmondSeenInSong13;

	public bool enteredSong_17;

	public bool garmondInSong17;

	public bool garmondSeenInSong17;

	public bool garmondInLibrary;

	public bool garmondLibrarySeen;

	public bool garmondLibraryMet;

	public bool garmondLibraryOffered;

	public bool garmondLibraryDefeatedHornet;

	public bool garmondWillAidInForumBattle;

	public bool garmondInEnclave;

	public bool garmondMetEnclave;

	public int garmondEncounters_act3;

	public bool metGarmondAct3;

	public bool garmondFinalQuestReady;

	public bool garmondBlackThreadDefeated;

	public bool pilgrimRestMerchant_SingConvo;

	public bool pilgrimRestMerchant_RhinoRuckusConvo;

	public int pilgrimRestCrowd;

	[DefaultValue(true)]
	public bool nuuIsHome;

	public bool MetHalfwayHunterFan;

	public bool MetHunterFanOutside;

	public bool nuuVisiting_splinterQueen;

	public bool nuuEncountered_splinterQueen;

	public bool nuuVisiting_coralDrillers;

	public bool nuuEncountered_coralDrillers;

	public bool nuuVisiting_skullKing;

	public bool nuuEncountered_skullKing;

	public bool nuuVisiting_zapNest;

	public bool nuuEncountered_zapNest;

	public bool nuuSlappedOutside;

	public bool nuuIntroAct3;

	public bool nuuMementoAwarded;

	public bool gillyMet;

	public bool gillyIntroduced;

	public bool gillyStatueConvo;

	public bool gillyTrapConvo;

	public bool gillyHunterCampConvo;

	public bool gillyAct3Convo;

	public int gillyLocation;

	public int gillyLocationAct3;

	public bool gillyQueueMovingOn;

	public string dreamReturnScene;

	public bool hasJournal;

	public bool seenJournalMsg;

	public bool seenMateriumMsg;

	public bool seenJournalQuestUpdateMsg;

	public EnemyJournalKillData EnemyJournalKillData;

	public int currentInvPane;

	public bool showGeoUI;

	public bool showHealthUI;

	public bool promptFocus;

	public bool seenFocusTablet;

	public bool seenDreamNailPrompt;

	[DefaultValue(true)]
	public bool isFirstGame;

	public bool enteredTutorialFirstTime;

	public bool isInvincible;

	public bool infiniteAirJump;

	public string currentArea;

	public bool visitedMossCave;

	public bool visitedBoneBottom;

	public bool visitedBoneForest;

	public bool visitedMosstown;

	public bool visitedHuntersTrail;

	public bool visitedDeepDocks;

	public bool visitedWilds;

	public bool visitedGrove;

	public bool visitedGreymoor;

	public bool visitedWisp;

	public bool visitedBellhartHaunted;

	public bool visitedBellhart;

	public bool visitedBellhartSaved;

	public bool visitedShellwood;

	public bool visitedCrawl;

	public bool visitedDustpens;

	public bool visitedShadow;

	public bool visitedAqueducts;

	public bool visitedMistmaze;

	public bool visitedCoral;

	public bool visitedCoralRiver;

	public bool visitedCoralRiverInner;

	public bool visitedCoralTower;

	public bool visitedSlab;

	public bool visitedGrandGate;

	public bool visitedCitadel;

	public bool visitedUnderstore;

	public bool visitedWard;

	public bool visitedHalls;

	public bool visitedLibrary;

	public bool visitedStage;

	public bool visitedGloom;

	public bool visitedWeave;

	public bool visitedMountain;

	public bool visitedIceCore;

	public bool visitedHang;

	public bool visitedHangAtrium;

	public bool visitedEnclave;

	public bool visitedArborium;

	public bool visitedCogwork;

	public bool visitedCradle;

	public bool visitedRuinedCradle;

	public bool visitedFleatopia;

	public bool visitedFleaFestival;

	public bool visitedAbyss;

	public bool citadelHalfwayComplete;

	public HashSet<string> scenesVisited;

	public HashSet<string> scenesMapped;

	public HashSet<string> scenesEncounteredBench;

	public HashSet<string> scenesEncounteredCocoon;

	public bool mapUpdateQueued;

	public bool mapAllRooms;

	public bool HasSeenMapUpdated;

	public bool HasSeenMapMarkerUpdated;

	public bool HasMossGrottoMap;

	public bool HasWildsMap;

	public bool HasBoneforestMap;

	public bool HasDocksMap;

	public bool HasGreymoorMap;

	public bool HasBellhartMap;

	public bool HasShellwoodMap;

	public bool HasCrawlMap;

	public bool HasHuntersNestMap;

	public bool HasJudgeStepsMap;

	public bool HasDustpensMap;

	public bool HasSlabMap;

	public bool HasPeakMap;

	public bool HasCitadelUnderstoreMap;

	public bool HasCoralMap;

	public bool HasSwampMap;

	public bool HasCloverMap;

	public bool HasAbyssMap;

	public bool HasHangMap;

	public bool HasSongGateMap;

	public bool HasHallsMap;

	public bool HasWardMap;

	public bool HasCogMap;

	public bool HasLibraryMap;

	public bool HasCradleMap;

	public bool HasArboriumMap;

	public bool HasAqueductMap;

	public bool HasWeavehomeMap;

	public bool act3MapUpdated;

	[NonSerialized]
	[JsonIgnore]
	private MapBoolList mapBoolList;

	public bool ShakraFinalQuestAppear;

	public bool hasPinBench;

	public bool hasPinCocoon;

	public bool hasPinShop;

	public bool hasPinSpa;

	public bool hasPinStag;

	public bool hasPinTube;

	public bool hasPinFleaMarrowlands;

	public bool hasPinFleaMidlands;

	public bool hasPinFleaBlastedlands;

	public bool hasPinFleaCitadel;

	public bool hasPinFleaPeaklands;

	public bool hasPinFleaMucklands;

	private static FieldInfo[] _savedFleaFields;

	public bool hasMarker;

	public bool hasMarker_a;

	public bool hasMarker_b;

	public bool hasMarker_c;

	public bool hasMarker_d;

	public bool hasMarker_e;

	public WrappedVector2List[] placedMarkers;

	public EnvironmentTypes environmentType;

	public int previousDarkness;

	public bool HasMelodyArchitect;

	public bool HasMelodyLibrarian;

	public bool SeenMelodyLibrarianReturn;

	public bool HasMelodyConductor;

	public bool UnlockedMelodyLift;

	public bool MelodyLiftCanReturn;

	public bool HeardMelodyConductorNoQuest;

	public bool ConductorWeaverDlgQueued;

	public bool ConductorWeaverDlgHeard;

	public bool muchTimePassed;

	[DefaultValue(1)]
	public int pilgrimGroupBonegrave;

	[DefaultValue(1)]
	public int pilgrimGroupShellgrave;

	[DefaultValue(1)]
	public int pilgrimGroupGreymoorField;

	public bool shellGravePopulated;

	public bool bellShrineBoneForest;

	public bool bellShrineWilds;

	public bool bellShrineGreymoor;

	public bool bellShrineShellwood;

	public bool bellShrineBellhart;

	public bool bellShrineEnclave;

	public bool completedMemory_reaper;

	public bool completedMemory_wanderer;

	public bool completedMemory_beast;

	public bool completedMemory_witch;

	public bool completedMemory_toolmaster;

	public bool completedMemory_shaman;

	public bool chapelClosed_reaper;

	public bool chapelClosed_wanderer;

	public bool chapelClosed_beast;

	public bool chapelClosed_witch;

	public bool chapelClosed_toolmaster;

	public bool chapelClosed_shaman;

	public bool bindCutscenePlayed;

	public bool encounteredMossMother;

	public bool defeatedMossMother;

	public bool entered_Tut01b;

	public bool completedTutorial;

	public bool BonePlazaOpened;

	public bool sawPlinneyLeft;

	public bool savedPlinney;

	public bool savedPlinneyConvo;

	public bool defeatedMossEvolver;

	public bool wokeMossEvolver;

	public bool MetCrestUpgrader;

	public bool MetCrestUpgraderAct3;

	public bool CrestPreUpgradeTalked;

	public bool CrestPreUpgradeAdditional;

	public bool CrestPurposeQueued;

	public bool CrestTalkedPurpose;

	public bool CrestUpgraderTalkedSnare;

	public bool CrestUpgraderOfferedFinal;

	public bool HasBoundCrestUpgrader;

	public bool churchKeeperIntro;

	public bool churchKeeperCursedConvo;

	public bool churchKeeperBonegraveConvo;

	public bool bonebottomQuestBoardFixed;

	public bool EncounteredBonetownBoss;

	public bool DefeatedBonetownBoss;

	public bool boneBottomAddition_RagLine;

	public bool seenPilbyLeft;

	public bool seenPebbLeft;

	public bool seenBonetownDestroyed;

	public bool bonetownPilgrimRoundActive;

	public bool bonetownPilgrimRoundSeen;

	public bool bonetownPilgrimHornedActive;

	public bool bonetownPilgrimHornedSeen;

	[DefaultValue(20)]
	public int bonetownPilgrimRoundCount;

	[DefaultValue(10)]
	public int bonetownPilgrimHornedCount;

	public bool ChurchKeeperLeftBasement;

	public bool BoneBottomShellFrag1;

	public bool SeenBoneBottomShopKeep;

	public bool MetBoneBottomShopKeep;

	public bool HeardBoneBottomShopKeepPostBoss;

	public bool PurchasedBonebottomFaithToken;

	public bool PurchasedBonebottomHeartPiece;

	public bool PurchasedBonebottomToolMetal;

	public bool BoneBottomShopKeepWillLeave;

	public bool BoneBottomShopKeepLeft;

	[DefaultValue(1)]
	public int bonetownCrowd;

	public bool grindleReleasedFromBonejail;

	public bool explodeWallMosstown3;

	public bool bonegraveOpen;

	public bool mosstownAspidBerryCollected;

	public bool bonegraveAspidBerryCollected;

	public bool bonegraveRosaryPilgrimDefeated;

	public bool bonegravePilgrimCrowdsCanReturn;

	public bool ShopkeeperQuestMentioned;

	public bool belltownBasementBreakWall;

	public bool basementAntWall;

	public bool hunterInfestationBoneForest;

	public bool skullKingShortcut;

	public bool skullKingAwake;

	public bool skullKingDefeated;

	public bool skullKingDefeatedBlackThreaded;

	public bool skullKingWillInvade;

	public bool skullKingInvaded;

	public bool skullKingKilled;

	public bool skullKingBenchMended;

	public bool skullKingPlatMended;

	public bool learnedPilbyName;

	public int pilbyFriendship;

	public bool pilbyMeetConvo;

	public bool pilbyCampConvo;

	public bool pilbyFirstRepeatConvo;

	public bool pilbyMosstownConvo;

	public bool pilbyGotSprintConvo;

	public bool pilbyBellhartConvo;

	public bool pilbyKilled;

	public bool pilbyAtPilgrimsRest;

	public bool pilbyInsidePilgrimsRest;

	public bool pilbySeenAtPilgrimsRest;

	public bool pilbyLeftPilgrimsRest;

	public bool pilbyPilgrimsRestMeetConvo;

	public bool boneBottomFuneral;

	public bool boneBottomFuneralComplete;

	public int BonebottomBellwayPilgrimState;

	public bool BonebottomBellwayPilgrimScared;

	public bool BonebottomBellwayPilgrimLeft;

	public bool greatBoneGateOpened;

	public bool bone01shortcutPlat;

	public bool didPilgrimIntroScene;

	public bool mosstown01_shortcut;

	public bool encounteredBellBeast;

	public bool defeatedBellBeast;

	public bool bonetownAspidBerryCollected;

	public int pinGalleriesCompleted;

	public bool PilgrimStomperNPCOffered;

	public bool pilgrimQuestSpoolCollected;

	public bool Bone_East_04b_ExplodeWall;

	public bool bone03_openedTrapdoor;

	public bool bone03_openedTrapdoorForRockRoller;

	public bool rockRollerDefeated_bone01;

	public bool rockRollerDefeated_bone06;

	public bool rockRollerDefeated_bone07;

	public bool collectorEggsHatched;

	public bool creaturesReturnedToBone10;

	public bool ant02GuardDefeated;

	public bool antBenchTrapDefused;

	public bool ant04_battleCompleted;

	public bool ant04_enemiesReturn;

	public int enemyGroupAnt04;

	public bool antMerchantKilled;

	public bool ant21_InitBattleCompleted;

	public bool ant21_ExtraBattleAdded;

	public bool metAntQueenNPC;

	public bool antQueenNPC_deepMelodyConvo;

	public bool defeatedAntQueen;

	public bool defeatedAntQueenAfterRedMemory;

	public bool tookRestroomRosaries;

	public bool encounteredLace1;

	public bool encounteredLace1Grotto;

	public bool encounteredLaceBlastedBridge;

	public bool defeatedLace1;

	public bool laceLeftDocks;

	public bool encounteredSongGolem;

	public bool defeatedSongGolem;

	public bool destroyedSongGolemRock;

	public bool boneEast07_openedMidRoof;

	public bool openedTallGeyser;

	public bool openedGeyserShaft;

	public bool openedSongGateDocks;

	public bool openedDocksBackEntrance;

	public bool docksBomberAmbush;

	public bool docks_02_shortcut_right;

	public bool docks_02_shortcut_left;

	public bool gotPastDockSpearThrower;

	public bool encounteredDockForemen;

	public bool defeatedDockForemen;

	public bool boneEastJailerKilled;

	public bool boneEastJailerClearedOut;

	public bool MetPilgrimsRestShop;

	public bool SeenMortLeft;

	public bool SeenMortDead;

	public int PilgrimsRestShopIdleTalkState;

	public bool PurchasedPilgrimsRestToolPouch;

	public bool PurchasedPilgrimsRestMemoryLocket;

	public bool PilgrimsRestDoorBroken;

	public bool pilgrimsRestRosaryThiefCowardLeft;

	public bool mortKeptWeightedAnklet;

	public bool rhinoChurchUnlocked;

	public bool churchRhinoKilled;

	public bool rhinoRampageCompleted;

	public bool rhinoRuckus;

	public bool didRhinoRuckus;

	public bool churchRhinoBlackThreadCorpse;

	public bool MetAntMerchant;

	public bool SeenAntMerchantDead;

	public bool antMerchantShortcut;

	public bool defeatedBoneFlyerGiant;

	public bool defeatedBoneFlyerGiantGolemScene;

	public bool openedBeastmasterDen;

	public bool openedCauldronShortcut;

	public bool visitedBoneEast14b;

	public bool cauldronShortcutUpdraft;

	public bool lavaChallengeEntranceCavedIn;

	public bool completedLavaChallenge;

	public bool lavaSpittersEmerge;

	public bool IsPinGallerySetup;

	public bool MetPinChallengeBug;

	public bool WasInPinChallenge;

	public bool PinGalleryLastChallengeOpen;

	public bool PinGalleryHasPlayedFinalChallenge;

	[DefaultValue(380)]
	public int PinGalleryWallet;

	public bool HuntressQuestOffered;

	public bool HuntressRuntQuestOffered;

	public bool HuntressRuntAppeared;

	public bool MottledChildGivenTool;

	public bool MottledChildNewTool;

	public bool encounteredAntTrapper;

	public bool defeatedAntTrapper;

	public bool explodeWallBoneEast18c;

	public bool defeatedGuardBoneEast25;

	public bool CompletedWeaveSprintChallenge;

	public bool CompletedWeaveSprintChallengeMax;

	public bool crashingIntoGreymoor;

	public bool crashedIntoGreymoor;

	public bool greymoor_04_battleCompleted;

	public bool greymoor_10_entered;

	public bool greymoor_05_centipedeArrives;

	public bool killedRoostingCrowman;

	public bool hitCrowCourtSwitch;

	public bool tookGreymoor17Spool;

	public bool completedGreymoor17Battle;

	public bool CrowCourtInSession;

	public string CrowSummonsAppearedScene;

	public bool OpenedCrowSummonsDoor;

	public bool PickedUpCrowMemento;

	public bool MetHalfwayBartender;

	public bool HalfwayPatronsCanVisit;

	public bool SeenHalfwayPatronLeft;

	public bool HalfwayPatronLeftGone;

	public bool SeenHalfwayPatronRight;

	public bool HalfwayPatronRightGone;

	public int HalfwayDrinksPurchased;

	public bool DeclinedBartenderDrink;

	public bool HalfwayBartenderOfferedQuest;

	public bool HalfwayBartenderCursedConvo;

	public bool HalfwayBartenderHauntedBellhartConvo;

	public bool visitedHalfway;

	public int halfwayCrowd;

	public bool HalfwayScarecrawAppeared;

	public bool HalfwayNectarOffered;

	public bool HalfwayNectarPaid;

	public bool MetHalfwayBartenderAct3;

	[DefaultValue(2)]
	public int halfwayCrowEnemyGroup;

	public bool brokeUnderstoreFloor;

	public bool enteredGreymoor05;

	public bool previouslyVisitedGreymoor_05;

	public bool greymoor05_clearedOut;

	public bool greymoor05_killedJailer;

	public bool greymoor05_farmerPlatBroken;

	public bool greymoor08_plat_destroyed;

	public bool encounteredVampireGnat_05;

	public bool allowVampireGnatInAltLoc;

	public bool encounteredVampireGnat_07;

	public bool encounteredVampireGnatBoss;

	public bool defeatedVampireGnatBoss;

	public int vampireGnatDeaths;

	public bool vampireGnatRequestedAid;

	public bool VampireGnatDefeatedBeforeCaravanArrived;

	public bool VampireGnatCorpseOnCaravan;

	public bool VampireGnatCorpseInWater;

	public bool encounteredCrowCourt;

	public bool defeatedCrowCourt;

	public bool defeatedWispPyreEffigy;

	public bool wisp02_enemiesReturned;

	public bool crawl03_oneWayWall;

	public bool roofCrabEncountered;

	public bool roofCrabDefeated;

	public bool littleCrabsAppeared;

	public bool aspid06_battleComplete;

	public bool aspid06_cloverStagsReturned;

	public bool aspid07_cloverStagsReturned;

	public int whiteCloverPos;

	public bool aspid_04_gate;

	public bool aspid_16_oneway;

	public bool aspid_16_relic;

	public bool aspid_04b_battleCompleted;

	public bool aspid_04b_wildlifeReturned;

	public bool pilgrimFisherPossessed;

	public int spinnerEncounter;

	public bool encounteredSpinner;

	public bool spinnerDefeated;

	public bool SpinnerDefeatedTimePassed;

	public bool shellwood14_ambushed;

	public bool shellwoodTwigShortcut;

	public bool encounteredSplinterQueen;

	public bool defeatedSplinterQueen;

	public int splinterQueenSproutTimer;

	public bool splinterQueenSproutGrewLarge;

	public bool splinterQueenSproutCut;

	public bool shellwood13_BellWall;

	public bool defeatedShellwoodRosaryPilgrim;

	public bool shellwoodBellshrineTwigWall;

	public bool seenEmptyShellwood16;

	public bool slabFlyInShellwood16;

	public bool shellwoodSlabflyDefeated;

	public bool visitedShellwood_16;

	public bool sethShortcut;

	public bool encounteredSeth;

	public int sethConvo;

	public bool defeatedSeth;

	public bool sethRevived;

	public bool sethLeftShellwood;

	public SethNpcLocations SethNpcLocation;

	public bool MetSethNPC;

	public bool SethJoinedFleatopia;

	public bool encounteredFlowerQueen;

	public bool defeatedFlowerQueen;

	public bool flowerQueenHeartAppeared;

	public bool MetWoodWitch;

	public bool WoodWitchOfferedItemQuest;

	public bool WoodWitchOfferedFlowerQuest;

	public bool WoodWitchTalkedPostQuest;

	public bool WoodWitchOfferedCurse;

	public bool WoodWitchGaveMandrake;

	public bool gainedCurse;

	public bool BlueScientistMet;

	public bool BlueScientistQuestOffered;

	public bool BlueAssistantCorpseFound;

	public bool BlueAssistantEnemyEncountered;

	[DefaultValue(4)]
	public int BlueAssistantBloodCount;

	public bool BlueScientistTalkedCorpse;

	public bool BlueScientistPreQuest2Convo;

	public bool BlueScientistQuest2Offered;

	public bool BlueScientistQuest3Offered;

	public bool BlueScientistInfectedSeen;

	public bool BlueScientistInfectedMet;

	public bool BlueScientistDead;

	public bool BlueScientistSceneryPustulesGrown;

	public bool dust01_battleCompleted;

	public bool dust01_returnReady;

	public bool dust03_battleCompleted;

	public bool dust03_returnReady;

	public bool openedDust05Gate;

	public bool dust05EnemyClearedOut;

	public bool CollectedDustCageKey;

	public bool UnlockedDustCage;

	public GreenPrinceLocations GreenPrinceLocation;

	public bool GreenPrinceSeenSong04;

	public bool FixedDustBellBench;

	public bool silkFarmBattle1_complete;

	public bool grubFarmerEmerged;

	public bool metGrubFarmer;

	public int grubFarmLevel;

	public bool farmer_grewFirstGrub;

	public bool farmer_grubGrowing_1;

	public bool farmer_grubGrown_1;

	public bool farmer_grubGrowing_2;

	public bool farmer_grubGrown_2;

	public bool farmer_grubGrowing_3;

	public bool farmer_grubGrown_3;

	public bool grubFarmer_firstGrubConvo;

	public bool grubFarmer_needolinConvo1;

	public float grubFarmerTimer;

	public bool silkFarmAbyssCoresCleared;

	public bool metGrubFarmerAct3;

	public bool DustTradersOfferedQuest;

	public bool DustTradersOfferedPins;

	public bool defeatedRoachkeeperChef;

	public bool gotPickledRoachEgg;

	public bool roachkeeperChefCorpsePrepared;

	public bool MetGrubFarmerMimic;

	public int[] GrubFarmerMimicValueList;

	public int GrubFarmerSilkGrubsSold;

	public bool encounteredPhantom;

	public bool defeatedPhantom;

	public bool metSwampMuckmen;

	public bool visitedShadow03;

	public bool swampMuckmanTallInvades;

	public bool DefeatedSwampShaman;

	public bool thievesReturnedToShadow28;

	public bool SeenBelltownCutscene;

	public bool belltownCrowdsReady;

	public int belltownCrowd;

	public bool MetBelltownShopkeep;

	public bool BelltownShopkeepCourierConvo1Accepted;

	public bool BelltownShopkeepCourierConvo1Completed;

	public bool BelltownShopkeepCursedConvo;

	public bool BelltownShopkeepHouseConvo;

	public bool BelltownShopkeepAct3Convo;

	public bool PurchasedBelltownShellFragment;

	public bool PurchasedBelltownToolPouch;

	public bool PurchasedBelltownSpoolSegment;

	public bool PurchasedBelltownMemoryLocket;

	public int BelltownGreeterConvo;

	public bool BelltownGreetCursedConvo;

	public bool BelltownGreeterHouseHalfDlg;

	public bool BelltownGreeterHouseFullDlg;

	public bool BelltownGreeterFurnishingDlg;

	public bool BelltownGreeterMetTimePassed;

	public bool BelltownGreeterTwistedBudDlg;

	public bool BelltownCouriersMet;

	public bool BelltownCouriersMetAct3;

	public bool BelltownCouriersGourmandHint;

	public bool BelltownCouriersTalkedCursed;

	public bool BelltownCouriersTalkedGourmand;

	public bool BelltownCouriersBrokenDlgQueued;

	public bool BelltownCouriersBrokenDlg;

	public bool BelltownCouriersNotPurchasedDlg;

	public int BelltownCouriersPurchasedDlgBitmask;

	public bool BelltownCouriersFirstBeginDlg;

	public List<string> BelltownCouriersGenericQuests;

	public string BelltownCouriersLastCompletedQuest;

	public bool PinsmithMetBelltown;

	public bool PinsmithQuestOffered;

	public bool PinsmithUpg2Offered;

	public bool PinsmithUpg3Offered;

	public bool PinsmithUpg4Offered;

	public bool BelltownHermitMet;

	public int BelltownHermitEnslavedConvo;

	public int BelltownHermitSavedConvo;

	public bool BelltownHermitCursedConvo;

	public bool BelltownHermitConvoCooldown;

	public bool MetBelltownBagpipers;

	public bool BelltownBagpipersOfferedQuest;

	public bool MetBelltownDoctorDoor;

	public bool MetBelltownDoctorDoorAct3;

	public bool MetBelltownDoctor;

	public bool BelltownDoctorQuestOffered;

	public bool BelltownDoctorFixOffered;

	public bool BelltownDoctorMaggotSpoke;

	public bool BelltownDoctorLifebloodSpoke;

	public bool BelltownDoctorCuredCurse;

	public int BelltownDoctorConvo;

	public bool MetFisherHomeBasic;

	public bool MetFisherHomeFull;

	public float FisherWalkerTimer;

	public bool FisherWalkerDirection;

	public float FisherWalkerIdleTimeLeft;

	public bool MetBelltownRelicDealer;

	public bool BelltownRelicDealerGaveRelic;

	public bool BelltownRelicDealerCylinderConvo;

	public bool BelltownRelicDealerOutroConvo;

	public bool BelltownRelicDealerOutroConvoAllComplete;

	public bool MetBelltownRelicDealerAct3;

	public BelltownHouseStates BelltownHouseState;

	public bool BelltownHouseUnlocked;

	public BellhomePaintColours BelltownHouseColour;

	public bool BelltownHousePaintComplete;

	public bool BelltownFurnishingDesk;

	public bool BelltownFurnishingSpaAvailable;

	public bool BelltownFurnishingSpa;

	public bool BelltownFurnishingFairyLights;

	public bool BelltownFurnishingGramaphone;

	public CollectionGramaphone.PlayingInfo BelltownHousePlayingInfo;

	public bool CrawbellInstalled;

	public float CrawbellTimer;

	public int[] CrawbellCurrency;

	public int[] CrawbellCurrencyCaps;

	public bool CrawbellCrawsInside;

	public bool DeskPlacedRelicList;

	public bool DeskPlacedLibrarianList;

	public bool ConstructedMaterium;

	public bool ConstructedFarsight;

	public bool CollectedToolMetal;

	public bool CollectedCommonSpine;

	public CollectableMementosData MementosDeposited;

	public MateriumItemsData MateriumCollected;

	public bool CollectedMementoGrey;

	public bool CollectedMementoSprintmaster;

	public bool MetForgeDaughter;

	public int ForgeDaughterTalkState;

	public bool ForgeDaughterPurchaseDlg;

	public bool ForgeDaughterSpentToolMetal;

	public bool ForgeDaughterMentionedWebShot;

	public bool MetForgeDaughterAct3;

	public bool PurchasedForgeToolKit;

	public bool BallowInSauna;

	public bool BallowSeenInSauna;

	public bool BallowLeftSauna;

	public bool ForgeDaughterMentionedDivingBell;

	public bool BallowMovedToDivingBell;

	public bool BallowGivenKey;

	public bool BallowTalkedPostRepair;

	public bool BallowTalkedPostRepairGramaphone;

	public bool ForgeDaughterPostAbyssDlg;

	public bool ForgeDaughterWhiteFlowerDlg;

	public bool SeenDivingBellGoneAbyss;

	public bool openedGateCoral_14;

	public bool defeatedZapGuard1;

	public bool encounteredCoralDrillers;

	public bool defeatedCoralDrillers;

	public bool coralDrillerSoloReady;

	public bool activatedStepsUpperBellbench;

	public bool defeatedCoralBridgeGuard1;

	public bool coralBridgeGuard2Stationed;

	public bool defeatedCoralBridgeGuard2;

	public bool encounteredCoralKing;

	public bool defeatedCoralKing;

	public bool coralKingHeartAppeared;

	public bool metGatePilgrim;

	public bool gatePilgrimNoNeedolinConvo;

	public bool encounteredLastJudge;

	public bool defeatedLastJudge;

	public bool SeenLastJudgeGateOpen;

	public bool pinstressStoppedResting;

	public bool pinstressInsideSitting;

	public bool pinstressQuestReady;

	public bool PinstressPeakQuestOffered;

	public bool PinstressPeakBattleOffered;

	public bool PinstressPeakBattleAccepted;

	public bool SteelSentinelMet;

	public bool SteelSentinelOffered;

	public bool EncounteredSummonedSaviour;

	public SteelSoulQuestSpot.Spot[] SteelQuestSpots;

	public int GrowstoneState;

	public float GrowstoneTimer;

	public bool SeenGrindleShop;

	public bool grindleShopEnemyIntro;

	public bool purchasedGrindleSimpleKey;

	public bool purchasedGrindleMemoryLocket;

	public bool purchasedGrindleSpoolPiece;

	public bool purchasedGrindleToolKit;

	public bool metGrindleAct3;

	public bool encounteredCoralDrillerSolo;

	public bool defeatedCoralDrillerSolo;

	public bool coralDrillerSoloEnemiesReturned;

	public bool defeatedZapCoreEnemy;

	public bool wokeGreyWarrior;

	public bool defeatedGreyWarrior;

	public float greyWarriorDeathX;

	public bool visitedCoralBellshrine;

	public bool coral19_clearedOut;

	public bool encounteredPharloomEdge;

	public bool encounteredPharloomEdgeAct3;

	public bool weave01_oneWay;

	public bool weave05_oneWay;

	public bool wokeLiftWeaver;

	public bool visitedUpperSlab;

	public bool slab_03_rubbishCleared;

	public bool slab_cloak_battle_encountered;

	public bool slab_cloak_battle_completed;

	public bool slab_cloak_gate_reopened;

	public bool slab_05_gateOpen;

	public bool slab_07_gateOpen;

	public bool slab_17_openedGateRight;

	public bool slab_cell_quiet_oneWayWall;

	public bool slab_17_openedGateLeft;

	public bool slabCaptor_heardChallenge;

	public bool slabCaptor_heardChallengeRings;

	public bool encounteredFirstWeaver;

	public bool defeatedFirstWeaver;

	public int grindleSlabSequence;

	public bool slabPrisonerSingConvo;

	public bool slabPrisonerFlyConvo;

	public bool slabPrisonerRemeetConvo;

	public bool HasSlabKeyA;

	public bool HasSlabKeyB;

	public bool HasSlabKeyC;

	public bool defeatedBroodMother;

	public bool broodMotherEyeCollected;

	public bool tinyBroodMotherAppeared;

	public bool peak13_oneWay;

	public bool peak05b_oneWay;

	public bool peak05c_oneWay;

	public bool peak06_oneWay;

	public bool MetMaskMaker;

	public bool MetMaskMakerAct3;

	public bool MaskMakerTalkedRelationship;

	public bool MaskMakerTalkedPeak;

	public bool MaskMakerTalkedUnmaskedAct3;

	public bool MaskMakerTalkedUnmasked;

	public bool MaskMakerTalkedUnmasked1;

	public bool MaskMakerQueuedUnmasked2;

	public bool MaskMakerTalkedUnmasked2;

	public bool understoreLiftBroke;

	public bool brokeConfessional;

	public bool droppedFloorBreakerPlat;

	public bool rosaryThievesInUnder07;

	public bool under07_battleCompleted;

	public bool under07_heavyWorkerReturned;

	public bool openedShellwoodShortcut;

	public bool openedUnder_05;

	public bool openedUnder_19;

	public bool openedUnder_01b;

	public bool MetArchitect;

	public bool MetArchitectAct3;

	public bool PurchasedArchitectToolKit;

	public bool PurchasedArchitectKey;

	public bool ArchitectTalkedCrest;

	public bool ArchitectMentionedWebShot;

	public bool ArchitectMentionedCogHeart;

	public bool ArchitectMentionedMelody;

	public bool ArchitectMelodyReturnQueued;

	public bool ArchitectMelodyReturnSeen;

	public bool ArchitectMelodyGainSeen;

	public bool ArchitectWillLeave;

	public bool ArchitectLeft;

	public bool SeenArchitectLeft;

	public bool citadelWoken;

	public bool song05MarchGroupReady;

	public bool laceMeetCitadel;

	public bool song18Shortcut;

	public bool encounteredLibraryEntryBattle;

	public bool completedLibraryEntryBattle;

	public bool scholarAmbushReady;

	public bool libraryRoofShortcut;

	public bool scholarAcolytesReleased;

	public bool scholarAcolytesReleasedAct3;

	public bool seenScholarAcolytes;

	public bool scholarAcolytesInLibrary_02;

	public bool completedLibraryAcolyteBattle;

	public bool library_14_ambush;

	public bool completedGrandStageBattle;

	public bool encounteredTrobbio;

	public bool defeatedTrobbio;

	public bool trobbioCleanedUp;

	public bool encounteredTormentedTrobbio;

	public bool defeatedTormentedTrobbio;

	public bool tormentedTrobbioLurking;

	public bool libraryStatueWoken;

	public bool marionettesMet;

	public bool song_17_clearedOut;

	public bool song_27_opened;

	public bool marionettesBurned;

	public bool song_11_oneway;

	public bool citadel_encounteredFencers;

	public bool enteredHang_08;

	public bool LibrarianAskedForRelic;

	public bool GivenLibrarianRelic;

	public bool LibrarianMetAct3;

	public bool LibrarianMentionedMelody;

	public bool LibrarianAskedForMelody;

	public CollectionGramaphone.PlayingInfo LibrarianPlayingInfo;

	public bool LibrarianCollectionComplete;

	public bool encounteredCogworkDancers;

	public bool defeatedCogworkDancers;

	public bool cityMerchantSaved;

	public bool cityMerchantIntroduced;

	public bool cityMerchantEnclaveConvo;

	public bool cityMerchantRecentlySeenInEnclave;

	public bool cityMerchantInGrandForum;

	public bool cityMerchantInGrandForumSeen;

	public bool cityMerchantInGrandForumLeft;

	public bool cityMerchantInLibrary03;

	public bool cityMerchantInLibrary03Seen;

	public bool cityMerchantInLibrary03Left;

	public bool cityMerchantCanLeaveForBridge;

	public bool MetCityMerchantScavenge;

	public bool MetCityMerchantEnclave;

	public bool MetCityMerchantEnclaveAct3;

	public bool cityMerchantConvo1;

	public bool cityMerchantBridgeSaveRemeet;

	public bool MerchantEnclaveShellFragment;

	public bool MerchantEnclaveSpoolPiece;

	public bool MerchantEnclaveSocket;

	public bool MerchantEnclaveWardKey;

	public bool MerchantEnclaveSimpleKey;

	public bool MerchantEnclaveToolMetal;

	public bool encounteredLaceTower;

	public bool defeatedLaceTower;

	public float laceCorpseScaleX;

	public float laceCorpsePosX;

	public bool laceTowerDoorOpened;

	public bool laceCorpseAddedEffects;

	public bool MetGourmandServant;

	public bool GourmandServantOfferedQuest;

	public bool GourmandGivenStew;

	public bool GourmandGivenNectar;

	public bool GourmandGivenEgg;

	public bool GourmandGivenMeat;

	public bool GourmandGivenCoral;

	public bool GotGourmandReward;

	public bool MetGourmandServantAct3;

	public bool metCaretaker;

	public bool caretakerWardConvo;

	public bool caretakerMerchantConvo;

	public bool caretakerBeastConvo;

	public bool caretakerLaceConvo;

	public bool caretakerConvoLv1;

	public bool caretakerConvoLv2;

	public bool caretakerConvoLv3;

	public bool CaretakerSwampSoulConvo;

	public bool CaretakerSnareProgressConvo;

	public bool CaretakerOfferedSnareQuest;

	public bool enclaveMerchantSaved;

	public bool enclaveMerchantSeenInEnclave;

	public NPCEncounterState EnclaveStatePilgrimSmall;

	public NPCEncounterState EnclaveStateNPCShortHorned;

	public NPCEncounterState EnclaveStateNPCTall;

	public bool MetEnclaveScaredPilgrim;

	public NPCEncounterState EnclaveStateNPCStandard;

	public NPCEncounterState EnclaveState_songKnightFan;

	[DefaultValue(0)]
	public int enclaveLevel;

	public bool enclaveDonation2_Available;

	public bool enclaveAddition_PinRack;

	public bool enclaveAddition_CloakLine;

	public bool enclaveNPC_songKnightFan;

	public bool savedGrindleInCitadel;

	public bool grindleEnclaveConvo;

	public int grindleChestLocation;

	public bool grindleChestEncountered;

	public bool grindleInSong_08;

	public bool seenGrindleInSong_08;

	public bool collectedWardKey;

	public bool wardBossEncountered;

	public bool wardBossDefeated;

	public bool wardBossHatchOpened;

	public bool collectedWardBossKey;

	public bool wardWoken;

	public bool garmondAidForumBattle;

	public bool shakraAidForumBattle;

	public bool hang_10_oneWay;

	public bool bankOpened;

	public bool rosaryThievesInBank;

	public bool rosaryThievesLeftBank;

	public bool destroyedRosaryCannonMachine;

	public bool hang04Battle;

	public bool leftTheGrandForum;

	public bool grindleMetGrandForum;

	public bool opened_cog_06_door;

	public bool cog7_automaton_defeated;

	public bool cog7_gateOpened;

	public bool cog7_automatonRepairing;

	public bool cog7_automatonRepairingComplete;

	public bool cog7_automatonDestroyed;

	public bool wokeSongChevalier;

	public bool songChevalierActiveInSong_25;

	public bool songChevalierSeenInSong_25;

	public bool songChevalierActiveInSong_27;

	public bool songChevalierSeenInSong_27;

	public bool song_04_battleCompleted;

	public bool songChevalierActiveInSong_04;

	public bool songChevalierSeenInSong_04;

	public bool songChevalierActiveInSong_02;

	public bool songChevalierSeenInSong_02;

	public bool songChevalierActiveInSong_07;

	public bool songChevalierSeenInSong_07;

	public bool songChevalierActiveInSong_24;

	public bool songChevalierSeenInSong_24;

	public bool songChevalierActiveInHang_02;

	public bool songChevalierSeenInHang_02;

	public bool songChevalierEncounterCooldown;

	public int songChevalierEncounters;

	public bool songChevalierRestingMet;

	public bool songChevalierRestingMetAct3;

	public bool songChevalierQuestReady;

	public bool encounteredSongChevalierBoss;

	public bool defeatedSongChevalierBoss;

	public bool arborium_09_oneWay;

	public bool arborium_08_oneWay;

	public bool uncagedGiantFlea;

	public bool tamedGiantFlea;

	public bool encounteredSilk;

	public bool soulSnareReady;

	public bool caretakerSoulSnareConvo;

	public bool encounteredSurfaceEdge;

	public bool fullyEnteredVerdania;

	public bool summonedLakeOrbs;

	public bool encounteredWhiteCloverstagMid;

	public bool encounteredWhiteCloverstag;

	public bool defeatedWhiteCloverstag;

	public bool encounteredCloverDancers;

	public bool defeatedCloverDancers;

	public bool cloverDancerRegrowth;

	public bool memoryOrbs_Clover_02c_A;

	public bool memoryOrbs_Clover_03_B;

	public bool memoryOrbs_Clover_06_A;

	public bool memoryOrbs_Clover_11;

	public bool memoryOrbs_Clover_16_B;

	public bool memoryOrbs_Clover_16_C;

	public bool memoryOrbs_Clover_21;

	public ulong memoryOrbs_Clover_18_A;

	public ulong memoryOrbs_Clover_18_B;

	public ulong memoryOrbs_Clover_18_C;

	public ulong memoryOrbs_Clover_18_D;

	public ulong memoryOrbs_Clover_18_E;

	public ulong memoryOrbs_Clover_19;

	public const int CLOVER_MEMORY_ORBS_TOTAL = 17;

	public const int CLOVER_MEMORY_ORBS_TARGET = 12;

	public bool completedSuperJumpSequence;

	public bool completedAbyssAscent;

	public bool blackThreadWorld;

	public bool act3_wokeUp;

	public bool completedCog10_abyssBattle;

	public bool act3_enclaveWakeSceneCompleted;

	public bool AbyssBellSeenDocks;

	public bool AbyssBellSeenDocksRepaired;

	public bool SatAtBenchAfterAbyssEscape;

	public bool CollectedHeartFlower;

	public bool CollectedHeartCoral;

	public bool CollectedHeartHunter;

	public bool CollectedHeartClover;

	public bool ShamanRitualCursedConvo;

	public bool CompletedRedMemory;

	public bool LastDiveCursedConvo;

	public bool SnailShamansCrestConvo;

	public bool SnailShamansCloverHeartConvo;

	public bool EncounteredLostLace;

	public bool MetCaravanTroupeLeader;

	public bool SeenFleaCaravan;

	public bool FleaQuestOffered;

	public bool CaravanPilgrimAttackComplete;

	public CaravanTroupeLocations CaravanTroupeLocation;

	public bool MetCaravanTroupeLeaderGreymoor;

	public bool CaravanTroupeLeaderCanLeaveGreymoor;

	public bool MetCaravanTroupeLeaderGreymoorScared;

	public bool MetCaravanTroupeLeaderJudge;

	public bool CaravanTroupeLeaderCanLeaveJudge;

	public bool CaravanLechSaved;

	public bool CaravanLechReturnedToCaravan;

	public bool CaravanLechMet;

	public bool CaravanLechSpaAcceptState;

	public bool CaravanLechSpaAttacked;

	public bool CaravanLechWoundedSpoken;

	public bool CaravanLechAct3Convo;

	public bool CaravanHauntedBellhartConvo_TroupeLeader;

	public bool MetTroupeHunterWild;

	public bool TroupeHunterWildAct3Convo;

	public bool TroupeLeaderSpokenLech;

	public bool TroupeLeaderSpokenHunter;

	public bool SeenFleatopiaEmpty;

	public bool TroupeLeaderSpokenFleatopiaSearch;

	public bool FleaGamesCanStart;

	public bool FleaGamesStarted;

	public bool FleaGamesPinataHit;

	public bool FleaGamesEnded;

	public bool grishkinSethConvo;

	public bool fleaGames_juggling_played;

	public int fleaGames_juggling_highscore;

	public bool fleaGames_bouncing_played;

	public int fleaGames_bouncing_highscore;

	public bool fleaGames_dodging_played;

	public int fleaGames_dodging_highscore;

	public bool FleaGamesEndedContinuedPlaying;

	public bool FleaGamesSpiritScoreAdded;

	public bool FleaGamesMementoGiven;

	public List<int> FleasCollectedTargetOrder;

	public bool SavedFlea_Bone_06;

	public bool SavedFlea_Dock_16;

	public bool SavedFlea_Bone_East_05;

	public bool SavedFlea_Bone_East_17b;

	public bool SavedFlea_Ant_03;

	public bool SavedFlea_Greymoor_15b;

	public bool SavedFlea_Greymoor_06;

	public bool SavedFlea_Shellwood_03;

	public bool SavedFlea_Bone_East_10_Church;

	public bool SavedFlea_Coral_35;

	public bool SavedFlea_Dust_12;

	public bool SavedFlea_Dust_09;

	public bool SavedFlea_Belltown_04;

	public bool SavedFlea_Crawl_06;

	public bool SavedFlea_Slab_Cell;

	public bool SavedFlea_Shadow_28;

	public bool SavedFlea_Dock_03d;

	public bool SavedFlea_Under_23;

	public bool SavedFlea_Shadow_10;

	public bool SavedFlea_Song_14;

	public bool SavedFlea_Coral_24;

	public bool SavedFlea_Peak_05c;

	public bool SavedFlea_Library_09;

	public bool SavedFlea_Song_11;

	public bool SavedFlea_Library_01;

	public bool SavedFlea_Under_21;

	public bool SavedFlea_Slab_06;

	public bool MetSeamstress;

	public bool SeamstressOfferedQuest;

	public int SeamstressIdleTalkState;

	public bool SeamstressCitadelConvo;

	public bool SeamstressPinstressConvo;

	public bool SeamstressAct3Convo;

	public bool SeamstressBadgeConvo;

	public bool FreedCaravanSpider;

	public bool SeenCaravanSpider;

	public bool MetCaravanSpider;

	public string CaravanSpiderTargetScene;

	public float CaravanSpiderTravelDirection;

	public bool OpenedCoralCaravanSpider;

	public bool MetCaravanSpiderCoral;

	public bool CaravanSpiderPaidExtraBellhart;

	[DefaultValue("Dust_05")]
	public string MazeEntranceScene;

	[DefaultValue("left1")]
	public string MazeEntranceDoor;

	[DefaultValue("Dust_05")]
	public string MazeEntranceInitialScene;

	[DefaultValue("left1")]
	public string MazeEntranceInitialDoor;

	public string PreviousMazeTargetDoor;

	public string PreviousMazeScene;

	public string PreviousMazeDoor;

	public int CorrectMazeDoorsEntered;

	public int IncorrectMazeDoorsEntered;

	public bool EnteredMazeRestScene;

	public bool DidEnterPreviousMazeDoor;

	public bool WasInSceneRace;

	public int SprintMasterCurrentRace;

	public bool SprintMasterExtraRaceAvailable;

	public bool SprintMasterExtraRaceDlg;

	public bool SprintMasterExtraRaceWon;

	public bool CurseKilledFlyBoneEast;

	public bool CurseKilledFlyGreymoor;

	public bool CurseKilledFlyShellwood;

	public bool CurseKilledFlySwamp;

	public bool act2Started;

	public float completionPercentage;

	public int mapKeyPref;

	public bool promisedFirstWish;

	[NonSerialized]
	public bool disablePause;

	[NonSerialized]
	public bool disableSaveQuit;

	[NonSerialized]
	public bool disableInventory;

	[NonSerialized]
	public bool isInventoryOpen;

	[NonSerialized]
	public bool disableSilkAbilities;

	public bool betaEnd;

	public bool newDatTraitorLord;

	public string bossReturnEntryGate;

	public int bossStatueTargetLevel;

	public string currentBossStatueCompletionKey;

	public BossSequenceController.BossSequenceData currentBossSequence;

	public bool bossRushMode;

	public List<string> unlockedBossScenes;

	public bool unlockedNewBossStatue;

	public bool hasGodfinder;

	public bool queuedGodfinderIcon;

	public bool InvPaneHasNew;

	public bool ToolPaneHasNew;

	public bool QuestPaneHasNew;

	public bool JournalPaneHasNew;

	[DefaultValue("Hunter")]
	public string CurrentCrestID;

	public string PreviousCrestID;

	public ToolCrestsData ToolEquips;

	[NonSerialized]
	public bool IsCurrentCrestTemp;

	public bool UnlockedExtraBlueSlot;

	public bool UnlockedExtraYellowSlot;

	public FloatingCrestSlotsData ExtraToolEquips;

	public ToolItemsData Tools;

	[NonSerialized]
	private Dictionary<string, int> toolAmountsOverride;

	public ToolItemLiquidsData ToolLiquids;

	public int ToolPouchUpgrades;

	public int ToolKitUpgrades;

	public bool LightningToolToggle;

	public bool SeenToolGetPrompt;

	public bool SeenToolWeaponGetPrompt;

	public bool SeenToolEquipPrompt;

	public bool SeenToolUsePrompt;

	public QuestCompletionData QuestCompletionData;

	public QuestRumourData QuestRumourData;

	public int ShellShards;

	public bool HasSeenGeo;

	public bool HasSeenGeoMid;

	public bool HasSeenGeoBig;

	public bool HasSeenShellShards;

	public bool HasSeenRation;

	public int TempGeoStore;

	public int TempShellShardStore;

	public CollectableItemsData Collectables;

	public CollectableRelicsData Relics;

	public bool UnlockedFastTravel;

	public FastTravelLocations FastTravelNPCLocation;

	public bool UnlockedFastTravelTeleport;

	[NonSerialized]
	public bool travelling;

	[NonSerialized]
	public string nextScene;

	[NonSerialized]
	public bool IsTeleporting;

	public bool UnlockedDocksStation;

	public bool UnlockedBoneforestEastStation;

	public bool UnlockedGreymoorStation;

	public bool UnlockedBelltownStation;

	public bool UnlockedCoralTowerStation;

	public bool UnlockedCityStation;

	public bool UnlockedPeakStation;

	public bool UnlockedShellwoodStation;

	public bool UnlockedShadowStation;

	public bool UnlockedAqueductStation;

	public bool bellCentipedeAppeared;

	public bool UnlockedSongTube;

	public bool UnlockedUnderTube;

	public bool UnlockedCityBellwayTube;

	public bool UnlockedHangTube;

	public bool UnlockedEnclaveTube;

	public bool UnlockedArboriumTube;

	[NonSerialized]
	public int MaggotCharmHits;

	public bool MushroomQuestFound1;

	public bool MushroomQuestFound2;

	public bool MushroomQuestFound3;

	public bool MushroomQuestFound4;

	public bool MushroomQuestFound5;

	public bool MushroomQuestFound6;

	public bool MushroomQuestFound7;

	public List<PlayerStory.EventInfo> StoryEvents;

	private static PlayerData _instance;

	private static BoolFieldAccessOptimizer<PlayerData> boolFieldAccessOptimizer = new BoolFieldAccessOptimizer<PlayerData>();

	private static FieldAccessOptimizer<PlayerData, int> intFieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, int>();

	private static FieldAccessOptimizer<PlayerData, float> floatFieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, float>();

	private static FieldAccessOptimizer<PlayerData, string> stringFieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, string>();

	private static FieldAccessOptimizer<PlayerData, Vector3> vector3FieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, Vector3>();

	public bool IsDemoMode => DemoHelper.IsDemoMode;

	public bool IsExhibitionMode => DemoHelper.IsExhibitionMode;

	public bool IsHornetStrengthRegained
	{
		get
		{
			if (maxHealthBase <= 5 && silkMax <= 9)
			{
				return silkRegenMax > 0;
			}
			return true;
		}
	}

	public int nailDamage => nailUpgrades switch
	{
		0 => 5, 
		1 => 9, 
		2 => 13, 
		3 => 17, 
		_ => 21, 
	};

	private MapBoolList MapBools
	{
		get
		{
			if (mapBoolList == null)
			{
				mapBoolList = new MapBoolList(this);
			}
			return mapBoolList;
		}
	}

	public bool HasAnyMap
	{
		get
		{
			if (mapAllRooms)
			{
				return true;
			}
			return MapBools.HasAnyMap;
		}
	}

	public bool HasAllMaps
	{
		get
		{
			if (mapAllRooms)
			{
				return true;
			}
			return MapBools.HasAllMaps;
		}
	}

	public int MapCount
	{
		get
		{
			if (mapAllRooms)
			{
				return 28;
			}
			return mapBoolList.HasCount;
		}
	}

	public bool CanUpdateMap
	{
		get
		{
			if (hasQuill)
			{
				return HasAnyMap;
			}
			return false;
		}
	}

	public bool IsWildsWideMapFull => scenesVisited.Contains("Bone_East_24");

	public bool HasAnyFleaPin
	{
		get
		{
			if (!hasPinFleaMarrowlands && !hasPinFleaMidlands && !hasPinFleaBlastedlands && !hasPinFleaCitadel && !hasPinFleaPeaklands)
			{
				return hasPinFleaMucklands;
			}
			return true;
		}
	}

	public bool IsFleaPinMapKeyVisible
	{
		get
		{
			if (!HasAnyFleaPin)
			{
				return false;
			}
			if (!CaravanLechSaved || !tamedGiantFlea)
			{
				return true;
			}
			CacheSavedFleas();
			FieldInfo[] savedFleaFields = _savedFleaFields;
			for (int i = 0; i < savedFleaFields.Length; i++)
			{
				if (!(bool)savedFleaFields[i].GetValue(this))
				{
					return true;
				}
			}
			return false;
		}
	}

	public int SavedFleasCount
	{
		get
		{
			CacheSavedFleas();
			return _savedFleaFields.Count((FieldInfo fieldInfo) => (bool)fieldInfo.GetValue(this));
		}
	}

	public bool HasAnyPin
	{
		get
		{
			if (!hasPinBench && !hasPinStag && !hasPinShop && !hasPinTube)
			{
				return IsFleaPinMapKeyVisible;
			}
			return true;
		}
	}

	public bool VampireGnatBossInAltLoc
	{
		get
		{
			if (allowVampireGnatInAltLoc && (CaravanTroupeLocation == CaravanTroupeLocations.Greymoor || visitedBellhart || defeatedLastJudge))
			{
				return visitedCitadel;
			}
			return false;
		}
	}

	public bool CaravanInGreymoor => CaravanTroupeLocation == CaravanTroupeLocations.Greymoor;

	public bool HasLifebloodSyringeGland => Collectables.GetData("Plasmium Gland").Amount > 0;

	public bool GourmandQuestAccepted => QuestManager.GetQuest("Great Gourmand").IsAccepted;

	public bool SlaveDeliveryQuestCompleted
	{
		get
		{
			if (permadeathMode != PermadeathModes.On)
			{
				return QuestCompletionData.GetData("Courier Delivery Dustpens Slave").IsCompleted;
			}
			return true;
		}
	}

	public bool BelltownHouseVisited => scenesVisited.Contains("Belltown_Room_Spare");

	public bool CrawbellHasSomething
	{
		get
		{
			if (CrawbellCurrency == null)
			{
				return false;
			}
			int[] crawbellCurrency = CrawbellCurrency;
			for (int i = 0; i < crawbellCurrency.Length; i++)
			{
				if (crawbellCurrency[i] > 0)
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool HasAnyMemento
	{
		get
		{
			foreach (CollectableItemMemento memento in Gameplay.MementoList)
			{
				if ((bool)memento && memento.CollectedAmount > 0)
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool CloakFlySmell => cloakOdour_slabFly > 0;

	public bool IsAnyRelicsDeposited => Relics.IsAnyMatching((CollectableRelicsData.Data relic) => relic.IsDeposited);

	public bool WillLoadWardBoss
	{
		get
		{
			if (!wardBossDefeated)
			{
				if (!collectedWardBossKey)
				{
					return wardBossHatchOpened;
				}
				return true;
			}
			return false;
		}
	}

	public int CollectedCloverMemoryOrbs => Convert.ToInt32(memoryOrbs_Clover_02c_A) + Convert.ToInt32(memoryOrbs_Clover_03_B) + Convert.ToInt32(memoryOrbs_Clover_06_A) + Convert.ToInt32(memoryOrbs_Clover_11) + Convert.ToInt32(memoryOrbs_Clover_16_B) + Convert.ToInt32(memoryOrbs_Clover_16_C) + Convert.ToInt32(memoryOrbs_Clover_21) + memoryOrbs_Clover_18_A.CountSetBits() + memoryOrbs_Clover_18_B.CountSetBits() + memoryOrbs_Clover_18_C.CountSetBits() + memoryOrbs_Clover_18_D.CountSetBits() + memoryOrbs_Clover_18_E.CountSetBits() + memoryOrbs_Clover_19.CountSetBits();

	public bool CloverMemoryOrbsCollectedAll => CollectedCloverMemoryOrbs >= 17;

	public bool CloverMemoryOrbsCollectedTarget => CollectedCloverMemoryOrbs >= 12;

	public bool IsAct3IntroQueued
	{
		get
		{
			if (blackThreadWorld)
			{
				return !act3_wokeUp;
			}
			return false;
		}
	}

	public bool HasWhiteFlower => Collectables.GetData("White Flower").Amount > 0;

	public bool FleaGamesIsJugglingChampion => fleaGames_juggling_highscore > 30;

	public bool FleaGamesIsJugglingSethChampion => fleaGames_juggling_highscore > 55;

	public bool FleaGamesIsBouncingChampion => fleaGames_bouncing_highscore > 42;

	public bool FleaGamesIsBouncingSethChampion => fleaGames_bouncing_highscore > 68;

	public bool FleaGamesIsDodgingChampion => fleaGames_dodging_highscore > 65;

	public bool FleaGamesIsDodgingSethChampion => fleaGames_dodging_highscore > 90;

	public bool FleaGamesOutroReady
	{
		get
		{
			if (FleaGamesIsJugglingChampion && FleaGamesIsBouncingChampion)
			{
				return FleaGamesIsDodgingChampion;
			}
			return false;
		}
	}

	public bool FleaGamesBestedSeth
	{
		get
		{
			if (FleaGamesIsJugglingSethChampion && FleaGamesIsBouncingSethChampion)
			{
				return FleaGamesIsDodgingSethChampion;
			}
			return false;
		}
	}

	public bool BellCentipedeWaiting
	{
		get
		{
			if (blackThreadWorld)
			{
				return !UnlockedFastTravelTeleport;
			}
			return false;
		}
	}

	public bool BellCentipedeLocked
	{
		get
		{
			if (bellCentipedeAppeared)
			{
				return !UnlockedFastTravelTeleport;
			}
			return false;
		}
	}

	public bool UnlockedAnyTube
	{
		get
		{
			if (!UnlockedArboriumTube && !UnlockedEnclaveTube && !UnlockedHangTube && !UnlockedSongTube && !UnlockedUnderTube)
			{
				return UnlockedCityBellwayTube;
			}
			return true;
		}
	}

	public int CurrentMaxHealth
	{
		get
		{
			if (BossSequenceController.BoundShell)
			{
				return Mathf.Min(maxHealth, BossSequenceController.BoundMaxHealth);
			}
			return maxHealth;
		}
	}

	public int CurrentSilkMax
	{
		get
		{
			int num = CurrentSilkMaxBasic;
			ToolItem spoolExtenderTool = Gameplay.SpoolExtenderTool;
			if ((bool)spoolExtenderTool && spoolExtenderTool.IsEquipped)
			{
				num += Gameplay.SpoolExtenderSilk;
			}
			return num;
		}
	}

	public int CurrentSilkMaxBasic
	{
		get
		{
			if (UnlockSilkFinalCutscene)
			{
				return silk;
			}
			if (IsAnyCursed)
			{
				return 3;
			}
			if (IsSilkSpoolBroken)
			{
				return 9;
			}
			return silkMax;
		}
	}

	public int SilkSkillCost
	{
		get
		{
			if (!Gameplay.FleaCharmTool.IsEquippedHud || health < CurrentMaxHealth)
			{
				return 4;
			}
			return 3;
		}
	}

	public bool IsAnyCursed => CurrentCrestID == Gameplay.CursedCrest.name;

	public int CurrentSilkRegenMax
	{
		get
		{
			if (!Gameplay.WhiteRingTool.IsEquipped)
			{
				return silkRegenMax;
			}
			return silkRegenMax + Gameplay.WhiteRingSilkRegenIncrease;
		}
	}

	public bool JournalIsCompleted => EnemyJournalManager.IsAllRequiredComplete();

	public int JournalCompletedCount => EnemyJournalManager.GetCompletedEnemiesCount();

	public bool MushroomQuestCompleted => QuestCompletionData.GetData("Mr Mushroom").IsCompleted;

	public static PlayerData instance
	{
		get
		{
			if (_instance != null)
			{
				return _instance;
			}
			return CreateNewSingleton(addEditorOverrides: true);
		}
		set
		{
			_instance = value;
		}
	}

	public static bool HasInstance => _instance != null;

	private void CacheSavedFleas()
	{
		if (_savedFleaFields == null)
		{
			_savedFleaFields = (from fieldInfo in GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
				where fieldInfo.FieldType == typeof(bool) && fieldInfo.Name.StartsWith("SavedFlea_")
				select fieldInfo).ToArray();
		}
	}

	public PlayerData()
	{
		SetupNewPlayerData(addEditorOverrides: false);
	}

	public static PlayerData CreateNewSingleton(bool addEditorOverrides)
	{
		_instance = SaveDataUtility.DeserializeSaveData<PlayerData>("{}");
		_instance.SetupNewPlayerData(addEditorOverrides);
		_instance.SetupExistingPlayerData();
		return _instance;
	}

	[OnDeserializing]
	private void OnDeserialized(StreamingContext context)
	{
		SetupNewPlayerData(addEditorOverrides: false);
	}

	public static void ClearOptimisers()
	{
		boolFieldAccessOptimizer = new BoolFieldAccessOptimizer<PlayerData>();
		intFieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, int>();
		floatFieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, float>();
		stringFieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, string>();
		vector3FieldAccessOptimiser = new FieldAccessOptimizer<PlayerData, Vector3>();
	}

	public void SetBool(string boolName, bool value)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			boolFieldAccessOptimizer.SetField(this, boolName, value);
			return;
		}
		FieldInfo field = GetType().GetField(boolName);
		if (field != null)
		{
			field.SetValue(instance, value);
		}
	}

	public void SetInt(string intName, int value)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			intFieldAccessOptimiser.SetField(this, intName, value);
			return;
		}
		FieldInfo field = GetType().GetField(intName);
		if (field != null)
		{
			field.SetValue(instance, value);
		}
	}

	public void IncrementInt(string intName)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			IntAdd(intName, 1);
			return;
		}
		FieldInfo field = GetType().GetField(intName);
		if (field != null)
		{
			int num = (int)field.GetValue(instance);
			field.SetValue(instance, num + 1);
		}
	}

	public void IntAdd(string intName, int amount)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			int field = intFieldAccessOptimiser.GetField(this, intName);
			intFieldAccessOptimiser.SetField(this, intName, field + amount);
			return;
		}
		FieldInfo field2 = GetType().GetField(intName);
		if (field2 != null)
		{
			int num = (int)field2.GetValue(instance);
			field2.SetValue(instance, num + amount);
		}
	}

	public void SetFloat(string floatName, float value)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			floatFieldAccessOptimiser.SetField(this, floatName, value);
			return;
		}
		FieldInfo field = GetType().GetField(floatName);
		if (field != null)
		{
			field.SetValue(instance, value);
		}
	}

	public void DecrementInt(string intName)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			IntAdd(intName, -1);
			return;
		}
		FieldInfo field = GetType().GetField(intName);
		if (!(field == null))
		{
			int num = (int)field.GetValue(instance);
			field.SetValue(instance, num - 1);
		}
	}

	public bool GetBool(string boolName)
	{
		if (string.IsNullOrEmpty(boolName))
		{
			return false;
		}
		if (CheatManager.UseFieldAccessOptimisers)
		{
			return boolFieldAccessOptimizer.GetField(this, boolName);
		}
		FieldInfo field = GetType().GetField(boolName);
		if (field != null)
		{
			return (bool)field.GetValue(instance);
		}
		return false;
	}

	public int GetInt(string intName)
	{
		if (string.IsNullOrEmpty(intName))
		{
			return -9999;
		}
		if (CheatManager.UseFieldAccessOptimisers)
		{
			return intFieldAccessOptimiser.GetField(this, intName);
		}
		FieldInfo field = GetType().GetField(intName);
		if (field != null)
		{
			return (int)field.GetValue(instance);
		}
		return -9999;
	}

	public float GetFloat(string floatName)
	{
		if (string.IsNullOrEmpty(floatName))
		{
			return -9999f;
		}
		if (CheatManager.UseFieldAccessOptimisers)
		{
			return floatFieldAccessOptimiser.GetField(this, floatName);
		}
		FieldInfo field = GetType().GetField(floatName);
		if (field != null)
		{
			return (float)field.GetValue(instance);
		}
		return -9999f;
	}

	public string GetString(string stringName)
	{
		if (string.IsNullOrEmpty(stringName))
		{
			return " ";
		}
		if (CheatManager.UseFieldAccessOptimisers)
		{
			return stringFieldAccessOptimiser.GetField(this, stringName);
		}
		FieldInfo field = GetType().GetField(stringName);
		if (field != null)
		{
			return (string)field.GetValue(instance);
		}
		return " ";
	}

	public void SetString(string stringName, string value)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			stringFieldAccessOptimiser.SetField(this, stringName, value);
			return;
		}
		FieldInfo field = GetType().GetField(stringName);
		if (field != null)
		{
			field.SetValue(instance, value);
		}
	}

	public void SetVector3(string vectorName, Vector3 value)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			vector3FieldAccessOptimiser.SetField(this, vectorName, value);
			return;
		}
		FieldInfo field = GetType().GetField(vectorName);
		if (field != null)
		{
			field.SetValue(instance, value);
		}
	}

	public Vector3 GetVector3(string vectorName)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			return vector3FieldAccessOptimiser.GetField(this, vectorName);
		}
		FieldInfo field = GetType().GetField(vectorName);
		if (field != null)
		{
			return (Vector3)field.GetValue(instance);
		}
		return Vector3.zero;
	}

	public int GetNextMossberryValue()
	{
		return mossBerryValueList[druidMossBerriesSold];
	}

	public int GetNextSilkGrubValue()
	{
		return GrubFarmerMimicValueList[GrubFarmerSilkGrubsSold % GrubFarmerMimicValueList.Length];
	}

	public void CaptureToolAmountsOverride()
	{
		toolAmountsOverride = new Dictionary<string, int>();
		foreach (KeyValuePair<string, ToolItemsData.Data> item in Tools.Enumerate())
		{
			toolAmountsOverride[item.Key] = item.Value.AmountLeft;
		}
	}

	public void ClearToolAmountsOverride()
	{
		if (toolAmountsOverride != null)
		{
			toolAmountsOverride = null;
			ToolItemManager.SendEquippedChangedEvent(force: true);
		}
	}

	public ToolItemsData.Data GetToolData(string toolName)
	{
		ToolItemsData.Data data = Tools.GetData(toolName);
		if (toolAmountsOverride != null && toolAmountsOverride.TryGetValue(toolName, out var value))
		{
			data.AmountLeft = value;
		}
		return data;
	}

	public void SetToolData(string toolName, ToolItemsData.Data data)
	{
		if (toolAmountsOverride != null)
		{
			ToolItemsData.Data data2 = Tools.GetData(toolName);
			toolAmountsOverride[toolName] = data.AmountLeft;
			data.AmountLeft = data2.AmountLeft;
		}
		Tools.SetData(toolName, data);
	}

	public void AddHealth(int amount)
	{
		if (health + amount >= maxHealth)
		{
			health = maxHealth;
		}
		else
		{
			health += amount;
		}
		if (health >= CurrentMaxHealth)
		{
			health = maxHealth;
		}
	}

	public void TakeHealth(int amount, bool hasBlueHealth, bool allowFracturedMaskBreak)
	{
		if (amount > 0 && health == maxHealth && health != CurrentMaxHealth)
		{
			health = CurrentMaxHealth;
		}
		damagedBlue = hasBlueHealth;
		if (!damagedBlue)
		{
			damagedPurple = false;
		}
		if (healthBlue > 0)
		{
			int num = amount - healthBlue;
			damagedBlue = true;
			damagedPurple = false;
			if (damagedBlue)
			{
				EventRegister.SendEvent("PURPLE HEALTH CHECK");
			}
			healthBlue -= amount;
			if (healthBlue < 0)
			{
				healthBlue = 0;
			}
			if (num > 0)
			{
				TakeHealth(num, hasBlueHealth: true, allowFracturedMaskBreak);
			}
			return;
		}
		int num2 = health - amount;
		ToolItem fracturedMaskTool = Gameplay.FracturedMaskTool;
		if (num2 <= 0 && (bool)fracturedMaskTool && fracturedMaskTool.IsEquipped && fracturedMaskTool.SavedData.AmountLeft > 0)
		{
			if (allowFracturedMaskBreak)
			{
				ToolItemsData.Data savedData = fracturedMaskTool.SavedData;
				savedData.AmountLeft = 0;
				fracturedMaskTool.SavedData = savedData;
			}
			amount = health - 1;
		}
		if (health - amount <= 0)
		{
			health = ((CheatManager.Invincibility == CheatManager.InvincibilityStates.PreventDeath) ? 1 : 0);
		}
		else
		{
			health -= amount;
		}
	}

	public void MaxHealth()
	{
		prevHealth = health;
		health = CurrentMaxHealth;
	}

	public void ActivateTestingCheats()
	{
		AddGeo(50000);
	}

	public void GetAllPowerups()
	{
		hasDash = true;
		hasBrolly = true;
		hasWalljump = true;
		hasDoubleJump = true;
	}

	public void AddToMaxHealth(int amount)
	{
		maxHealthBase += amount;
		maxHealth += amount;
		prevHealth = health;
		health = maxHealth;
	}

	public void AddGeo(int amount)
	{
		geo += amount;
		int currencyCap = Gameplay.GetCurrencyCap(CurrencyType.Money);
		if (geo > currencyCap)
		{
			geo = currencyCap;
		}
	}

	public void TakeGeo(int amount)
	{
		geo -= amount;
		if (geo < 0)
		{
			geo = 0;
		}
	}

	public void AddShards(int amount)
	{
		ShellShards += amount;
		int currencyCap = Gameplay.GetCurrencyCap(CurrencyType.Shard);
		if (ShellShards > currencyCap)
		{
			ShellShards = currencyCap;
		}
	}

	public void TakeShards(int amount)
	{
		ShellShards -= amount;
	}

	public bool WouldDie(int damage)
	{
		if (health - damage <= 0)
		{
			return true;
		}
		return false;
	}

	public bool AddSilk(int amount)
	{
		int num = silk;
		silk += amount;
		int currentSilkMax = CurrentSilkMax;
		if (silk >= currentSilkMax)
		{
			silkParts = 0;
			silk = currentSilkMax;
		}
		return silk > num;
	}

	public void TakeSilk(int amount)
	{
		silk = Math.Max(silk - amount, 0);
	}

	public void ReduceOdours(int amount)
	{
		if (cloakOdour_slabFly > 0)
		{
			cloakOdour_slabFly -= amount;
			if (cloakOdour_slabFly < 0)
			{
				cloakOdour_slabFly = 0;
			}
		}
	}

	public void EquipCharm(int charmNum)
	{
	}

	public void UnequipCharm(int charmNum)
	{
	}

	public void CalculateNotchesUsed()
	{
	}

	public void SetBenchRespawn(RespawnMarker spawnMarker, string sceneName, int spawnType)
	{
		respawnMarkerName = spawnMarker.name;
		respawnScene = sceneName;
		respawnType = spawnType;
		if (spawnMarker.overrideMapZone.IsEnabled && spawnMarker.overrideMapZone.Value != MapZone.NONE)
		{
			GameManager.instance.SetOverrideMapZoneAsRespawn(spawnMarker.overrideMapZone.Value);
		}
		else
		{
			GameManager.instance.SetCurrentMapZoneAsRespawn();
		}
	}

	public void SetBenchRespawn(string spawnMarker, string sceneName, bool facingRight)
	{
		respawnMarkerName = spawnMarker;
		respawnScene = sceneName;
		GameManager.instance.SetCurrentMapZoneAsRespawn();
	}

	public void SetBenchRespawn(string spawnMarker, string sceneName, int spawnType, bool facingRight)
	{
		respawnMarkerName = spawnMarker;
		respawnScene = sceneName;
		respawnType = spawnType;
		GameManager.instance.SetCurrentMapZoneAsRespawn();
	}

	public void SetHazardRespawn(HazardRespawnMarker location)
	{
		hazardRespawnLocation = location.transform.position;
		hazardRespawnFacing = location.RespawnFacingDirection;
	}

	public void SetHazardRespawn(Vector3 position, bool facingRight)
	{
		hazardRespawnLocation = position;
		hazardRespawnFacing = ((!facingRight) ? HazardRespawnMarker.FacingDirection.Left : HazardRespawnMarker.FacingDirection.Right);
	}

	public void MapperLeaveAll()
	{
		MapperLeftBellhart = true;
		MapperLeftBoneForest = true;
		MapperLeftBonetown = true;
		MapperLeftCoralCaverns = true;
		MapperLeftCrawl = true;
		MapperLeftDocks = true;
		MapperLeftDustpens = true;
		MapperLeftGreymoor = true;
		MapperLeftHuntersNest = true;
		MapperLeftJudgeSteps = true;
		MapperLeftPeak = true;
		MapperLeftShadow = true;
		MapperLeftShellwood = true;
		MapperLeftWilds = true;
	}

	public void CountGameCompletion()
	{
		completionPercentage = 0f;
		completionPercentage += ToolItemManager.GetCount(ToolItemManager.GetUnlockedTools(), null);
		completionPercentage += ToolItemManager.GetUnlockedCrestsCount() - 1;
		completionPercentage += nailUpgrades;
		completionPercentage += ToolKitUpgrades;
		completionPercentage += ToolPouchUpgrades;
		completionPercentage += silkRegenMax;
		if (hasNeedolin)
		{
			completionPercentage += 1f;
		}
		if (hasDash)
		{
			completionPercentage += 1f;
		}
		if (hasWalljump)
		{
			completionPercentage += 1f;
		}
		if (hasHarpoonDash)
		{
			completionPercentage += 1f;
		}
		if (hasSuperJump)
		{
			completionPercentage += 1f;
		}
		completionPercentage += maxHealthBase - 5;
		completionPercentage += silkMax - 9;
		if (hasChargeSlash)
		{
			completionPercentage += 1f;
		}
		if (HasBoundCrestUpgrader)
		{
			completionPercentage += 1f;
		}
		if (HasWhiteFlower)
		{
			completionPercentage += 1f;
		}
	}

	private void SetupNewPlayerData(bool addEditorOverrides)
	{
		ResetNonSerializableFields();
		scenesVisited = new HashSet<string>();
		scenesMapped = new HashSet<string>();
		scenesEncounteredBench = new HashSet<string>();
		scenesEncounteredCocoon = new HashSet<string>();
		ToolEquips = new ToolCrestsData();
		ToolEquips.SetData("Hunter", new ToolCrestsData.Data
		{
			IsUnlocked = true
		});
		ExtraToolEquips = new FloatingCrestSlotsData();
		Tools = new ToolItemsData();
		ToolLiquids = new ToolItemLiquidsData();
		EnemyJournalKillData = new EnemyJournalKillData();
		QuestCompletionData = new QuestCompletionData();
		QuestRumourData = new QuestRumourData();
		Collectables = new CollectableItemsData();
		Relics = new CollectableRelicsData();
		MementosDeposited = new CollectableMementosData();
		MateriumCollected = new MateriumItemsData();
		mossBerryValueList = Array.Empty<int>();
		GrubFarmerMimicValueList = Array.Empty<int>();
	}

	public void SetupExistingPlayerData()
	{
		if (mossBerryValueList == null || mossBerryValueList.Length == 0)
		{
			mossBerryValueList = new int[3] { 1, 2, 3 };
			mossBerryValueList.Shuffle();
		}
		if (GrubFarmerMimicValueList == null || GrubFarmerMimicValueList.Length == 0)
		{
			GrubFarmerMimicValueList = new int[3] { 1, 2, 3 };
			GrubFarmerMimicValueList.Shuffle();
		}
		SteelSoulQuestSpot.Spot[] steelQuestSpots = SteelQuestSpots;
		if (steelQuestSpots == null || steelQuestSpots.Length != 3)
		{
			SteelQuestSpots = new SteelSoulQuestSpot.Spot[3];
			List<string> list = new List<string> { "Shellwood_26", "Bone_East_14", "Aspid_01" };
			List<string> list2 = new List<string> { "Hang_08", "Coral_28", "Aqueduct_05" };
			SteelQuestSpots[0] = new SteelSoulQuestSpot.Spot
			{
				SceneName = list.GetAndRemoveRandomElement()
			};
			SteelQuestSpots[1] = new SteelSoulQuestSpot.Spot
			{
				SceneName = list2.GetAndRemoveRandomElement()
			};
			List<string> list3 = new List<string>(list.Count + list2.Count);
			list3.AddRange(list);
			list3.AddRange(list2);
			SteelQuestSpots[2] = new SteelSoulQuestSpot.Spot
			{
				SceneName = list3.GetRandomElement()
			};
		}
	}

	public void ResetNonSerializableFields()
	{
		tempRespawnScene = null;
		tempRespawnMarker = null;
		tempRespawnType = -1;
	}

	public void ResetTempRespawn()
	{
		tempRespawnType = -1;
		tempRespawnMarker = null;
		tempRespawnScene = null;
	}

	public void ResetCutsceneBools()
	{
		disablePause = false;
		disableInventory = false;
		disableSaveQuit = false;
	}

	public void AddGGPlayerDataOverrides()
	{
	}

	public override void OnUpdatedVariable(string variableName)
	{
		LastSetFieldName = variableName;
	}

	public static string GetDateString()
	{
		return DateTime.Now.ToString("yyyy/MM/dd");
	}

	public void OnBeforeSave()
	{
		GameManager gameManager = GameManager.instance;
		bool flag = false;
		if (!slab_cloak_battle_completed && gameManager.GetSceneNameString() == "Slab_16" && CurrentCrestID != "Cloakless" && PreviousCrestID == "Cloakless")
		{
			string currentCrestID = CurrentCrestID;
			CurrentCrestID = PreviousCrestID;
			PreviousCrestID = currentCrestID;
			flag = true;
		}
		else if (IsCurrentCrestTemp)
		{
			_ = CurrentCrestID;
			CurrentCrestID = PreviousCrestID;
			PreviousCrestID = string.Empty;
			IsCurrentCrestTemp = false;
			flag = true;
		}
		if (flag)
		{
			ToolItemManager.SendEquippedChangedEvent(force: true);
		}
		Platform.Current.UpdatePlayTime(playTime);
	}

	public void UpdateDate()
	{
		date = GetDateString();
	}

	private void AddEditorOverrides()
	{
	}
}
