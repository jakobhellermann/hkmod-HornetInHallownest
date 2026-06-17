using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GlobalEnums;
using GlobalSettings;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
using TeamCherry.SharedUtils;
using UnityEngine;

namespace Silksong;

public class HeroController : MonoBehaviour, ITagDamageTakerOwner
{
	[Serializable]
	public class ConfigGroup
	{
		public HeroControllerConfig Config;

		public GameObject ActiveRoot;

		[Space]
		public GameObject NormalSlashObject;

		public GameObject AlternateSlashObject;

		public GameObject UpSlashObject;

		public GameObject AltUpSlashObject;

		public GameObject DownSlashObject;

		public GameObject AltDownSlashObject;

		public GameObject WallSlashObject;

		[Space]
		public GameObject DashStab;

		public GameObject DashStabAlt;

		public GameObject ChargeSlash;

		public GameObject TauntSlash;

		public NailSlash NormalSlash { get; private set; }

		public DamageEnemies NormalSlashDamager { get; private set; }

		public NailSlash AlternateSlash { get; private set; }

		public DamageEnemies AlternateSlashDamager { get; private set; }

		public NailSlash UpSlash { get; private set; }

		public DamageEnemies UpSlashDamager { get; private set; }

		public NailSlash AltUpSlash { get; private set; }

		public DamageEnemies AltUpSlashDamager { get; private set; }

		public NailSlash DownSlash { get; private set; }

		public Downspike Downspike { get; private set; }

		public DamageEnemies DownSlashDamager { get; private set; }

		public NailSlash AltDownSlash { get; private set; }

		public Downspike AltDownspike { get; private set; }

		public DamageEnemies AltDownSlashDamager { get; private set; }

		public NailSlash WallSlash { get; private set; }

		public DamageEnemies WallSlashDamager { get; private set; }

		public void Setup()
		{
			if ((bool)ActiveRoot)
			{
				ActiveRoot.SetActive(value: false);
			}
			if ((bool)NormalSlashObject)
			{
				NormalSlash = NormalSlashObject.GetComponent<NailSlash>();
				NormalSlashDamager = NormalSlashObject.GetComponent<DamageEnemies>();
			}
			if ((bool)AlternateSlashObject)
			{
				AlternateSlash = AlternateSlashObject.GetComponent<NailSlash>();
				AlternateSlashDamager = AlternateSlashObject.GetComponent<DamageEnemies>();
			}
			if ((bool)UpSlashObject)
			{
				UpSlash = UpSlashObject.GetComponent<NailSlash>();
				UpSlashDamager = UpSlashObject.GetComponent<DamageEnemies>();
			}
			if ((bool)AltUpSlashObject)
			{
				AltUpSlash = AltUpSlashObject.GetComponent<NailSlash>();
				AltUpSlashDamager = AltUpSlashObject.GetComponent<DamageEnemies>();
			}
			switch (Config.DownSlashType)
			{
			case HeroControllerConfig.DownSlashTypes.DownSpike:
				if ((bool)DownSlashObject)
				{
					Downspike = DownSlashObject.GetComponent<Downspike>();
				}
				if ((bool)AltDownSlashObject)
				{
					AltDownspike = AltDownSlashObject.GetComponent<Downspike>();
				}
				break;
			case HeroControllerConfig.DownSlashTypes.Slash:
				if ((bool)DownSlashObject)
				{
					DownSlash = DownSlashObject.GetComponent<NailSlash>();
				}
				if ((bool)AltDownSlashObject)
				{
					AltDownSlash = AltDownSlashObject.GetComponent<NailSlash>();
				}
				break;
			default:
				throw new NotImplementedException();
			case HeroControllerConfig.DownSlashTypes.Custom:
				break;
			}
			if ((bool)DownSlashObject)
			{
				DownSlashDamager = DownSlashObject.GetComponent<DamageEnemies>();
			}
			if ((bool)AltDownSlashObject)
			{
				AltDownSlashDamager = AltDownSlashObject.GetComponent<DamageEnemies>();
			}
			if ((bool)WallSlashObject)
			{
				WallSlash = WallSlashObject.GetComponent<NailSlash>();
				WallSlashDamager = WallSlashObject.GetComponent<DamageEnemies>();
			}
		}
	}

	public struct DecayingVelocity
	{
		public enum SkipBehaviours
		{
			None,
			WhileMoving,
			WhileMovingForward,
			WhileMovingBackward
		}

		public Vector2 Velocity;

		public float Decay;

		public bool CancelOnTurn;

		public SkipBehaviours SkipBehaviour;
	}

	private struct SilkEatTracker
	{
		public float EatDelayLeft;

		public float EatDurationLeft;

		public int EatSilkCount;
	}

	public struct DeliveryTimer
	{
		public DeliveryQuestItem.ActiveItem Item;

		public float TimeLeft;
	}

	public delegate void HeroSetDelegate(HeroController heroController);

	public delegate void HeroInPosition(bool forceDirect);

	public delegate void DamageTakenDelegate(DamageInfo damageInfo);

	public struct DamageInfo(HazardType hazardType)
	{
		public HazardType hazardType = hazardType;
	}

	public struct WarriorCrestStateInfo
	{
		public bool IsInRageMode;

		public float RageEffectTimeLeft;

		public float RageHealTimeLeft;

		public int RageModeHealCount;

		public int LastHealAttack;
	}

	public struct ReaperCrestStateInfo
	{
		public float ReaperModeDurationLeft;

		public bool IsInReaperMode;
	}

	public struct HunterUpgCrestStateInfo
	{
		public int CurrentMeterHits;

		public bool IsComboMeterAboveExtra => CurrentMeterHits >= Gameplay.HunterCombo2Hits + Gameplay.HunterCombo2ExtraHits;
	}

	public struct WandererCrestStateInfo
	{
		public bool QueuedNextHitCritical;

		public bool CriticalHitsLocked;
	}

	public const int GROUND_LAYERS = 8448;

	private const float HAZARD_LOOP_COOLDOWN = 0.5f;

	private bool verboseMode;

	public float RUN_SPEED;

	public float WALK_SPEED;

	public float JUMP_SPEED;

	public float MIN_JUMP_SPEED;

	public int JUMP_STEPS;

	public int JUMP_STEPS_MIN;

	public float AIR_HANG_GRAVITY;

	public float AIR_HANG_ACCEL;

	public float SHUTTLECOCK_SPEED;

	public float FLOAT_SPEED;

	public int DOUBLE_JUMP_RISE_STEPS;

	public int DOUBLE_JUMP_FALL_STEPS;

	public float JUMP_ABILITY_GROUND_RAY_LENGTH;

	public float WALLJUMP_RAY_LENGTH;

	public float WALLJUMP_BROLLY_RAY_LENGTH;

	public int WJLOCK_STEPS_SHORT;

	public int WJLOCK_STEPS_LONG;

	public int WJLOCK_CHAIN_STEPS;

	public float WJ_KICKOFF_SPEED;

	public int WALL_STICKY_STEPS;

	public float DASH_SPEED;

	public float DASH_TIME;

	public float AIR_DASH_TIME;

	public float DOWN_DASH_TIME;

	public int DASH_QUEUE_STEPS;

	public float DASH_COOLDOWN;

	public float WALLSLIDE_STICK_TIME;

	public float WALLSLIDE_ACCEL;

	public float WALLSLIDE_SHUTTLECOCK_VEL;

	public float WALLCLING_DECEL;

	public float WALLCLING_COOLDOWN;

	public float NAIL_CHARGE_TIME;

	public float NAIL_CHARGE_TIME_QUICK;

	public ToolItem NailChargeTimeQuickTool;

	public float NAIL_CHARGE_BEGIN_TIME;

	public float NAIL_CHARGE_BEGIN_TIME_QUICK;

	public float TIME_TO_ENTER_SCENE_BOT;

	public float SPEED_TO_ENTER_SCENE_HOR;

	public float SPEED_TO_ENTER_SCENE_UP;

	public float SPEED_TO_ENTER_SCENE_DOWN;

	public float DEFAULT_GRAVITY;

	public float UNDERWATER_GRAVITY;

	public float ALT_ATTACK_RESET;

	public int DOWNSPIKE_REBOUND_STEPS;

	public float DOWNSPIKE_REBOUND_SPEED;

	public float DOWNSPIKE_LAND_RECOVERY_TIME;

	public float DOWNSPIKE_ANTIC_DECELERATION;

	public MinMaxFloat DOWNSPIKE_ANTIC_CLAMP_VEL_Y;

	public float JUMP_SPEED_UPDRAFT_EXIT;

	public int DOWNSPIKE_INVULNERABILITY_STEPS = 2;

	public int DOWNSPIKE_INVULNERABILITY_STEPS_LONG = 10;

	public float BOUNCE_TIME;

	public float BOUNCE_VELOCITY;

	public float SHROOM_BOUNCE_VELOCITY;

	public float RECOIL_HOR_VELOCITY;

	public float RECOIL_HOR_VELOCITY_LONG;

	public float RECOIL_HOR_VELOCITY_DRILLDASH;

	public int RECOIL_HOR_STEPS;

	public float RECOIL_DOWN_VELOCITY;

	public float BIG_FALL_TIME;

	public float HARD_LANDING_TIME;

	public float DOWN_DASH_RECOVER_TIME;

	public float MAX_FALL_VELOCITY;

	public float MAX_FALL_VELOCITY_WEIGHTED;

	public float MAX_FALL_VELOCITY_DJUMP;

	public float RECOIL_DURATION;

	public float RECOIL_VELOCITY;

	public float DAMAGE_FREEZE_DOWN;

	public float DAMAGE_FREEZE_WAIT;

	public float DAMAGE_FREEZE_UP;

	public float DAMAGE_FREEZE_SPEED;

	public float INVUL_TIME;

	public float INVUL_TIME_PARRY;

	public float INVUL_TIME_QUAKE;

	public float INVUL_TIME_CROSS_STITCH;

	public float INVUL_TIME_SILKDASH;

	public float REVENGE_WINDOW_TIME;

	public float DASHCOMBO_WINDOW_TIME;

	public float CAST_RECOIL_VELOCITY;

	public float WALLSLIDE_CLIP_DELAY;

	[Space]
	public float QUICKENING_DURATION;

	public float QUICKENING_RUN_SPEED;

	public float QUICKENING_WALK_SPEED;

	[Space]
	public float FIRST_SILK_REGEN_DELAY;

	public float FIRST_SILK_REGEN_DURATION;

	public float SILK_REGEN_DELAY;

	public float SILK_REGEN_DURATION;

	[Space]
	public float CURSED_SILK_EAT_DELAY_FIRST;

	public float CURSED_SILK_EAT_DELAY;

	public float CURSED_SILK_EAT_DURATION;

	[Space]
	public float MAGGOTED_SILK_EAT_DELAY_FIRST;

	public float MAGGOTED_SILK_EAT_DELAY;

	public float MAGGOTED_SILK_EAT_DURATION;

	private int JUMP_QUEUE_STEPS = 2;

	private int JUMP_RELEASE_QUEUE_STEPS = 2;

	private int DOUBLE_JUMP_QUEUE_STEPS = 10;

	private int ATTACK_QUEUE_STEPS = 8;

	private int TOOLTHROW_QUEUE_STEPS = 5;

	private int HARPOON_QUEUE_STEPS = 8;

	private float LOOK_DELAY = 0.85f;

	private float LOOK_ANIM_DELAY = 0.25f;

	private float DEATH_WAIT = 4f;

	private const float FROST_DEATH_WAIT = 5.1f;

	private float HAZARD_DEATH_CHECK_TIME = 3f;

	private float FLOATING_CHECK_TIME = 0.18f;

	private int LANDING_BUFFER_STEPS = 5;

	private int LEDGE_BUFFER_STEPS = 4;

	private int HEAD_BUMP_STEPS = 3;

	private float FIND_GROUND_POINT_DISTANCE = 10f;

	private float FIND_GROUND_POINT_DISTANCE_EXT = 1000f;

	private const float MAX_SILK_REGEN_RATE = 0.03f;

	private double HARPOON_DASH_TIME;

	[Space]
	public ActorStates hero_state;

	public ActorStates prev_hero_state;

	public HeroTransitionState transitionState;

	public DamageMode damageMode;

	private HeroLockStates unlockRequests;

	public float move_input;

	public float vertical_input;

	public float controller_deadzone = 0.2f;

	public Vector2 current_velocity;

	private bool isGameplayScene;

	public bool isEnteringFirstLevel;

	private bool isDashStabBouncing;

	private bool canSoftLand;

	private int softLandTime;

	private bool blockSteepSlopes;

	private bool queuedLifeBloodState;

	public Vector2 slashOffset;

	public Vector2 upSlashOffset;

	public Vector2 downwardSlashOffset;

	public Vector2 spell1Offset;

	private int jump_steps;

	private int jumped_steps;

	private int doubleJump_steps;

	private float dash_timer;

	private float dash_time;

	private float attack_time;

	private float attack_cooldown;

	private float throwToolCooldown;

	private int downspike_rebound_steps;

	private Vector2 transition_vel;

	private float altAttackTime;

	private float lookDelayTimer;

	private float bounceTimer;

	private float hardLandingTimer;

	private float dashLandingTimer;

	private float recoilTimer;

	private int recoilStepsLeft;

	private int landingBufferSteps;

	private int dashQueueSteps;

	private bool dashQueuing;

	private float shadowDashTimer;

	private float dashCooldownTimer;

	private float nailChargeTimer;

	private int wallLockSteps;

	private int wallJumpChainStepsLeft;

	private float wallslideClipTimer;

	private float hardLandFailSafeTimer;

	private float hazardDeathTimer;

	private float floatingBufferTimer;

	private float attackDuration;

	private AttackDirection prevAttackDir = (AttackDirection)(-1);

	public float parryInvulnTimer;

	public float revengeWindowTimer;

	private float wandererDashComboWindowTimer;

	public float downSpikeTimer;

	public float downSpikeRecoveryTimer;

	private float toolThrowTime;

	private float toolThrowDuration;

	private float wallStickTimer;

	private float wallStickStartVelocity;

	private float lavaBellCooldownTimeLeft;

	private float shuttlecockTime;

	private float shuttlecockTimeResetTimer;

	private float wallClingCooldownTimer;

	private bool evadingDidClash;

	private bool doMaxSilkRegen;

	private float maxSilkRegenTimer;

	private int shuttleCockJumpSteps;

	[Space(6f)]
	[Header("Effect Prefabs")]
	public GameObject nailTerrainImpactEffectPrefab;

	public GameObject nailTerrainImpactEffectPrefabDownSpike;

	public Transform downspikeEffectPrefabSpawnPoint;

	public GameObject takeHitSingleEffectPrefab;

	public GameObject takeHitDoubleEffectPrefab;

	public GameObject takeHitDoubleBlackThreadEffectPrefab;

	public GameObject takeHitBlackHealthNullifyPrefab;

	public GameObject takeHitDoubleFlameEffectPrefab;

	public GameObject softLandingEffectPrefab;

	public GameObject hardLandingEffectPrefab;

	public RunEffects runEffectPrefab;

	public DashEffect backDashPrefab;

	public JumpEffects jumpEffectPrefab;

	public GameObject jumpTrailPrefab;

	public GameObject fallEffectPrefab;

	public ParticleSystem wallslideDustPrefab;

	public GameObject artChargeEffect;

	public GameObject artChargedEffect;

	public tk2dSpriteAnimator artChargedEffectAnim;

	public GameObject downspikeBurstPrefab;

	public GameObject dashBurstPrefab;

	public ParticleSystem dashParticles;

	public GameObject wallPuffPrefab;

	public GameObject backflipPuffPrefab;

	public GameObject airDashEffect;

	public GameObject walldashKickoffEffect;

	public GameObject umbrellaEffect;

	public GameObject doubleJumpEffectPrefab;

	public GameObject canBindEffect;

	public PlayParticleEffects quickeningEffectPrefab;

	public PlayParticleEffects quickeningPoisonEffectPrefab;

	public PlayParticleEffects maggotEffectPrefab;

	public PlayParticleEffects frostedEffect;

	public NoiseMaker SlashNoiseMakerFront;

	public NoiseMaker SlashNoiseMakerAbove;

	public NoiseMaker SlashNoiseMakerBelow;

	public GameObject luckyDiceShieldEffectPrefab;

	[SerializeField]
	private GameObject mossDamageEffectPrefab;

	[SerializeField]
	private GameObject witchDamageSpawn;

	[SerializeField]
	private GameObject silkAcidPrefab;

	[SerializeField]
	private GameObject voidAcidPrefab;

	[SerializeField]
	private GameObject frostEnterPrefab;

	[SerializeField]
	private GameObject heatEnterPrefab;

	[SerializeField]
	private GameObject maggotEnterPrefab;

	[SerializeField]
	private ParticleSystem runningWaterEffect;

	[SerializeField]
	private GameObject wallClingEffect;

	[SerializeField]
	private GameObject afterDamageEffectsPrefab;

	[Space(6f)]
	[Header("Hero Death")]
	public GameObject spikeDeathPrefab;

	public GameObject acidDeathPrefab;

	public GameObject lavaDeathPrefab;

	public GameObject coalDeathPrefab;

	public GameObject zapDeathPrefab;

	public GameObject sinkDeathPrefab;

	public GameObject steamDeathPrefab;

	public GameObject coalSpikeDeathPrefab;

	public GameObject heroDeathPrefab;

	public GameObject heroDeathCursedPrefab;

	public GameObject heroDeathNonLethalPrefab;

	public GameObject heroDeathMemoryPrefab;

	[SerializeField]
	private GameObject heroDeathFrostPrefab;

	[Space(6f)]
	[Header("Hero Other")]
	public GameObject cutscenePrefab;

	private GameManager gm;

	private Rigidbody2D rb2d;

	private Collider2D col2d;

	private MeshRenderer renderer;

	private new Transform transform;

	private HeroAnimationController animCtrl;

	public HeroBox heroBox;

	public HeroControllerStates cState;

	public PlayerData playerData;

	private HeroAudioController audioCtrl;

	private AudioSource audioSource;

	[HideInInspector]
	public UIManager ui;

	private InputHandler inputHandler;

	public PlayMakerFSM damageEffectFSM;

	private HeroVibrationController vibrationCtrl;

	public PlayMakerFSM sprintFSM;

	public PlayMakerFSM toolsFSM;

	private FsmBool sprintFSMIsQuickening;

	private FsmBool toolsFSMIsQuickening;

	private FsmBool sprintMasterActiveBool;

	private FsmFloat sprintSpeedAddFloat;

	public PlayMakerFSM mantleFSM;

	public PlayMakerFSM umbrellaFSM;

	public PlayMakerFSM silkSpecialFSM;

	public PlayMakerFSM crestAttacksFSM;

	public PlayMakerFSM harpoonDashFSM;

	public PlayMakerFSM superJumpFSM;

	public PlayMakerFSM bellBindFSM;

	public PlayMakerFSM wallScrambleFSM;

	private ParticleSystem dashParticleSystem;

	private InvulnerablePulse invPulse;

	private SpriteFlash spriteFlash;

	private float prevGravityScale;

	private Vector2 recoilVector;

	private Vector2 lastInputState;

	private Vector2 velocity_crt;

	private Vector2 velocity_prev;

	public GatePosition gatePosition;

	private bool hardLanded;

	private bool fallRumble;

	public bool acceptingInput;

	private bool fallTrailGenerated;

	private float dashBumpCorrection;

	public bool controlReqlinquished;

	public bool enterWithoutInput;

	public bool skipNormalEntry;

	private float downspike_rebound_xspeed;

	private float downSpikeHorizontalSpeed;

	private float shuttlecockSpeed;

	private float currentGravity;

	private bool didAirHang;

	private int updraftsEntered;

	private float harpoonDashCooldown;

	private EndBeta endBeta;

	private int lastLookDirection;

	private int controlRelinquishedFrame;

	private int jumpQueueSteps;

	private bool jumpQueuing;

	private int doubleJumpQueueSteps;

	private bool doubleJumpQueuing;

	private int jumpReleaseQueueSteps;

	private bool jumpReleaseQueuing;

	private int attackQueueSteps;

	private bool attackQueuing;

	private int harpoonQueueSteps;

	private bool harpoonQueuing;

	private int toolThrowQueueSteps;

	private bool toolThrowQueueing;

	public bool touchingWallL;

	public bool touchingWallR;

	private GameObject touchingWallObj;

	private bool wallSlidingL;

	private bool wallSlidingR;

	private bool airDashed;

	public bool dashingDown;

	private bool startWithWallslide;

	private bool startWithJump;

	private bool startWithAnyJump;

	private bool startWithTinyJump;

	private bool startWithShuttlecock;

	private bool startWithFullJump;

	private bool startWithFlipJump;

	private bool startWithBackflipJump;

	private bool startWithBrolly;

	private bool startWithDoubleJump;

	private bool startWithWallsprintLaunch;

	private bool startWithDash;

	private bool dashCurrentFacing;

	private bool startWithDownSpikeBounce;

	private bool startWithDownSpikeBounceSlightlyShort;

	private bool startWithDownSpikeBounceShort;

	private bool startWithDownSpikeEnd;

	private bool startWithHarpoonBounce;

	private bool startWithWitchSprintBounce;

	private bool startWithBalloonBounce;

	private bool startWithUpdraftExit;

	private bool useUpdraftExitJumpSpeed;

	private bool startWithScrambleLeap;

	private bool startWithRecoilBack;

	private bool startWithRecoilBackLong;

	private bool startWithWhipPullRecoil;

	private bool startWithAttack;

	private bool startWithToolThrow;

	private bool wallSlashing;

	private bool doubleJumped;

	private bool wallJumpedR;

	private bool wallJumpedL;

	public bool wallLocked;

	private float currentWalljumpSpeed;

	private float walljumpSpeedDecel;

	private int wallUnstickSteps;

	private float recoilVelocity;

	public float conveyorSpeed;

	private bool enteringVertically;

	private bool playingWallslideClip;

	private bool playedMantisClawClip;

	public bool exitedSuperDashing;

	public bool exitedQuake;

	public bool exitedSprinting;

	private bool fallCheckFlagged;

	private int ledgeBufferSteps;

	private int sprintBufferSteps;

	private bool syncBufferSteps;

	private double noShuttlecockTime;

	private int headBumpSteps;

	private bool takeNoDamage;

	private bool joniBeam;

	public bool fadedSceneIn;

	private bool stopWalkingOut;

	private bool boundsChecking;

	private bool blockerFix;

	private bool doFullJump;

	private bool startFromMantle;

	private bool allowMantle;

	private bool allowAttackCancellingDownspikeRecovery;

	private bool queuedWallJumpInterrupt;

	private bool startWithWallJump;

	private bool jumpPressedWhileRelinquished;

	private bool regainControlJumpQueued;

	private Vector2[] positionHistory;

	private bool tilemapTestActive;

	private bool allowNailChargingWhileRelinquished;

	private bool allowRecoilWhileRelinquished;

	private bool recoilZeroVelocity;

	private float silkRegenDelayLeft;

	private float silkRegenDurationLeft;

	private bool isSilkRegenBlocked;

	private bool hasSilkSpoolAppeared;

	private bool isNextSilkRegenUpgraded;

	private bool blockFsmMove;

	private SilkEatTracker maggotedSilkTracker;

	private float maggotCharmTimer;

	private int mossCreep1Hits;

	private int mossCreep2Hits;

	private float frostAmount;

	private float frostDamageTimer;

	private bool isInFrostRegion;

	private SpriteFlash.FlashHandle frostRegionFlash;

	private SpriteFlash.FlashHandle frostAnticFlash;

	private List<DeliveryTimer> currentTimedDeliveries;

	private bool doingHazardRespawn;

	private double lastHazardRespawnTime;

	private int luckyDiceShieldedHits;

	private bool forceWalkingSound;

	private float silkTauntEffectTimeLeft;

	private float ringTauntEffectTimeLeft;

	public const float TAUNT_EFFECT_DURATION = 6f;

	public const float TAUNT_EFFECT_DAMAGE_MULT = 1.5f;

	private int refillSoundSuppressFramesLeft;

	private Coroutine recoilRoutine;

	private Vector2 groundRayOriginC;

	private Vector2 groundRayOriginL;

	private Vector2 groundRayOriginR;

	private Coroutine takeDamageCoroutine;

	private Coroutine tilemapTestCoroutine;

	private Coroutine hazardInvulnRoutine;

	private Coroutine hazardRespawnRoutine;

	[SerializeField]
	[ArrayForEnum(typeof(EnvironmentTypes))]
	private RandomAudioClipTable[] footStepTables;

	public AudioClip nailArtChargeComplete;

	public AudioClip doubleJumpClip;

	public AudioClip mantisClawClip;

	public AudioClip downDashCancelClip;

	public AudioClip deathImpactClip;

	public RandomAudioClipTable attackAudioTable;

	public RandomAudioClipTable warriorRageAttackAudioTable;

	public RandomAudioClipTable quickSlingAudioTable;

	public RandomAudioClipTable woundAudioTable;

	public RandomAudioClipTable woundHeavyAudioTable;

	public RandomAudioClipTable woundFrostAudioTable;

	public RandomAudioClipTable hazardDamageAudioTable;

	public RandomAudioClipTable pitFallAudioTable;

	public RandomAudioClipTable deathAudioTable;

	public RandomAudioClipTable gruntAudioTable;

	public AudioSource shuttleCockJumpAudio;

	public GameObject shuttleCockJumpEffectPrefab;

	[SerializeField]
	private AudioEvent frostedEnterAudio;

	[SerializeField]
	private AudioSource frostedAudioLoop;

	[SerializeField]
	private float frostedAudioLoopFadeOut;

	private Coroutine frostedFadeOutRoutine;

	private NailSlash _slashComponent;

	private PlayMakerFSM slashFsm;

	private DamageEnemies currentSlashDamager;

	private Downspike currentDownspike;

	private RunEffects runEffect;

	private GameObject backDash;

	private GameObject fallEffect;

	private DashEffect dashEffect;

	private GameObject grubberFlyBeam;

	private GameObject hazardCorpe;

	private GameObject spawnedSilkAcid;

	private GameObject spawnedVoidAcid;

	private GameObject spawnedFrostEnter;

	private GameObject spawnedHeatEnter;

	private EnviroRegionListener enviroRegionListener;

	private TagDamageTaker tagDamageTaker;

	private CharacterBumpCheck bumpChecker;

	private PlayParticleEffects spawnedQuickeningEffect;

	private SpriteFlash.FlashHandle quickeningFlash;

	private PlayParticleEffects spawnedMaggotEffect;

	private GameObject spawnedMaggotEnter;

	private GameObject spawnedLuckyDiceShieldEffect;

	private SpriteFlash.FlashHandle maggotedFlash;

	private Coroutine cocoonFloatRoutine;

	private MatchXScaleSignOnEnable matchScale;

	private SpriteFlash.FlashHandle poisonHealthFlash;

	private Dictionary<DeliveryQuestItem, GameObject> spawnedDeliveryEffects = new Dictionary<DeliveryQuestItem, GameObject>();

	public PlayMakerFSM vignetteFSM;

	public HeroLight heroLight;

	public SpriteRenderer vignette;

	public PlayMakerFSM dashBurst;

	public PlayMakerFSM fsm_thornCounter;

	public PlayMakerFSM spellControl;

	public PlayMakerFSM fsm_fallTrail;

	public PlayMakerFSM fsm_orbitShield;

	public PlayMakerFSM fsm_brollyControl;

	[SerializeField]
	private Transform toolThrowPoint;

	[SerializeField]
	private Transform toolThrowClosePoint;

	[SerializeField]
	private Transform toolThrowWallPoint;

	[SerializeField]
	private PlayMakerFSM toolEventTarget;

	[SerializeField]
	private PlayMakerFSM skillEventTarget;

	[SerializeField]
	private DamageTag acidDamageTag;

	[SerializeField]
	private DamageTag frostWaterDamageTag;

	[SerializeField]
	private GameObject lavaBellEffectPrefab;

	[SerializeField]
	private GameObject lavaBellRechargeEffectPrefab;

	[SerializeField]
	private GameObject heroPhysicsPusher;

	private GameObject spawnedLavaBellRechargeEffect;

	[SerializeField]
	private NestedFadeGroupTimedFader rageModeEffectPrefab;

	private NestedFadeGroupTimedFader rageModeEffect;

	[SerializeField]
	private NestedFadeGroupTimedFader reaperModeEffectPrefab;

	private NestedFadeGroupTimedFader reaperModeEffect;

	[Space]
	[SerializeField]
	private ConfigGroup[] configs;

	[SerializeField]
	private ConfigGroup[] specialConfigs;

	[NonSerialized]
	public bool isHeroInPosition;

	private bool isHeroInPrePosition;

	private bool jumpReleaseQueueingEnabled;

	private ToolItem willThrowTool;

	private bool queuedAutoThrowTool;

	private const float AUTO_THROW_TOOL_DELAY = 0.15f;

	private bool lookDownBlocked;

	private WarriorCrestStateInfo warriorState;

	private readonly Probability.ProbabilityInt[] reaperBundleDrops = new Probability.ProbabilityInt[3]
	{
		new Probability.ProbabilityInt
		{
			Value = 1,
			Probability = 0.35f
		},
		new Probability.ProbabilityInt
		{
			Value = 2,
			Probability = 0.5f
		},
		new Probability.ProbabilityInt
		{
			Value = 3,
			Probability = 0.15f
		}
	};

	private float silkPartsTimeLeft;

	private ReaperCrestStateInfo reaperState;

	private HunterUpgCrestStateInfo hunterUpgState;

	private float quickeningTimeLeft;

	private bool didCheckEdgeAdjust;

	private NailSlash normalSlash;

	private DamageEnemies normalSlashDamager;

	private NailSlash alternateSlash;

	private DamageEnemies alternateSlashDamager;

	private NailSlash upSlash;

	private DamageEnemies upSlashDamager;

	private NailSlash altUpSlash;

	private DamageEnemies altUpSlashDamager;

	private NailSlash downSlash;

	private Downspike downSpike;

	private DamageEnemies downSlashDamager;

	private NailSlash altDownSlash;

	private Downspike altDownSpike;

	private DamageEnemies altDownSlashDamager;

	private NailSlash wallSlash;

	private DamageEnemies wallSlashDamager;

	private AreaEffectTint areaEffectTint;

	private readonly HashSet<object> inputBlockers = new HashSet<object>();

	private readonly List<DecayingVelocity> extraAirMoveVelocities = new List<DecayingVelocity>();

	private HeroControllerConfig crestConfig;

	private static HeroController _instance;

	private static int lastUpdate = -1;

	private FsmBool isUmbrellaActive;

	private bool tryShove;

	private bool onFlatGround;

	private List<DamageEnemies> damageEnemiesList = new List<DamageEnemies>();

	private static RaycastHit2D[] _rayHitStore = new RaycastHit2D[10];

	private double canThrowTime;

	private bool tryCancelDownSlash;

	private int parriedAttack;

	private bool announceNextFixedUpdate;

	private double recoilAllowTime;

	private const float PreventCastByDialogueEndDuration = 0.3f;

	private float preventCastByDialogueEndTimer;

	private float preventSoftLandTimer;

	private float invulnerableFreezeDuration;

	private float invulnerableDuration;

	private WallTouchCache leftCache = new WallTouchCache();

	private WallTouchCache rightCache = new WallTouchCache();

	private TouchGroundResult checkTouchGround = new TouchGroundResult();

	private float CurrentNailChargeTime
	{
		get
		{
			if (!NailChargeTimeQuickTool.IsEquipped)
			{
				return NAIL_CHARGE_TIME;
			}
			return NAIL_CHARGE_TIME_QUICK;
		}
	}

	private float CurrentNailChargeBeginTime
	{
		get
		{
			if (!NailChargeTimeQuickTool.IsEquipped)
			{
				return NAIL_CHARGE_BEGIN_TIME;
			}
			return NAIL_CHARGE_BEGIN_TIME_QUICK;
		}
	}

	public HeroLockStates HeroLockState { get; private set; }

	private bool CanTurn
	{
		get
		{
			if (!cState.downSpikeBouncing && !queuedAutoThrowTool)
			{
				return !animCtrl.IsTurnBlocked();
			}
			return false;
		}
	}

	public float fallTimer { get; private set; }

	public HeroAudioController AudioCtrl => audioCtrl;

	public Bounds Bounds => col2d.bounds;

	public PlayMakerFSM proxyFSM { get; private set; }

	public HeroNailImbuement NailImbuement { get; private set; }

	public bool IsSprintMasterActive => sprintMasterActiveBool.Value;

	public bool IsGravityApplied { get; private set; }

	public TransitionPoint sceneEntryGate { get; private set; }

	public Vector2[] PositionHistory => positionHistory;

	public bool ForceWalkingSound
	{
		get
		{
			return forceWalkingSound;
		}
		set
		{
			forceWalkingSound = value;
		}
	}

	public bool ForceRunningSound { get; set; }

	public bool IsStunned { get; set; }

	private NailSlash SlashComponent
	{
		get
		{
			return _slashComponent;
		}
		set
		{
			if (cState.downAttacking && _slashComponent != null && _slashComponent != value)
			{
				_slashComponent.CancelAttack();
			}
			_slashComponent = value;
		}
	}

	public WarriorCrestStateInfo WarriorState => warriorState;

	public ReaperCrestStateInfo ReaperState => reaperState;

	public HunterUpgCrestStateInfo HunterUpgState => hunterUpgState;

	public WandererCrestStateInfo WandererState { get; set; }

	public bool IsUsingQuickening => quickeningTimeLeft > 0f;

	public bool IsInLifebloodState { get; private set; }

	public bool IsWandererLucky
	{
		get
		{
			if (cState.isMaggoted)
			{
				return false;
			}
			if (!Gameplay.WandererCrest.IsEquipped)
			{
				return false;
			}
			return playerData.silk >= 9;
		}
	}

	public int PoisonHealthCount { get; private set; }

	public Rigidbody2D Body => rb2d;

	public bool HasAnimationControl => animCtrl.controlEnabled;

	public HeroAnimationController AnimCtrl => animCtrl;

	public static HeroController instance => SilentInstance;

	public static HeroController SilentInstance
	{
		get
		{
			if (lastUpdate != CustomPlayerLoop.FixedUpdateCycle)
			{
				lastUpdate = CustomPlayerLoop.FixedUpdateCycle;
				if (_instance == null)
				{
					_instance = UnityEngine.Object.FindObjectOfType<HeroController>();
					if ((bool)_instance && Application.isPlaying)
					{
						HeroController.OnHeroInstanceSet?.Invoke(_instance);
						HeroUtility.Reset();
						UnityEngine.Object.DontDestroyOnLoad(_instance.gameObject);
					}
					else
					{
						lastUpdate = -1;
					}
				}
			}
			return _instance;
		}
	}

	public static HeroController UnsafeInstance => _instance;

	public HeroControllerConfig Config
	{
		get
		{
			if (CurrentConfigGroup == null)
			{
				return null;
			}
			return CurrentConfigGroup.Config;
		}
	}

	public ConfigGroup CurrentConfigGroup { get; private set; }

	public SpriteFlash SpriteFlash => spriteFlash;

	public Vector2 TagDamageEffectPos => Vector2.zero;

	public bool IsRefillSoundsSuppressed => refillSoundSuppressFramesLeft > 0;

	public bool ForceClampTerminalVelocity { get; set; }

	private int CriticalHealthValue
	{
		get
		{
			float num = 1f;
			if (Gameplay.BarbedWireTool.Status.IsEquipped)
			{
				num *= Gameplay.BarbedWireDamageTakenMultiplier;
			}
			return Mathf.FloorToInt(num);
		}
	}

	public static int ControlVersion { get; private set; }

	public int AnimationControlVersion { get; private set; }

	public event Action<float> FrostAmountUpdated;

	public event Action OnDoubleJumped;

	public static event HeroSetDelegate OnHeroInstanceSet;

	public event HeroInPosition preHeroInPosition;

	public event HeroInPosition heroInPosition;

	public event HeroInPosition heroInPositionDelayed;

	public event Action OnTakenDamage;

	public event DamageTakenDelegate OnTakenDamageExtra;

	public event Action OnDeath;

	public event Action OnHazardDeath;

	public event Action OnHazardRespawn;

	public event Action<Vector2> BeforeApplyConveyorSpeed;

	public event Action FlippedSprite;

	public event Action HeroLeavingScene;

	private void OnValidate()
	{
		ArrayForEnumAttribute.EnsureArraySize(ref footStepTables, typeof(EnvironmentTypes));
	}

	private void Awake()
	{
		OnValidate();
		if (_instance == null)
		{
			_instance = this;
			HeroController.OnHeroInstanceSet?.Invoke(_instance);
			UnityEngine.Object.DontDestroyOnLoad(this);
			HeroUtility.Reset();
		}
		else if (this != _instance)
		{
			UnityEngine.Object.Destroy(base.gameObject);
			return;
		}
		EventRegister spoolAppearedRegister = EventRegister.GetRegisterGuaranteed(base.gameObject, "SPOOL APPEARED");
		Action temp = null;
		temp = delegate
		{
			hasSilkSpoolAppeared = true;
			spoolAppearedRegister.ReceivedEvent -= temp;
		};
		spoolAppearedRegister.ReceivedEvent += temp;
		SetupGameRefs();
		SetupPools();
		rageModeEffect = SpawnChildPrefab(rageModeEffectPrefab);
		reaperModeEffect = SpawnChildPrefab(reaperModeEffectPrefab);
		spawnedSilkAcid = SpawnChildPrefab(silkAcidPrefab);
		spawnedVoidAcid = SpawnChildPrefab(voidAcidPrefab);
		spawnedFrostEnter = SpawnChildPrefab(frostEnterPrefab);
		spawnedHeatEnter = SpawnChildPrefab(heatEnterPrefab);
		spawnedMaggotEnter = SpawnChildPrefab(maggotEnterPrefab);
		spawnedLuckyDiceShieldEffect = SpawnChildPrefab(luckyDiceShieldEffectPrefab);
		spawnedLavaBellRechargeEffect = SpawnChildPrefab(lavaBellRechargeEffectPrefab);
		ConfigGroup[] array = configs;
		for (int num = 0; num < array.Length; num++)
		{
			array[num].Setup();
		}
		array = specialConfigs;
		for (int num = 0; num < array.Length; num++)
		{
			array[num].Setup();
		}
		UpdateConfig();
		EventRegister.GetRegisterGuaranteed(base.gameObject, "TOOL EQUIPS CHANGED").ReceivedEvent += ResetAllCrestState;
		ResetAllCrestState();
		tagDamageTaker = TagDamageTaker.Add(base.gameObject, this);
		bumpChecker = CharacterBumpCheck.Add(base.gameObject, 8448, rb2d, col2d, () => cState.facingRight);
		OnDeath += CurrencyObjectBase.ProcessHeroDeath;
		wallClingEffect.SetActive(value: false);
	}

	private void OnDestroy()
	{
		if (_instance == this)
		{
			_instance = null;
		}
		if ((bool)heroPhysicsPusher)
		{
			UnityEngine.Object.Destroy(heroPhysicsPusher);
		}
		damageEnemiesList.Clear();
	}

	private GameObject SpawnChildPrefab(GameObject prefab)
	{
		if (!prefab)
		{
			return null;
		}
		GameObject obj = UnityEngine.Object.Instantiate(prefab);
		Transform obj2 = obj.transform;
		obj2.SetParent(transform);
		obj2.localPosition = Vector3.zero;
		obj.gameObject.SetActive(value: false);
		return obj;
	}

	private T SpawnChildPrefab<T>(T prefab) where T : Component
	{
		if (!prefab)
		{
			return null;
		}
		T val = UnityEngine.Object.Instantiate(prefab);
		Transform obj = val.transform;
		obj.SetParent(transform);
		obj.localPosition = Vector3.zero;
		val.gameObject.SetActive(value: false);
		return val;
	}

	private void Start()
	{
		playerData = PlayerData.instance;
		ui = UIManager.instance;
		if (fsm_thornCounter == null)
		{
			fsm_thornCounter = FSMUtility.LocateFSM(transform.Find("Charm Effects").gameObject, "Thorn Counter");
		}
		if (dashBurst == null)
		{
			dashBurst = FSMUtility.GetFSM(transform.Find("Effects").Find("Dash Burst").gameObject);
		}
		if (spellControl == null)
		{
			spellControl = FSMUtility.LocateFSM(base.gameObject, "Spell Control");
		}
		if (gm.IsGameplayScene())
		{
			isGameplayScene = true;
			vignette.enabled = true;
			SendHeroInPosition(forceDirect: false);
			FinishedEnteringScene();
			if (GameManager.instance.profileID == 0)
			{
				AddSilk(playerData.CurrentSilkMax, heroEffect: false);
			}
		}
		else
		{
			isGameplayScene = false;
			transform.SetPositionY(-50000f);
			vignette.enabled = false;
			AffectedByGravity(gravityApplies: false);
		}
		if ((bool)sprintFSM)
		{
			sprintFSMIsQuickening = sprintFSM.FsmVariables.GetFsmBool("Is Quickening Active");
			toolsFSMIsQuickening = toolsFSM.FsmVariables.GetFsmBool("Is Quickening Active");
			sprintMasterActiveBool = sprintFSM.FsmVariables.FindFsmBool("Sprintmaster Active");
			sprintSpeedAddFloat = sprintFSM.FsmVariables.FindFsmFloat("Add Speed");
		}
		CharmUpdate();
		if ((bool)acidDeathPrefab)
		{
			ObjectPool.CreatePool(acidDeathPrefab, 1);
		}
		if ((bool)spikeDeathPrefab)
		{
			ObjectPool.CreatePool(spikeDeathPrefab, 1);
		}
		if ((bool)zapDeathPrefab)
		{
			ObjectPool.CreatePool(zapDeathPrefab, 1);
		}
		DeliveryQuestItem.BreakTimedNoEffects();
		SetupDeliveryItems();
		if (umbrellaFSM != null)
		{
			isUmbrellaActive = umbrellaFSM.FsmVariables.FindFsmBool("Is Active");
		}
		damageEnemiesList.AddRange(GetComponentsInChildren<DamageEnemies>(includeInactive: true));
	}

	private void SendPreHeroInPosition(bool forceDirect)
	{
		isHeroInPrePosition = true;
		this.preHeroInPosition?.Invoke(forceDirect);
	}

	private void SendHeroInPosition(bool forceDirect)
	{
		if (!isHeroInPrePosition)
		{
			SendPreHeroInPosition(forceDirect);
		}
		isHeroInPosition = true;
		animCtrl.waitingToEnter = false;
		gm.cameraCtrl.ResetStartTimer();
		if (this.heroInPosition != null)
		{
			this.heroInPosition(forceDirect: false);
		}
		if (this.heroInPositionDelayed != null)
		{
			this.heroInPositionDelayed(forceDirect: false);
		}
		vignetteFSM.SendEvent("RESET");
	}

	private void UpdateConfig()
	{
		if ((bool)crestConfig)
		{
			HeroControllerConfig heroControllerConfig = crestConfig;
			ConfigGroup configGroup = null;
			ConfigGroup[] array = configs;
			foreach (ConfigGroup configGroup2 in array)
			{
				if (!(configGroup2.Config != heroControllerConfig))
				{
					configGroup = configGroup2;
					break;
				}
			}
			ConfigGroup overrideGroup = null;
			array = specialConfigs;
			foreach (ConfigGroup configGroup3 in array)
			{
				if (!(configGroup3.Config != heroControllerConfig) && (!(configGroup3.Config == Gameplay.WarriorCrest.HeroConfig) || warriorState.IsInRageMode))
				{
					overrideGroup = configGroup3;
					break;
				}
			}
			SetConfigGroup(configGroup, overrideGroup);
		}
		else
		{
			SetConfigGroup(configs[0], null);
		}
	}

	private void SetConfigGroup(ConfigGroup configGroup, ConfigGroup overrideGroup)
	{
		if (configGroup == null)
		{
			Debug.LogError("configGroup was null!");
			return;
		}
		ConfigGroup currentConfigGroup = CurrentConfigGroup;
		HeroControllerConfig heroControllerConfig = currentConfigGroup?.Config;
		CurrentConfigGroup = configGroup;
		HeroControllerConfig config = configGroup.Config;
		if (config != heroControllerConfig)
		{
			if (heroControllerConfig != null && !config.ForceBareInventory && heroControllerConfig.ForceBareInventory)
			{
				CollectableItemManager.ApplyHiddenItems();
				gm.gameMap.SetupMap();
			}
			ToolItemManager.ReportAllBoundAttackToolsUpdated();
			animCtrl.SetHeroControllerConfig(config);
			if (currentConfigGroup != null && (bool)currentConfigGroup.ActiveRoot)
			{
				currentConfigGroup.ActiveRoot.SetActive(value: false);
			}
			if ((bool)configGroup.ActiveRoot)
			{
				configGroup.ActiveRoot.SetActive(value: true);
			}
		}
		normalSlash = ((overrideGroup != null && (bool)overrideGroup.NormalSlash) ? overrideGroup.NormalSlash : configGroup.NormalSlash);
		normalSlashDamager = ((overrideGroup != null && (bool)overrideGroup.NormalSlashDamager) ? overrideGroup.NormalSlashDamager : configGroup.NormalSlashDamager);
		alternateSlash = ((overrideGroup != null && (bool)overrideGroup.AlternateSlash) ? overrideGroup.AlternateSlash : configGroup.AlternateSlash);
		alternateSlashDamager = ((overrideGroup != null && (bool)overrideGroup.AlternateSlashDamager) ? overrideGroup.AlternateSlashDamager : configGroup.AlternateSlashDamager);
		upSlash = ((overrideGroup != null && (bool)overrideGroup.UpSlash) ? overrideGroup.UpSlash : configGroup.UpSlash);
		upSlashDamager = ((overrideGroup != null && (bool)overrideGroup.UpSlashDamager) ? overrideGroup.UpSlashDamager : configGroup.UpSlashDamager);
		altUpSlash = ((overrideGroup != null && (bool)overrideGroup.AltUpSlash) ? overrideGroup.AltUpSlash : configGroup.AltUpSlash);
		altUpSlashDamager = ((overrideGroup != null && (bool)overrideGroup.AltUpSlashDamager) ? overrideGroup.AltUpSlashDamager : configGroup.AltUpSlashDamager);
		downSpike = ((overrideGroup != null && (bool)overrideGroup.Downspike) ? overrideGroup.Downspike : configGroup.Downspike);
		downSlash = ((overrideGroup != null && (bool)overrideGroup.DownSlash) ? overrideGroup.DownSlash : configGroup.DownSlash);
		downSlashDamager = ((overrideGroup != null && (bool)overrideGroup.DownSlashDamager) ? overrideGroup.DownSlashDamager : configGroup.DownSlashDamager);
		altDownSpike = ((overrideGroup != null && (bool)overrideGroup.AltDownspike) ? overrideGroup.AltDownspike : configGroup.AltDownspike);
		altDownSlash = ((overrideGroup != null && (bool)overrideGroup.AltDownSlash) ? overrideGroup.AltDownSlash : configGroup.AltDownSlash);
		altDownSlashDamager = ((overrideGroup != null && (bool)overrideGroup.AltDownSlashDamager) ? overrideGroup.AltDownSlashDamager : configGroup.AltDownSlashDamager);
		wallSlash = ((overrideGroup != null && (bool)overrideGroup.WallSlash) ? overrideGroup.WallSlash : configGroup.WallSlash);
		wallSlashDamager = ((overrideGroup != null && (bool)overrideGroup.WallSlashDamager) ? overrideGroup.WallSlashDamager : configGroup.WallSlashDamager);
		if (config != heroControllerConfig)
		{
			FSMUtility.SendEventToGameObject(base.gameObject, "HC CONFIG UPDATED");
		}
	}

	public void SceneInit()
	{
		if (!(this != _instance))
		{
			if (!gm)
			{
				gm = GameManager.instance;
			}
			if (gm.IsGameplayScene())
			{
				isGameplayScene = true;
				HeroBox.Inactive = false;
				damageMode = DamageMode.FULL_DAMAGE;
			}
			else
			{
				isGameplayScene = false;
				acceptingInput = false;
				SetState(ActorStates.no_input);
				transform.SetPositionY(-50000f);
				vignette.enabled = false;
				AffectedByGravity(gravityApplies: false);
				rb2d.linearVelocity = Vector2.zero;
			}
			transform.SetPositionZ(0.004f);
			SetWalkZone(inWalkZone: false);
			ResetUpdraft();
			ResetTauntEffects();
			cState.invulnerable = false;
			cState.evading = false;
			cState.whipLashing = false;
			cState.isTriggerEventsPaused = false;
		}
	}

	private void Update()
	{
		if (Time.frameCount % 10 == 0)
		{
			Update10();
		}
		current_velocity = rb2d.linearVelocity;
		if (transitionState == HeroTransitionState.WAITING_TO_TRANSITION)
		{
			FallCheck();
		}
		FailSafeChecks();
		if (hero_state == ActorStates.running && !cState.dashing && !cState.backDashing && !controlReqlinquished)
		{
			if (cState.inWalkZone)
			{
				audioCtrl.StopSound(HeroSounds.FOOTSTEPS_RUN);
				audioCtrl.StopSound(HeroSounds.FOOTSTEPS_SPRINT);
				audioCtrl.PlaySound(HeroSounds.FOOTSTEPS_WALK, playVibration: true);
			}
			else
			{
				audioCtrl.StopSound(HeroSounds.FOOTSTEPS_WALK);
				audioCtrl.StopSound(HeroSounds.FOOTSTEPS_SPRINT);
				audioCtrl.PlaySound(HeroSounds.FOOTSTEPS_RUN, playVibration: true);
			}
			Vector2 linearVelocity = rb2d.linearVelocity;
			bool flag = (linearVelocity.x < -0.1f || linearVelocity.x > 0.1f) && !cState.inWalkZone;
			if ((bool)runEffect && !flag)
			{
				runEffect.Stop();
				runEffect = null;
			}
			if (!runEffect && flag)
			{
				runEffect = runEffectPrefab.Spawn();
				runEffect.transform.SetParent(base.gameObject.transform, worldPositionStays: false);
				runEffect.StartEffect(isHero: true);
			}
		}
		else
		{
			if (ForceWalkingSound)
			{
				audioCtrl.PlaySound(HeroSounds.FOOTSTEPS_WALK, playVibration: true);
			}
			else
			{
				audioCtrl.StopSound(HeroSounds.FOOTSTEPS_WALK);
			}
			if (ForceRunningSound)
			{
				audioCtrl.PlaySound(HeroSounds.FOOTSTEPS_RUN, playVibration: true);
			}
			else
			{
				audioCtrl.StopSound(HeroSounds.FOOTSTEPS_RUN);
			}
			if ((bool)runEffect)
			{
				runEffect.Stop();
				runEffect = null;
			}
			if ((cState.isSprinting || cState.isBackScuttling) && cState.onGround)
			{
				audioCtrl.PlaySound(HeroSounds.FOOTSTEPS_SPRINT, playVibration: true);
			}
			else
			{
				audioCtrl.StopSound(HeroSounds.FOOTSTEPS_SPRINT);
			}
		}
		if (hero_state == ActorStates.dash_landing)
		{
			dashLandingTimer += Time.deltaTime;
			if (dashLandingTimer > DOWN_DASH_RECOVER_TIME)
			{
				BackOnGround(force: true);
				FinishedDashing(wasDashingDown: true);
			}
		}
		if (hero_state == ActorStates.hard_landing)
		{
			hardLandingTimer += Time.deltaTime;
			if (hardLandingTimer > HARD_LANDING_TIME)
			{
				SetState(ActorStates.grounded);
				BackOnGround();
			}
		}
		else if (hero_state == ActorStates.no_input)
		{
			LookForInput();
			if (cState.recoiling)
			{
				if (recoilTimer < RECOIL_DURATION)
				{
					recoilTimer += Time.deltaTime;
				}
				else
				{
					StartRevengeWindow();
					CancelDamageRecoil();
					if ((prev_hero_state == ActorStates.idle || prev_hero_state == ActorStates.running) && !CheckTouchingGround())
					{
						cState.onGround = false;
						SetState(ActorStates.airborne);
					}
					else
					{
						SetState(ActorStates.previous);
					}
				}
			}
		}
		else if (hero_state != ActorStates.no_input)
		{
			LookForInput();
			if (cState.recoiling)
			{
				cState.recoiling = false;
				AffectedByGravity(gravityApplies: true);
			}
			if (cState.attacking && !cState.dashing)
			{
				attack_time += Time.deltaTime;
				if (attack_time >= attackDuration)
				{
					ResetAttacks();
					animCtrl.StopAttack();
				}
			}
			if (cState.isToolThrowing)
			{
				toolThrowTime += Time.deltaTime;
				if (toolThrowTime >= toolThrowDuration || (queuedAutoThrowTool && toolThrowTime >= 0.15f))
				{
					bool num = queuedAutoThrowTool;
					ThrowToolEnd();
					if (num)
					{
						ThrowTool(isAutoThrow: true);
					}
				}
			}
			if (cState.bouncing)
			{
				if (bounceTimer < BOUNCE_TIME)
				{
					bounceTimer += Time.deltaTime;
				}
				else
				{
					CancelBounce();
					rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, 0f);
				}
			}
			if (cState.shroomBouncing && current_velocity.y <= 0f)
			{
				cState.shroomBouncing = false;
			}
			if (cState.shuttleCock)
			{
				if (cState.attacking || (cState.falling && !cState.jumping) || cState.downSpikeAntic)
				{
					ShuttleCockCancel();
					if (cState.falling && inputHandler.inputActions.Dash.IsPressed)
					{
						sprintFSM.SendEvent("TRY SPRINT");
					}
				}
				if (controlReqlinquished)
				{
					ShuttleCockCancel();
				}
				shuttlecockTime += Time.deltaTime;
				shuttlecockTimeResetTimer = 0.1f;
			}
			else if (shuttlecockTimeResetTimer > 0f)
			{
				shuttlecockTimeResetTimer -= Time.deltaTime;
			}
			else if (shuttlecockTime > 0f)
			{
				shuttlecockTime = 0f;
			}
			if (hero_state == ActorStates.idle && !controlReqlinquished && !IsPaused() && !IsInputBlocked() && CanAttack())
			{
				if (lastLookDirection == 0)
				{
					if (inputHandler.inputActions.Up.IsPressed || inputHandler.inputActions.RsUp.IsPressed)
					{
						lastLookDirection = 1;
					}
					else if (inputHandler.inputActions.Down.IsPressed || inputHandler.inputActions.RsDown.IsPressed)
					{
						lastLookDirection = 2;
					}
				}
				else if (lastLookDirection == 1)
				{
					if (inputHandler.inputActions.Up.IsPressed || inputHandler.inputActions.RsUp.IsPressed)
					{
						lastLookDirection = 1;
					}
					else
					{
						lastLookDirection = 0;
					}
				}
				else if (lastLookDirection == 2)
				{
					if (inputHandler.inputActions.Down.IsPressed || inputHandler.inputActions.RsDown.IsPressed)
					{
						lastLookDirection = 2;
					}
					else
					{
						lastLookDirection = 0;
					}
				}
				if (lastLookDirection == 1)
				{
					cState.lookingDown = false;
					cState.lookingDownAnim = false;
					if (lookDelayTimer >= LOOK_DELAY || (inputHandler.inputActions.RsUp.IsPressed && !cState.jumping && !cState.dashing))
					{
						cState.lookingUp = true;
					}
					else
					{
						lookDelayTimer += Time.deltaTime;
					}
					if (lookDelayTimer >= LOOK_ANIM_DELAY || inputHandler.inputActions.RsUp.IsPressed)
					{
						cState.lookingUpAnim = true;
					}
					else
					{
						cState.lookingUpAnim = false;
					}
				}
				else if (lastLookDirection == 2)
				{
					cState.lookingUp = false;
					cState.lookingUpAnim = false;
					if (lookDownBlocked)
					{
						cState.lookingDown = false;
					}
					else
					{
						if (lookDelayTimer >= LOOK_DELAY || (inputHandler.inputActions.RsDown.IsPressed && !cState.jumping && !cState.dashing))
						{
							cState.lookingDown = true;
						}
						else
						{
							lookDelayTimer += Time.deltaTime;
						}
						if (lookDelayTimer >= LOOK_ANIM_DELAY || inputHandler.inputActions.RsDown.IsPressed)
						{
							cState.lookingDownAnim = true;
						}
						else
						{
							cState.lookingDownAnim = false;
						}
					}
				}
				else
				{
					ResetLook();
					lookDownBlocked = false;
				}
			}
		}
		LookForQueueInput();
		if (cState.wallSliding)
		{
			if (airDashed)
			{
				airDashed = false;
			}
			if (doubleJumped)
			{
				doubleJumped = false;
			}
			if (cState.onGround)
			{
				FlipSprite();
				CancelWallsliding();
			}
			else if (!cState.touchingWall)
			{
				FlipSprite();
				CancelWallsliding();
			}
			else if (!CanContinueWallSlide())
			{
				CancelWallsliding();
			}
			if (cState.wallSliding)
			{
				if (!playedMantisClawClip)
				{
					audioSource.PlayOneShot(mantisClawClip, 1f);
					playedMantisClawClip = true;
				}
				bool wallClinging = cState.wallClinging;
				cState.wallClinging = !NoWallClingRegion.IsWallClingBlocked && wallClingCooldownTimer <= 0f && Gameplay.WallClingTool.Status.IsEquipped && ((wallSlidingL && move_input < 0f) || (wallSlidingR && move_input > 0f));
				if (cState.wallClinging)
				{
					if (!wallClinging)
					{
						wallClingEffect.SetActive(value: false);
						wallClingEffect.SetActive(value: true);
					}
					wallslideClipTimer = 0f;
					audioCtrl.StopSound(HeroSounds.WALLSLIDE);
					playingWallslideClip = false;
					AffectedByGravity(gravityApplies: false);
				}
				else
				{
					if (wallClinging)
					{
						wallClingCooldownTimer = WALLCLING_COOLDOWN;
					}
					if (!playingWallslideClip)
					{
						if (wallslideClipTimer <= WALLSLIDE_CLIP_DELAY)
						{
							wallslideClipTimer += Time.deltaTime;
						}
						else
						{
							wallslideClipTimer = 0f;
							audioCtrl.PlaySound(HeroSounds.WALLSLIDE, playVibration: true);
							vibrationCtrl.StartWallSlide();
							playingWallslideClip = true;
						}
					}
				}
			}
		}
		else
		{
			if (playedMantisClawClip)
			{
				playedMantisClawClip = false;
			}
			if (playingWallslideClip)
			{
				audioCtrl.StopSound(HeroSounds.WALLSLIDE);
				playingWallslideClip = false;
			}
			if (wallslideClipTimer > 0f)
			{
				wallslideClipTimer = 0f;
			}
			if (wallStickTimer > 0f)
			{
				wallStickTimer = 0f;
			}
			if (wallSlashing)
			{
				CancelAttack();
			}
		}
		if (attack_cooldown > 0f)
		{
			attack_cooldown -= Time.deltaTime;
		}
		if (throwToolCooldown > 0f)
		{
			throwToolCooldown -= Time.deltaTime;
		}
		if (dashCooldownTimer > 0f)
		{
			dashCooldownTimer -= Time.deltaTime;
		}
		if (shadowDashTimer > 0f)
		{
			shadowDashTimer -= Time.deltaTime;
			if (shadowDashTimer <= 0f)
			{
				spriteFlash.FlashShadowRecharge();
			}
		}
		if (harpoonDashCooldown > 0f)
		{
			if (cState.onGround)
			{
				harpoonDashCooldown = 0f;
			}
			harpoonDashCooldown -= Time.deltaTime;
		}
		if (wallClingCooldownTimer > 0f)
		{
			wallClingCooldownTimer -= Time.deltaTime;
		}
		if (cState.downSpikeRecovery)
		{
			if (cState.onGround)
			{
				if (downSpikeRecoveryTimer >= DOWNSPIKE_LAND_RECOVERY_TIME)
				{
					cState.downSpikeRecovery = false;
				}
			}
			else if (downSpikeRecoveryTimer >= Config.DownspikeRecoveryTime)
			{
				cState.downSpikeRecovery = false;
			}
			downSpikeRecoveryTimer += Time.deltaTime;
		}
		if (cState.downSpikeAntic)
		{
			if (downSpikeTimer >= Config.DownSpikeAnticTime)
			{
				if (Config.DownspikeBurstEffect)
				{
					downspikeBurstPrefab.SetActive(value: true);
				}
				downSpikeTimer = 0f;
				cState.downSpikeAntic = false;
				cState.downSpiking = true;
				allowAttackCancellingDownspikeRecovery = false;
				currentDownspike.StartSlash();
			}
			else
			{
				downSpikeTimer += Time.deltaTime;
			}
		}
		if (refillSoundSuppressFramesLeft > 0)
		{
			refillSoundSuppressFramesLeft--;
		}
		if (evadingDidClash && !cState.evading)
		{
			evadingDidClash = false;
		}
		if (!gm.isPaused && !playerData.isInventoryOpen)
		{
			if (animCtrl.IsPlayingUpdraftAnim)
			{
				audioCtrl.PlaySound(HeroSounds.UPDRAFT_IDLE);
			}
			else
			{
				audioCtrl.StopSound(HeroSounds.UPDRAFT_IDLE);
			}
			if (animCtrl.IsPlayingWindyAnim)
			{
				audioCtrl.PlaySound(HeroSounds.WINDY_IDLE);
			}
			else
			{
				audioCtrl.StopSound(HeroSounds.WINDY_IDLE);
			}
			if (inputHandler.inputActions.Attack.IsPressed && CanNailCharge())
			{
				cState.nailCharging = true;
				nailChargeTimer += Time.deltaTime;
			}
			else if (cState.nailCharging || nailChargeTimer != 0f)
			{
				CancelNailCharge();
			}
			float currentNailChargeTime = CurrentNailChargeTime;
			if (cState.nailCharging && nailChargeTimer > CurrentNailChargeBeginTime && !artChargeEffect.activeSelf && nailChargeTimer < currentNailChargeTime)
			{
				artChargeEffect.SetActive(value: true);
				audioCtrl.PlaySound(HeroSounds.NAIL_ART_CHARGE, playVibration: true);
			}
			if (artChargeEffect.activeSelf && (!cState.nailCharging || nailChargeTimer > currentNailChargeTime))
			{
				StopNailChargeEffects();
			}
			if (!artChargedEffect.activeSelf && nailChargeTimer >= currentNailChargeTime)
			{
				artChargedEffect.SetActive(value: true);
				artChargedEffectAnim.PlayFromFrame(0);
				GameCameras.instance.cameraShakeFSM.SendEvent("EnemyKillShake");
				audioSource.PlayOneShot(nailArtChargeComplete, 1f);
				audioCtrl.PlaySound(HeroSounds.NAIL_ART_READY, playVibration: true);
				spriteFlash.flashFocusGet();
				cState.nailCharging = true;
				GameCameras.instance.cameraController.ScreenFlash(new Color(1f, 1f, 1f, 0.3f));
			}
			if (artChargedEffect.activeSelf && (nailChargeTimer < currentNailChargeTime || !cState.nailCharging))
			{
				artChargedEffect.SetActive(value: false);
				audioCtrl.StopSound(HeroSounds.NAIL_ART_READY);
			}
			if (gm.GameState == GameState.CUTSCENE)
			{
				ResetAllCrestStateMinimal(clearHunterState: false);
			}
			else if (gm.GameState == GameState.PLAYING)
			{
				if (!isSilkRegenBlocked && hasSilkSpoolAppeared && !cState.dead)
				{
					if (silkRegenDelayLeft > 0f)
					{
						silkRegenDelayLeft -= Time.deltaTime;
						if (silkRegenDelayLeft <= 0f)
						{
							StartSilkRegen();
						}
					}
					if (silkRegenDurationLeft > 0f)
					{
						silkRegenDurationLeft -= Time.deltaTime;
						if (silkRegenDurationLeft <= 0f)
						{
							DoSilkRegen();
						}
					}
				}
				if (warriorState.IsInRageMode)
				{
					warriorState.RageEffectTimeLeft -= Time.deltaTime;
					if (warriorState.RageEffectTimeLeft <= 0f)
					{
						ResetWarriorRageEffect();
					}
					warriorState.RageHealTimeLeft -= Time.deltaTime;
					if (warriorState.RageHealTimeLeft <= 0f)
					{
						ResetWarriorCrestState();
					}
				}
				if (reaperState.IsInReaperMode)
				{
					reaperState.ReaperModeDurationLeft -= Time.deltaTime;
					if (reaperState.ReaperModeDurationLeft <= 0f)
					{
						ResetReaperCrestState();
					}
				}
				if (silkPartsTimeLeft > 0f)
				{
					silkPartsTimeLeft -= Time.deltaTime;
					if (silkPartsTimeLeft <= 0f)
					{
						playerData.silkParts = 0;
						gm.gameCams.silkSpool.RefreshSilk();
					}
				}
				if (IsUsingQuickening)
				{
					quickeningTimeLeft -= Time.deltaTime;
					if (quickeningTimeLeft <= 0f || playerData.atBench || ToolItemManager.ActiveState != ToolsActiveStates.Active)
					{
						StopQuickening();
					}
				}
				if (parryInvulnTimer > 0f)
				{
					parryInvulnTimer -= Time.deltaTime;
				}
				if (revengeWindowTimer > 0f)
				{
					revengeWindowTimer -= Time.deltaTime;
				}
				if (wandererDashComboWindowTimer > 0f)
				{
					wandererDashComboWindowTimer -= Time.deltaTime;
				}
				bool flag2 = !cState.hazardDeath && !cState.hazardRespawning && !cState.isBinding;
				TickFrostEffect(flag2);
				if (flag2)
				{
					TickSilkEat(playerData.silk > 0 && cState.isMaggoted && !cState.swimming, ref maggotedSilkTracker, SilkSpool.SilkUsingFlags.Maggot, SilkSpool.SilkTakeSource.Normal, MAGGOTED_SILK_EAT_DELAY_FIRST, MAGGOTED_SILK_EAT_DELAY, MAGGOTED_SILK_EAT_DURATION);
				}
				if (!cState.dead && lavaBellCooldownTimeLeft > 0f)
				{
					if (Gameplay.LavaBellTool.Status.IsEquipped)
					{
						float num2 = Gameplay.LavaBellCooldownTime - lavaBellCooldownTimeLeft;
						lavaBellCooldownTimeLeft -= Time.deltaTime;
						float num3 = Gameplay.LavaBellCooldownTime - lavaBellCooldownTimeLeft;
						float num4 = Gameplay.LavaBellCooldownTime - 1.25f;
						if (num2 < num4 && num3 >= num4)
						{
							spawnedLavaBellRechargeEffect.SetActive(value: true);
							EventRegister.SendEvent(EventRegisterEvents.LavaBellRecharging);
						}
						if (lavaBellCooldownTimeLeft <= 0f)
						{
							spriteFlash.FlashLavaBellRecharge();
						}
					}
					else
					{
						ResetLavaBell();
					}
				}
				tagDamageTaker.Tick(canTakeDamage: true);
				if (silkTauntEffectTimeLeft > 0f)
				{
					silkTauntEffectTimeLeft -= Time.deltaTime;
				}
				if (ringTauntEffectTimeLeft > 0f)
				{
					ringTauntEffectTimeLeft -= Time.deltaTime;
				}
				if (InteractManager.BlockingInteractable == null)
				{
					TickDeliveryItems();
				}
				if ((bool)runningWaterEffect)
				{
					if (enviroRegionListener.CurrentEnvironmentType == EnvironmentTypes.RunningWater && cState.onGround)
					{
						if (!runningWaterEffect.isPlaying)
						{
							if ((bool)areaEffectTint)
							{
								areaEffectTint.DoTint();
							}
							runningWaterEffect.Play(withChildren: true);
						}
					}
					else if (runningWaterEffect.isPlaying)
					{
						runningWaterEffect.Stop(withChildren: true);
					}
				}
			}
		}
		if ((gm.isPaused || playerData.isInventoryOpen) && !inputHandler.inputActions.Attack.IsPressed)
		{
			CancelNailCharge();
		}
		if (NoClamberRegion.IsClamberBlocked || cState.onGround || (controlReqlinquished && !allowMantle) || cState.attacking || cState.upAttacking || cState.downAttacking || cState.wallSliding || cState.doubleJumping || cState.downSpikeBouncing || cState.hazardDeath || cState.hazardRespawning || cState.recoilFrozen || cState.recoiling || gm.GameState != GameState.PLAYING || !(rb2d.linearVelocity.y < 5f) || SlideSurface.IsHeroInside || CheckTouchingGround() || (((!inputHandler.inputActions.Left.IsPressed && (!(velocity_prev.x < -0.1f) || !inputHandler.inputActions.Dash.IsPressed)) || !CheckStillTouchingWall(CollisionSide.left, checkTop: false, checkNonSliders: false)) && ((!inputHandler.inputActions.Right.IsPressed && (!(velocity_prev.x > 0.1f) || !inputHandler.inputActions.Dash.IsPressed)) || !CheckStillTouchingWall(CollisionSide.right, checkTop: false, checkNonSliders: false))))
		{
			return;
		}
		if (!wallLocked && !cState.dashing)
		{
			if (inputHandler.inputActions.Left.IsPressed)
			{
				FaceLeft();
			}
			else if (inputHandler.inputActions.Right.IsPressed)
			{
				FaceRight();
			}
		}
		mantleFSM.SendEvent("AIR MANTLE");
	}

	public void SetIsMaggoted(bool value)
	{
		bool isMaggoted = cState.isMaggoted;
		cState.isMaggoted = value;
		if (value)
		{
			if (!spawnedMaggotEffect && (bool)maggotEffectPrefab)
			{
				spawnedMaggotEffect = maggotEffectPrefab.Spawn();
				spawnedMaggotEffect.transform.SetParent(transform, worldPositionStays: true);
				spawnedMaggotEffect.transform.SetLocalPosition2D(Vector2.zero);
				spawnedMaggotEffect.PlayParticleSystems();
			}
			if (!spriteFlash.IsFlashing(repeating: true, maggotedFlash))
			{
				maggotedFlash = spriteFlash.FlashingMaggot();
			}
			StatusVignette.AddStatus(StatusVignette.StatusTypes.Maggoted);
			if (!isMaggoted)
			{
				Effects.BeginMaggotedSound.SpawnAndPlayOneShot(transform.position);
			}
			if ((bool)spawnedMaggotEnter)
			{
				spawnedMaggotEnter.SetActive(value: false);
				spawnedMaggotEnter.SetActive(value: true);
			}
			ResetAllCrestState();
		}
		else
		{
			if ((bool)spawnedMaggotEffect)
			{
				spawnedMaggotEffect.StopParticleSystems();
				spawnedMaggotEffect = null;
			}
			if (spriteFlash.IsFlashing(repeating: true, maggotedFlash))
			{
				spriteFlash.CancelRepeatingFlash(maggotedFlash);
				maggotedFlash = default(SpriteFlash.FlashHandle);
			}
			if (isMaggoted)
			{
				StatusVignette.RemoveStatus(StatusVignette.StatusTypes.Maggoted);
			}
		}
		EventRegister.SendEvent(EventRegisterEvents.MaggotCheck);
	}

	public void AddToMaggotCharmTimer(float delta)
	{
		if (playerData.MaggotCharmHits < 3)
		{
			maggotCharmTimer += delta;
			float maggotCharmHealthLossTime = Gameplay.MaggotCharmHealthLossTime;
			if (!(maggotCharmTimer < maggotCharmHealthLossTime))
			{
				maggotCharmTimer %= maggotCharmHealthLossTime;
				playerData.MaggotCharmHits++;
				EventRegister.SendEvent(EventRegisterEvents.MaggotCheck);
			}
		}
	}

	public void DidMaggotCharmHit()
	{
		maggotCharmTimer = 0f;
		playerData.MaggotCharmHits++;
		EventRegister.SendEvent(EventRegisterEvents.MaggotCheck);
		GameObject maggotCharmHitSinglePrefab = Gameplay.MaggotCharmHitSinglePrefab;
		if ((bool)maggotCharmHitSinglePrefab)
		{
			Vector3 position = transform.position;
			position.z = maggotCharmHitSinglePrefab.transform.position.z;
			maggotCharmHitSinglePrefab.Spawn(position);
		}
	}

	private void TickFrostEffect(bool shouldTickInto)
	{
		if (cState.dead && !cState.isFrostDeath)
		{
			return;
		}
		float num = (cState.isFrostDeath ? 100f : GetTotalFrostSpeed());
		bool flag = playerData.hasDoubleJump;
		if (num > Mathf.Epsilon)
		{
			if (!shouldTickInto)
			{
				return;
			}
			num *= 1f;
			if (Config.ForceBareInventory)
			{
				num *= 2f;
				flag = false;
			}
		}
		else
		{
			num -= 100f;
		}
		float num2 = num / 100f;
		float value = frostAmount + num2 * Time.deltaTime;
		if (flag)
		{
			frostAmount = Mathf.Clamp(value, 0f, 0.035f);
			this.FrostAmountUpdated?.Invoke(0f);
		}
		else
		{
			frostAmount = Mathf.Clamp01(value);
			this.FrostAmountUpdated?.Invoke(frostAmount);
		}
		StatusVignette.SetFrostVignetteAmount(frostAmount);
		if (cState.dead)
		{
			return;
		}
		bool isFrosted = cState.isFrosted;
		cState.isFrosted = !flag && frostAmount >= 1f - Mathf.Epsilon;
		bool flag2 = isInFrostRegion;
		isInFrostRegion = num2 > Mathf.Epsilon;
		bool inFrostRegion = cState.inFrostRegion;
		cState.inFrostRegion = !flag && isInFrostRegion;
		if (cState.isFrosted != isFrosted || cState.inFrostRegion != inFrostRegion)
		{
			EventRegister.SendEvent(EventRegisterEvents.FrostUpdateHealth);
		}
		if (cState.inFrostRegion)
		{
			if (!spriteFlash.IsFlashing(repeating: true, frostRegionFlash))
			{
				frostRegionFlash = spriteFlash.FlashingFrosted();
			}
			if (!frostedEffect.IsAlive())
			{
				frostedEffect.PlayParticleSystems();
			}
		}
		else
		{
			if (spriteFlash.IsFlashing(repeating: true, frostRegionFlash))
			{
				spriteFlash.CancelRepeatingFlash(frostRegionFlash);
				frostRegionFlash = default(SpriteFlash.FlashHandle);
			}
			if (frostedEffect.IsAlive())
			{
				frostedEffect.StopParticleSystems();
			}
		}
		if (isInFrostRegion)
		{
			if (!flag2 && (bool)spawnedFrostEnter)
			{
				spawnedFrostEnter.SetActive(value: true);
			}
		}
		else if (flag2 && num < gm.sm.FrostSpeed - 100f - Mathf.Epsilon && (bool)spawnedHeatEnter)
		{
			spawnedHeatEnter.SetActive(value: true);
		}
		if (cState.isFrosted)
		{
			if (!spriteFlash.IsFlashing(repeating: true, frostAnticFlash))
			{
				frostAnticFlash = spriteFlash.FlashingFrostAntic();
			}
			if (!frostedAudioLoop.isPlaying || frostedFadeOutRoutine != null)
			{
				if (frostedFadeOutRoutine != null)
				{
					StopCoroutine(frostedFadeOutRoutine);
					frostedFadeOutRoutine = null;
				}
				frostedEnterAudio.SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, frostedAudioLoop.transform.position);
				frostedAudioLoop.volume = 1f;
				frostedAudioLoop.Play();
			}
			frostDamageTimer += Time.deltaTime;
			if (frostDamageTimer >= 1.75f)
			{
				frostDamageTimer %= 1.75f;
				TakeFrostDamage(1);
				spriteFlash.Flash(new Color(0.6f, 0.8f, 1f), 0.9f, 0.1f, 0.01f, 0.1f);
			}
			return;
		}
		frostDamageTimer -= Time.deltaTime;
		if (frostDamageTimer < 0f)
		{
			frostDamageTimer = 0f;
		}
		if (spriteFlash.IsFlashing(repeating: true, frostAnticFlash))
		{
			spriteFlash.CancelRepeatingFlash(frostAnticFlash);
			frostAnticFlash = default(SpriteFlash.FlashHandle);
		}
		if (frostedAudioLoop.isPlaying && frostedFadeOutRoutine == null)
		{
			frostedFadeOutRoutine = this.StartTimerRoutine(0f, frostedAudioLoopFadeOut, delegate(float t)
			{
				frostedAudioLoop.volume = 1f - t;
			}, null, delegate
			{
				frostedAudioLoop.Stop();
				frostedFadeOutRoutine = null;
			});
		}
	}

	public void SpawnDeliveryItemEffect(DeliveryQuestItem deliveryQuestItem)
	{
		if (!(deliveryQuestItem == null) && !spawnedDeliveryEffects.ContainsKey(deliveryQuestItem))
		{
			GameObject value = null;
			if ((bool)deliveryQuestItem.HeroLoopEffect)
			{
				value = deliveryQuestItem.HeroLoopEffect.Spawn(transform, Vector3.zero);
			}
			spawnedDeliveryEffects[deliveryQuestItem] = value;
		}
	}

	public void RemoveDeliveryItemEffect(DeliveryQuestItem deliveryQuestItem)
	{
		if (!(deliveryQuestItem == null) && spawnedDeliveryEffects.TryGetValue(deliveryQuestItem, out var value))
		{
			if (value != null)
			{
				UnityEngine.Object.Destroy(value);
			}
			spawnedDeliveryEffects.Remove(deliveryQuestItem);
		}
	}

	public void SetupDeliveryItems()
	{
		if (currentTimedDeliveries == null)
		{
			currentTimedDeliveries = new List<DeliveryTimer>();
		}
		currentTimedDeliveries.Clear();
		foreach (DeliveryQuestItem.ActiveItem activeItem in DeliveryQuestItem.GetActiveItems())
		{
			float chunkDuration = activeItem.Item.GetChunkDuration(activeItem.MaxCount);
			if (!(chunkDuration <= Mathf.Epsilon))
			{
				SpawnDeliveryItemEffect(activeItem.Item);
				currentTimedDeliveries.Add(new DeliveryTimer
				{
					Item = activeItem,
					TimeLeft = chunkDuration
				});
			}
		}
		CleanSpawnedDeliveryEffects();
	}

	private void TickDeliveryItems()
	{
		if (currentTimedDeliveries == null)
		{
			return;
		}
		for (int num = currentTimedDeliveries.Count - 1; num >= 0; num--)
		{
			DeliveryTimer value = currentTimedDeliveries[num];
			value.TimeLeft -= Time.deltaTime;
			if (value.TimeLeft > 0f)
			{
				currentTimedDeliveries[num] = value;
			}
			else
			{
				value.Item.CurrentCount = value.Item.Item.CollectedAmount;
				DeliveryQuestItem.TakeHitForItem(value.Item, hitEffect: false);
			}
		}
	}

	public IEnumerable<DeliveryTimer> GetDeliveryTimers()
	{
		if (currentTimedDeliveries == null)
		{
			return Enumerable.Empty<DeliveryTimer>();
		}
		return currentTimedDeliveries;
	}

	private float GetTotalFrostSpeed()
	{
		if (CheatManager.IsFrostDisabled)
		{
			return 0f;
		}
		float num = gm.sm.FrostSpeed;
		foreach (FrostRegion frostRegion in FrostRegion.FrostRegions)
		{
			if (frostRegion.IsInside)
			{
				num += frostRegion.FrostSpeed;
			}
		}
		if (NailImbuement.CurrentElement == NailElements.Fire)
		{
			num *= 0.7f;
		}
		if (warriorState.IsInRageMode)
		{
			num *= 0.9f;
		}
		if (Gameplay.WispLanternTool.Status.IsEquipped)
		{
			num *= 0.8f;
		}
		return num;
	}

	private void TickSilkEat(bool shouldEatSilk, ref SilkEatTracker tracker, SilkSpool.SilkUsingFlags usingType, SilkSpool.SilkTakeSource takeSource, float firstDelay, float delay, float duration)
	{
		if (shouldEatSilk)
		{
			if (tracker.EatDurationLeft <= 0f)
			{
				if (tracker.EatDelayLeft <= 0f)
				{
					tracker.EatDelayLeft = firstDelay;
				}
				else
				{
					tracker.EatDelayLeft -= Time.deltaTime;
				}
				if (tracker.EatDelayLeft <= 0f)
				{
					tracker.EatDurationLeft = duration;
					SilkSpool.Instance.AddUsing(usingType);
				}
			}
			else
			{
				if (tracker.EatSilkCount != playerData.silk)
				{
					tracker.EatDurationLeft = duration;
				}
				else
				{
					tracker.EatDurationLeft -= Time.deltaTime;
				}
				if (tracker.EatDurationLeft <= 0f)
				{
					SilkSpool.Instance.RemoveUsing(usingType);
					TakeSilk(1, takeSource);
					tracker.EatDurationLeft = 0f;
					tracker.EatDelayLeft = ((playerData.silk > 0) ? delay : 0f);
				}
			}
		}
		else
		{
			if (tracker.EatDurationLeft > 0f)
			{
				SilkSpool.Instance.RemoveUsing(usingType);
			}
			tracker.EatDelayLeft = 0f;
			tracker.EatDurationLeft = 0f;
		}
		tracker.EatSilkCount = playerData.silk;
	}

	public void ShuttleCockCancel()
	{
		if (cState.shuttleCock && !cState.onGround)
		{
			AddExtraAirMoveVelocity(new DecayingVelocity
			{
				Velocity = new Vector2(shuttlecockSpeed, 0f),
				Decay = 5f,
				CancelOnTurn = true,
				SkipBehaviour = DecayingVelocity.SkipBehaviours.WhileMoving
			});
		}
		ShuttleCockCancelInert();
	}

	private void ShuttleCockCancelInert()
	{
		cState.shuttleCock = false;
		if ((bool)shuttleCockJumpAudio)
		{
			shuttleCockJumpAudio.Stop();
		}
		vibrationCtrl.StopShuttlecock();
	}

	private void FixedUpdate()
	{
		if (cState.recoilingLeft || cState.recoilingRight)
		{
			if (recoilStepsLeft > 0)
			{
				recoilStepsLeft--;
			}
			else
			{
				CancelRecoilHorizontal();
			}
		}
		for (int num = extraAirMoveVelocities.Count - 1; num >= 0; num--)
		{
			DecayingVelocity value = extraAirMoveVelocities[num];
			value.Velocity -= value.Velocity * (value.Decay * Time.deltaTime);
			if (value.Velocity.magnitude < 0.01f)
			{
				extraAirMoveVelocities.RemoveAt(num);
			}
			else
			{
				extraAirMoveVelocities[num] = value;
			}
		}
		if (cState.dead)
		{
			rb2d.linearVelocity = new Vector2(0f, 0f);
		}
		UpdateSteepSlopes();
		if ((hero_state == ActorStates.hard_landing && !cState.onConveyor) || hero_state == ActorStates.dash_landing)
		{
			ResetMotion(resetNailCharge: false);
			UpdateEdgeAdjust();
		}
		else if (hero_state == ActorStates.no_input)
		{
			didCheckEdgeAdjust = false;
			if (cState.transitioning)
			{
				if (transitionState == HeroTransitionState.EXITING_SCENE)
				{
					if (transition_vel.y > Mathf.Epsilon || (cState.onGround && transition_vel.magnitude > Mathf.Epsilon))
					{
						AffectedByGravity(gravityApplies: false);
					}
					if (!stopWalkingOut)
					{
						rb2d.linearVelocity = new Vector2(transition_vel.x, transition_vel.y + rb2d.linearVelocity.y);
					}
				}
				else if (transitionState == HeroTransitionState.ENTERING_SCENE)
				{
					rb2d.linearVelocity = transition_vel;
					if (transition_vel.x > 0f)
					{
						CheckForBump(CollisionSide.right);
					}
					else if (transition_vel.x < 0f)
					{
						CheckForBump(CollisionSide.left);
					}
				}
				else if (transitionState == HeroTransitionState.DROPPING_DOWN)
				{
					rb2d.linearVelocity = new Vector2(transition_vel.x, rb2d.linearVelocity.y);
				}
			}
			else if (cState.recoiling)
			{
				AffectedByGravity(gravityApplies: false);
				rb2d.linearVelocity = recoilVector;
			}
		}
		else if (hero_state != ActorStates.no_input)
		{
			if (cState.transitioning)
			{
				return;
			}
			DoMovement(acceptingInput);
			if ((cState.lookingUp || cState.lookingDown) && Mathf.Abs(move_input) > 0.6f)
			{
				ResetLook();
			}
			if (cState.jumping && !cState.dashing && !cState.isSprinting)
			{
				Jump();
			}
			if (cState.doubleJumping)
			{
				DoubleJump();
			}
			if (cState.dashing)
			{
				Dash();
			}
			if (cState.downSpiking)
			{
				Downspike();
			}
			if (cState.floating)
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, FLOAT_SPEED);
				ResetHardLandingTimer();
			}
			if (cState.casting)
			{
				if (cState.castRecoiling)
				{
					if (cState.facingRight)
					{
						rb2d.linearVelocity = new Vector2(0f - CAST_RECOIL_VELOCITY, 0f);
					}
					else
					{
						rb2d.linearVelocity = new Vector2(CAST_RECOIL_VELOCITY, 0f);
					}
				}
				else
				{
					rb2d.linearVelocity = Vector2.zero;
				}
			}
			if (cState.bouncing)
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, BOUNCE_VELOCITY);
			}
			_ = cState.shroomBouncing;
			if (cState.downSpikeBouncing)
			{
				if (downspike_rebound_steps <= DOWNSPIKE_REBOUND_STEPS)
				{
					if (downspike_rebound_xspeed != 0f)
					{
						rb2d.linearVelocity = new Vector2(downspike_rebound_xspeed, rb2d.linearVelocity.y);
					}
					downspike_rebound_steps++;
				}
				else
				{
					cState.downSpikeBouncing = false;
				}
			}
			if (wallJumpChainStepsLeft > 0)
			{
				wallJumpChainStepsLeft--;
			}
			if (wallLocked)
			{
				if (wallJumpedR)
				{
					rb2d.linearVelocity = new Vector2(currentWalljumpSpeed, rb2d.linearVelocity.y);
				}
				else if (wallJumpedL)
				{
					rb2d.linearVelocity = new Vector2(0f - currentWalljumpSpeed, rb2d.linearVelocity.y);
				}
				wallLockSteps++;
				if (wallLockSteps > WJLOCK_STEPS_LONG)
				{
					wallLocked = false;
					wallJumpChainStepsLeft = WJLOCK_CHAIN_STEPS;
				}
				currentWalljumpSpeed -= walljumpSpeedDecel;
			}
			if (cState.wallSliding)
			{
				HeroActions inputActions = inputHandler.inputActions;
				bool flag = false;
				if (wallSlidingL)
				{
					if (inputActions.Right.IsPressed && !inputActions.Left.IsPressed)
					{
						flag = true;
					}
				}
				else if (wallSlidingR && inputActions.Left.IsPressed && !inputActions.Right.IsPressed)
				{
					flag = true;
				}
				if (flag)
				{
					wallUnstickSteps++;
				}
				else
				{
					wallUnstickSteps = 0;
				}
				if (wallUnstickSteps >= WALL_STICKY_STEPS)
				{
					FlipSprite();
					CancelWallsliding();
				}
				if (wallSlidingL)
				{
					if (!CheckStillTouchingWall(CollisionSide.left))
					{
						FlipSprite();
						CancelWallsliding();
					}
				}
				else if (wallSlidingR && !CheckStillTouchingWall(CollisionSide.right))
				{
					FlipSprite();
					CancelWallsliding();
				}
			}
			if (hero_state == ActorStates.running)
			{
				if (move_input > 0f)
				{
					CheckForBump(CollisionSide.right);
				}
				else if (move_input < 0f)
				{
					CheckForBump(CollisionSide.left);
				}
			}
		}
		if (cState.downSpikeAntic && Config.DownspikeThrusts && downSpikeTimer < Config.DownSpikeAnticTime)
		{
			rb2d.linearVelocity *= DOWNSPIKE_ANTIC_DECELERATION;
		}
		float maxFallVelocity = GetMaxFallVelocity();
		if (rb2d.linearVelocity.y < 0f - maxFallVelocity && (ForceClampTerminalVelocity || cState.isBackSprinting || cState.isBackScuttling || (!controlReqlinquished && !cState.shadowDashing && !cState.spellQuake)))
		{
			rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, 0f - maxFallVelocity);
		}
		if (jumpQueuing)
		{
			jumpQueueSteps++;
		}
		if (doubleJumpQueuing)
		{
			doubleJumpQueueSteps++;
		}
		if (dashQueuing)
		{
			dashQueueSteps++;
		}
		if (attackQueuing)
		{
			attackQueueSteps++;
		}
		if (harpoonQueuing)
		{
			harpoonQueueSteps++;
		}
		if (toolThrowQueueing)
		{
			toolThrowQueueSteps++;
		}
		if (doMaxSilkRegen)
		{
			maxSilkRegenTimer += Time.deltaTime;
			if (maxSilkRegenTimer >= 0.03f)
			{
				if (playerData.CurrentSilkRegenMax - playerData.silk > 0)
				{
					maxSilkRegenTimer %= 0.03f;
					AddSilk(1, heroEffect: false);
				}
				if (playerData.CurrentSilkRegenMax - playerData.silk <= 0)
				{
					maxSilkRegenTimer = 0f;
					doMaxSilkRegen = false;
				}
			}
		}
		if (cState.superDashOnWall && !cState.onConveyorV)
		{
			rb2d.linearVelocity = new Vector3(0f, 0f);
		}
		if (cState.onConveyor && (cState.onGround || cState.isSprinting || hero_state == ActorStates.hard_landing))
		{
			if (cState.freezeCharge || hero_state == ActorStates.hard_landing || (controlReqlinquished && !cState.isSprinting && !cState.isBackScuttling && !cState.isBackSprinting))
			{
				rb2d.linearVelocity = new Vector3(0f, 0f);
			}
			if (this.BeforeApplyConveyorSpeed != null)
			{
				this.BeforeApplyConveyorSpeed(rb2d.linearVelocity);
			}
			Vector2 linearVelocity = rb2d.linearVelocity;
			rb2d.linearVelocity = new Vector2(linearVelocity.x + conveyorSpeed, linearVelocity.y);
		}
		if (cState.inConveyorZone)
		{
			if (cState.freezeCharge || hero_state == ActorStates.hard_landing)
			{
				rb2d.linearVelocity = new Vector3(0f, 0f);
			}
			float num2 = conveyorSpeed;
			if (cState.isSprinting)
			{
				num2 *= 2f;
			}
			Vector2 position = rb2d.position;
			position.x += num2 * Time.fixedDeltaTime;
			rb2d.position = position;
		}
		if (cState.shuttleCock)
		{
			if (cState.onGround && rb2d.linearVelocity.y <= 0f)
			{
				ShuttleCockCancel();
				if (inputHandler.inputActions.Dash.IsPressed)
				{
					sprintFSM.SendEvent("TRY SPRINT");
				}
				else
				{
					ForceSoftLanding();
				}
			}
			else
			{
				rb2d.linearVelocity = new Vector3(shuttlecockSpeed, rb2d.linearVelocity.y);
			}
		}
		if (!didAirHang && !cState.onGround && rb2d.linearVelocity.y < 0f)
		{
			if (!controlReqlinquished && transitionState == HeroTransitionState.WAITING_TO_TRANSITION)
			{
				StartAirHang();
			}
			didAirHang = true;
		}
		if (cState.onGround)
		{
			if (didAirHang)
			{
				didAirHang = false;
			}
			if (airDashed)
			{
				airDashed = false;
			}
		}
		if (rb2d.gravityScale < DEFAULT_GRAVITY && !controlReqlinquished && hero_state != ActorStates.no_input && !cState.wallSliding)
		{
			rb2d.gravityScale += AIR_HANG_ACCEL * Time.deltaTime;
		}
		if (rb2d.gravityScale < DEFAULT_GRAVITY && !inputHandler.inputActions.Jump.IsPressed && !controlReqlinquished && hero_state != ActorStates.no_input && !cState.wallSliding)
		{
			rb2d.gravityScale = DEFAULT_GRAVITY;
		}
		if (rb2d.gravityScale > DEFAULT_GRAVITY)
		{
			rb2d.gravityScale = DEFAULT_GRAVITY;
		}
		velocity_crt = rb2d.linearVelocity;
		velocity_prev = velocity_crt;
		if ((tryShove || (cState.falling && cState.wasOnGround)) && ((!cState.onGround && velocity_crt == Vector2.zero && hero_state != ActorStates.dash_landing) || (dashingDown && !onFlatGround)) && !cState.hazardRespawning && !cState.transitioning && !playerData.atBench)
		{
			ShoveOff();
		}
		tryShove = false;
		onFlatGround = false;
		if (landingBufferSteps > 0)
		{
			landingBufferSteps--;
		}
		if (ledgeBufferSteps > 0)
		{
			ledgeBufferSteps--;
		}
		if (sprintBufferSteps > 0)
		{
			sprintBufferSteps--;
		}
		if (shuttleCockJumpSteps > 0)
		{
			shuttleCockJumpSteps--;
		}
		if (headBumpSteps > 0)
		{
			headBumpSteps--;
		}
		if (jumpReleaseQueueSteps > 0)
		{
			jumpReleaseQueueSteps--;
		}
		if (cState.downspikeInvulnerabilitySteps > 0)
		{
			cState.downspikeInvulnerabilitySteps--;
		}
		positionHistory[1] = positionHistory[0];
		positionHistory[0] = transform.position;
		cState.wasOnGround = cState.onGround;
	}

	private void ShoveOff()
	{
		float num = Mathf.Sign(transform.localScale.x) * 0.2f;
		Vector3 position = transform.position;
		transform.position = new Vector3(position.x + num, position.y, position.z);
	}

	public void UpdateMoveInput()
	{
		move_input = inputHandler.inputActions.MoveVector.Vector.x;
	}

	public void DoMovement(bool useInput)
	{
		if (CheatManager.IsOpen)
		{
			move_input = 0f;
		}
		if (cState.backDashing || cState.dashing)
		{
			return;
		}
		Move(move_input, useInput);
		UpdateEdgeAdjust();
		if ((!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.wallSliding && !wallLocked && !cState.shuttleCock && !cState.isToolThrowing && TrySetCorrectFacing())
		{
			if (Config.CanTurnWhileSlashing)
			{
				CancelAttackNotSlash(resetNailCharge: false);
			}
			else
			{
				CancelAttack(resetNailCharge: false);
			}
		}
		DoRecoilMovement();
	}

	public void DoRecoilMovement()
	{
		if (cState.recoilingLeft)
		{
			Vector2 linearVelocity = rb2d.linearVelocity;
			rb2d.linearVelocity = ((linearVelocity.x - recoilVelocity > 0f - recoilVelocity) ? new Vector2(0f - recoilVelocity, linearVelocity.y) : new Vector2(linearVelocity.x - recoilVelocity, linearVelocity.y));
		}
		if (cState.recoilingRight)
		{
			Vector2 linearVelocity2 = rb2d.linearVelocity;
			rb2d.linearVelocity = ((linearVelocity2.x + recoilVelocity < recoilVelocity) ? new Vector2(recoilVelocity, linearVelocity2.y) : new Vector2(linearVelocity2.x + recoilVelocity, linearVelocity2.y));
		}
	}

	public void ConveyorReset()
	{
		cState.inConveyorZone = false;
		conveyorSpeed = 0f;
		cState.onConveyor = false;
		cState.onConveyorV = false;
	}

	public void SetBlockSteepSlopes(bool blocked)
	{
		blockSteepSlopes = blocked;
		if (blocked)
		{
			cState.isTouchingSlopeLeft = false;
			cState.isTouchingSlopeRight = false;
		}
	}

	private static bool IsSteepSlopeRayHitting(Vector2 heroCentre, Vector2 heroMin, Vector2 heroMax, float rayPadding, int layerMask, bool checkLeft, out RaycastHit2D hit)
	{
		int num = Physics2D.RaycastNonAlloc(new Vector2(heroCentre.x, heroMin.y + Physics2D.defaultContactOffset), checkLeft ? Vector2.left : Vector2.right, distance: (checkLeft ? (heroCentre.x - heroMin.x) : (heroMax.x - heroCentre.x)) + rayPadding, results: _rayHitStore, layerMask: layerMask);
		bool flag = false;
		RaycastHit2D raycastHit2D = default(RaycastHit2D);
		hit = default(RaycastHit2D);
		for (int i = 0; i < num; i++)
		{
			hit = _rayHitStore[i];
			Collider2D collider = hit.collider;
			if (!collider.isTrigger && SteepSlope.IsSteepSlope(collider))
			{
				if (!flag || hit.distance < raycastHit2D.distance)
				{
					raycastHit2D = hit;
				}
				flag = true;
			}
			_rayHitStore[i] = default(RaycastHit2D);
		}
		return flag;
	}

	private void UpdateSteepSlopes()
	{
		if (blockSteepSlopes)
		{
			return;
		}
		cState.isTouchingSlopeLeft = false;
		cState.isTouchingSlopeRight = false;
		ActorStates actorStates = hero_state;
		bool flag = actorStates == ActorStates.running || actorStates == ActorStates.airborne;
		Vector2 linearVelocity = rb2d.linearVelocity;
		Bounds bounds = col2d.bounds;
		Vector3 min = bounds.min;
		Vector3 max = bounds.max;
		Vector3 center = bounds.center;
		float rayPadding = Mathf.Abs(linearVelocity.x) * Time.fixedDeltaTime + 0.05f;
		bool flag2;
		if (IsSteepSlopeRayHitting(center, min, max, rayPadding, 33562880, checkLeft: true, out var hit))
		{
			flag2 = true;
		}
		else
		{
			if (!IsSteepSlopeRayHitting(center, min, max, rayPadding, 33562880, checkLeft: false, out hit))
			{
				return;
			}
			flag2 = false;
		}
		Vector2 normal = hit.normal;
		if (normal.y < 0f)
		{
			return;
		}
		cState.isTouchingSlopeLeft = flag2;
		cState.isTouchingSlopeRight = !flag2;
		if (flag)
		{
			float f = normal.y / normal.x;
			float num = Physics2D.gravity.y * Mathf.Abs(f) * 4f;
			linearVelocity.y += num * Time.fixedDeltaTime;
			rb2d.linearVelocity = linearVelocity;
			if (cState.shuttleCock)
			{
				ShuttleCockCancelInert();
			}
			ResetHardLandingTimer();
		}
	}

	private void UpdateEdgeAdjust()
	{
		if (cState.onGround && (hero_state == ActorStates.idle || hero_state == ActorStates.hard_landing))
		{
			if (!didCheckEdgeAdjust && (!cState.falling || controlReqlinquished || rb2d.linearVelocity.y != 0f || wallSlidingL || wallSlidingR))
			{
				DoEdgeAdjust();
				didCheckEdgeAdjust = true;
			}
		}
		else
		{
			didCheckEdgeAdjust = false;
		}
	}

	public bool GetWillThrowTool(bool reportFailure)
	{
		AttackToolBinding usedBinding;
		if (inputHandler.inputActions.Up.IsPressed)
		{
			willThrowTool = ToolItemManager.GetBoundAttackTool(AttackToolBinding.Up, ToolEquippedReadSource.Active, out usedBinding);
		}
		else if (inputHandler.inputActions.Down.IsPressed)
		{
			willThrowTool = ToolItemManager.GetBoundAttackTool(AttackToolBinding.Down, ToolEquippedReadSource.Active, out usedBinding);
		}
		else
		{
			willThrowTool = ToolItemManager.GetBoundAttackTool(AttackToolBinding.Neutral, ToolEquippedReadSource.Active, out usedBinding);
		}
		if ((bool)willThrowTool)
		{
			return CanThrowTool(willThrowTool, usedBinding, reportFailure);
		}
		return false;
	}

	private bool CanThrowTool(ToolItem tool, AttackToolBinding binding, bool reportFailure)
	{
		switch (tool.Type)
		{
		case ToolItemType.Red:
			if (!tool.IsEmpty || (tool.UsableWhenEmpty && !tool.UsableWhenEmptyPrevented))
			{
				int silkRequired = tool.Usage.SilkRequired;
				if (silkRequired <= 0 || playerData.silk >= silkRequired)
				{
					return true;
				}
				EventRegister.SendEvent(EventRegisterEvents.BindFailedNotEnough);
			}
			if (reportFailure)
			{
				ToolItemManager.ReportBoundAttackToolFailed(binding);
			}
			return false;
		case ToolItemType.Skill:
			if (playerData.silk >= playerData.SilkSkillCost)
			{
				return true;
			}
			if (reportFailure)
			{
				ToolItemManager.ReportBoundAttackToolFailed(binding);
				EventRegister.SendEvent(EventRegisterEvents.BindFailedNotEnough);
			}
			return false;
		default:
			return false;
		}
	}

	public void SetToolCooldown(float cooldown)
	{
		canThrowTime = Time.timeAsDouble + (double)cooldown;
	}

	private void ThrowTool(bool isAutoThrow)
	{
		if ((!isAutoThrow && Time.timeAsDouble < canThrowTime) || !willThrowTool)
		{
			return;
		}
		AttackToolBinding? attackToolBinding = ToolItemManager.GetAttackToolBinding(willThrowTool);
		if (!attackToolBinding.HasValue)
		{
			return;
		}
		inputHandler.inputActions.QuickCast.ClearInputState();
		ToolItem.UsageOptions usage = willThrowTool.Usage;
		ToolItemsData.Data savedData = willThrowTool.SavedData;
		bool isEmpty = willThrowTool.IsEmpty;
		if (!CanThrowTool(willThrowTool, attackToolBinding.Value, reportFailure: true))
		{
			return;
		}
		if (cState.isToolThrowing)
		{
			ThrowToolEnd();
		}
		ResetLook();
		cState.recoiling = false;
		cState.floating = false;
		CancelAttack(resetNailCharge: true);
		if (!isAutoThrow && TrySetCorrectFacing(force: true) && cState.wallSliding)
		{
			FlipSprite();
		}
		if (willThrowTool.Type == ToolItemType.Skill)
		{
			if (skillEventTarget.IsEventValid(usage.FsmEventName, isRequired: true).Value)
			{
				EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
				skillEventTarget.SendEvent(usage.FsmEventName);
			}
			else
			{
				Debug.LogErrorFormat(this, "Fsm Skill Tool Event {0} is invalid", usage.FsmEventName);
			}
			return;
		}
		if (!usage.ThrowPrefab && !usage.IsNonBlockingEvent)
		{
			if (toolEventTarget.IsEventValid(usage.FsmEventName, isRequired: true).Value)
			{
				if (cState.wallSliding)
				{
					FlipSprite();
					CancelWallsliding();
				}
				string activeStateName = toolEventTarget.ActiveStateName;
				toolEventTarget.SendEventSafe("TAKE CONTROL");
				toolEventTarget.SendEvent(usage.FsmEventName);
				if (toolEventTarget.ActiveStateName != activeStateName)
				{
					DidUseAttackTool(savedData);
				}
			}
			else
			{
				Debug.LogErrorFormat(this, "Fsm Tool Event {0} is invalid", usage.FsmEventName);
			}
			return;
		}
		DidUseAttackTool(savedData);
		cState.isToolThrowing = true;
		cState.toolThrowCount++;
		cState.throwingToolVertical = willThrowTool.Usage.ThrowAnimVerticalDirection;
		toolThrowDuration = ((cState.throwingToolVertical > 0) ? animCtrl.GetClipDuration("ToolThrow Up") : animCtrl.GetClipDuration("ToolThrow Q"));
		toolThrowTime = 0f;
		DidAttack();
		throwToolCooldown = usage.ThrowCooldown;
		attackAudioTable.SpawnAndPlayOneShot(this.transform.position);
		Transform transform;
		float num;
		float direction;
		int num2;
		Vector2 vector3;
		if ((bool)usage.ThrowPrefab)
		{
			Vector2 vector;
			if (cState.wallSliding)
			{
				transform = toolThrowWallPoint;
				vector = Vector2.right;
				num = -1f;
				direction = (cState.facingRight ? 180 : 0);
			}
			else
			{
				transform = toolThrowPoint;
				vector = Vector2.left;
				num = 1f;
				direction = ((!cState.facingRight) ? 180 : 0);
			}
			if ((bool)toolThrowClosePoint)
			{
				Vector3 position = transform.position;
				Vector3 vector2 = toolThrowClosePoint.TransformDirection(vector);
				Vector3 position2 = toolThrowClosePoint.position;
				float length = Mathf.Abs(position.x - position2.x);
				if (Helper.IsRayHittingNoTriggers(position2, vector2, length, 8448))
				{
					transform = toolThrowClosePoint;
				}
			}
			if (isAutoThrow)
			{
				num2 = (usage.UseAltForQuickSling ? 1 : 0);
				if (num2 != 0)
				{
					vector3 = usage.ThrowOffsetAlt;
					goto IL_03aa;
				}
			}
			else
			{
				num2 = 0;
			}
			vector3 = usage.ThrowOffset;
			goto IL_03aa;
		}
		toolEventTarget.SendEventSafe(usage.FsmEventName);
		goto IL_0566;
		IL_03aa:
		Vector2 vector4 = vector3;
		if (cState.wallSliding)
		{
			vector4.x *= -1f;
		}
		vector4.y += UnityEngine.Random.Range(-0.1f, 0.1f);
		GameObject gameObject = usage.ThrowPrefab.Spawn(transform.TransformPoint(vector4));
		if (usage.ScaleToHero)
		{
			Vector3 localScale = gameObject.transform.localScale;
			float num3 = (cState.facingRight ? (0f - num) : num);
			localScale.x = num3 * usage.ThrowPrefab.transform.localScale.x;
			if (usage.FlipScale)
			{
				localScale.x = 0f - localScale.x;
			}
			gameObject.transform.localScale = localScale;
			if (usage.SetDamageDirection)
			{
				DamageEnemies component = gameObject.GetComponent<DamageEnemies>();
				if ((bool)component)
				{
					component.SetDirection(direction);
				}
			}
		}
		Vector2 linearVelocity = ((num2 != 0) ? usage.ThrowVelocityAlt : usage.ThrowVelocity).MultiplyElements(num);
		if (linearVelocity.magnitude > Mathf.Epsilon)
		{
			Rigidbody2D component2 = gameObject.GetComponent<Rigidbody2D>();
			if ((bool)component2)
			{
				if (!cState.facingRight)
				{
					linearVelocity.x *= -1f;
				}
				if (Mathf.Abs(linearVelocity.y) > Mathf.Epsilon)
				{
					float magnitude = linearVelocity.magnitude;
					linearVelocity = (linearVelocity.normalized.DirectionToAngle() + UnityEngine.Random.Range(-2f, 2f)).AngleToDirection() * magnitude;
				}
				component2.linearVelocity = linearVelocity;
			}
		}
		vibrationCtrl.PlayToolThrow();
		goto IL_0566;
		IL_0566:
		willThrowTool.OnWasUsed(isEmpty);
		bool flag = Gameplay.QuickSlingTool.Status.IsEquipped && !ToolItemManager.IsCustomToolOverride;
		if (flag)
		{
			quickSlingAudioTable.SpawnAndPlayOneShot(this.transform.position);
		}
		if (!isAutoThrow && flag && CanThrowTool(willThrowTool, attackToolBinding.Value, reportFailure: false))
		{
			queuedAutoThrowTool = true;
			return;
		}
		willThrowTool = null;
		queuedAutoThrowTool = false;
	}

	private void DidUseAttackTool(ToolItemsData.Data toolData)
	{
		if (!willThrowTool.IsCustomUsage && toolData.AmountLeft > 0)
		{
			toolData.AmountLeft--;
			willThrowTool.SavedData = toolData;
			ToolItemLimiter.ReportToolUsed(willThrowTool);
		}
		AttackToolBinding value = ToolItemManager.GetAttackToolBinding(willThrowTool).Value;
		ToolItemManager.ReportBoundAttackToolUsed(value);
		ToolItemManager.ReportBoundAttackToolUpdated(value);
	}

	private void ThrowToolEnd()
	{
		if (cState.isToolThrowing)
		{
			cState.isToolThrowing = false;
			queuedAutoThrowTool = false;
			animCtrl.StopToolThrow();
		}
	}

	public void QueueLifeBloodStateQuick()
	{
		if (!IsInLifebloodState)
		{
			queuedLifeBloodState = true;
		}
	}

	public void EnterLifebloodStateQuick()
	{
		queuedLifeBloodState = false;
		EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
		toolEventTarget.SendEventSafe("TAKE CONTROL");
		toolEventTarget.SendEvent("OVERBLUE QUICK");
	}

	public bool TrySetCorrectFacing(bool force = false)
	{
		if (!(CanTurn || force))
		{
			return false;
		}
		if (move_input > 0f && !cState.facingRight)
		{
			FlipSprite();
			return true;
		}
		if (move_input < 0f && cState.facingRight)
		{
			FlipSprite();
			return true;
		}
		return false;
	}

	private void DoEdgeAdjust()
	{
		if (!SlideSurface.IsHeroInside && !dashingDown)
		{
			float edgeAdjustX = EdgeAdjustHelper.GetEdgeAdjustX(col2d, cState.facingRight, 0.25f);
			if (edgeAdjustX != 0f)
			{
				rb2d.MovePosition(rb2d.position + new Vector2(edgeAdjustX, 0f));
			}
		}
	}

	private void Update10()
	{
		if (isGameplayScene)
		{
			OutOfBoundsCheck();
		}
		float scaleX = transform.GetScaleX();
		if (scaleX < -1f)
		{
			transform.SetScaleX(-1f);
		}
		if (scaleX > 1f)
		{
			transform.SetScaleX(1f);
		}
		if (!controlReqlinquished && Math.Abs(transform.position.z - 0.004f) >= Mathf.Epsilon)
		{
			transform.SetPositionZ(0.004f);
		}
	}

	private void LateUpdate()
	{
		if (!cState.wallSliding || cState.onConveyorV)
		{
			return;
		}
		Vector2 linearVelocity = rb2d.linearVelocity;
		if (cState.wallClinging)
		{
			if (linearVelocity.y < -10f)
			{
				linearVelocity.y = -10f;
			}
			if (linearVelocity.y < 0f)
			{
				linearVelocity.y += WALLCLING_DECEL * Time.deltaTime;
			}
			else if (linearVelocity.y > 0f)
			{
				linearVelocity.y = 0f;
			}
			wallStickTimer = 0f;
			wallStickStartVelocity = 0f;
		}
		else if (wallStickTimer < WALLSLIDE_STICK_TIME)
		{
			if (wallStickStartVelocity > 0f)
			{
				wallStickStartVelocity += WALLSLIDE_ACCEL * Time.deltaTime;
			}
			linearVelocity.y = Mathf.Max(wallStickStartVelocity, 0f);
			wallStickTimer += Time.deltaTime;
		}
		else
		{
			linearVelocity = new Vector3(linearVelocity.x, linearVelocity.y + WALLSLIDE_ACCEL * Time.deltaTime);
		}
		rb2d.linearVelocity = linearVelocity;
	}

	private void OnLevelUnload()
	{
		if (transform.parent != null)
		{
			SetHeroParent(null);
		}
	}

	private void OnDisable()
	{
		if (gm != null)
		{
			gm.UnloadingLevel -= OnLevelUnload;
		}
		ReattachHeroLight();
	}

	private void Move(float moveDirection, bool useInput)
	{
		if (cState.onGround)
		{
			SetState(ActorStates.grounded);
		}
		if (cState.downSpikeRecovery && cState.onGround)
		{
			moveDirection = 0f;
		}
		if (cState.isTouchingSlopeLeft && moveDirection < 0f)
		{
			moveDirection = 0f;
		}
		else if (cState.isTouchingSlopeRight && moveDirection > 0f)
		{
			moveDirection = 0f;
		}
		Vector2 linearVelocity = rb2d.linearVelocity;
		if (useInput && !cState.wallSliding)
		{
			if (cState.inWalkZone && cState.onGround)
			{
				linearVelocity.x = moveDirection * GetWalkSpeed();
			}
			else
			{
				linearVelocity.x = moveDirection * GetRunSpeed();
			}
		}
		foreach (DecayingVelocity extraAirMoveVelocity in extraAirMoveVelocities)
		{
			switch (extraAirMoveVelocity.SkipBehaviour)
			{
			case DecayingVelocity.SkipBehaviours.WhileMoving:
				if (Math.Abs(move_input) > Mathf.Epsilon)
				{
					continue;
				}
				break;
			case DecayingVelocity.SkipBehaviours.WhileMovingForward:
				if (cState.facingRight)
				{
					if (move_input > Mathf.Epsilon)
					{
						continue;
					}
				}
				else if (move_input < Mathf.Epsilon)
				{
					continue;
				}
				break;
			case DecayingVelocity.SkipBehaviours.WhileMovingBackward:
				if (cState.facingRight)
				{
					if (move_input < Mathf.Epsilon)
					{
						continue;
					}
				}
				else if (move_input > Mathf.Epsilon)
				{
					continue;
				}
				break;
			default:
				throw new ArgumentOutOfRangeException();
			case DecayingVelocity.SkipBehaviours.None:
				break;
			}
			linearVelocity += extraAirMoveVelocity.Velocity;
		}
		rb2d.linearVelocity = linearVelocity;
	}

	private void Jump()
	{
		if (jump_steps <= JUMP_STEPS)
		{
			if (isDashStabBouncing)
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, Config.DashStabBounceJumpSpeed);
			}
			else
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, useUpdraftExitJumpSpeed ? JUMP_SPEED_UPDRAFT_EXIT : JUMP_SPEED);
			}
			jump_steps++;
			jumped_steps++;
			ledgeBufferSteps = 0;
			sprintBufferSteps = 0;
			syncBufferSteps = false;
		}
		else
		{
			CancelJump();
		}
	}

	private void DoubleJump()
	{
		if (cState.inUpdraft && CanFloat())
		{
			CancelDoubleJump();
			fsm_brollyControl.SendEvent("FORCE UPDRAFT ENTER");
			return;
		}
		if (doubleJump_steps <= DOUBLE_JUMP_RISE_STEPS + DOUBLE_JUMP_FALL_STEPS)
		{
			if (doubleJump_steps > DOUBLE_JUMP_FALL_STEPS)
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, JUMP_SPEED * 1.1f);
			}
			doubleJump_steps++;
		}
		else
		{
			CancelDoubleJump();
		}
		if (cState.onGround)
		{
			CancelDoubleJump();
		}
	}

	public void SetSlashComponent(NailSlash nailSlash)
	{
		SlashComponent = nailSlash;
	}

	private void DoAttack()
	{
		queuedWallJumpInterrupt = false;
		ResetLook();
		if (cState.dashing || dashingDown)
		{
			CancelDash();
		}
		cState.recoiling = false;
		if (inputHandler.inputActions.Up.IsPressed)
		{
			Attack(AttackDirection.upward);
		}
		else if (inputHandler.inputActions.Down.IsPressed)
		{
			if (allowAttackCancellingDownspikeRecovery || !cState.onGround)
			{
				Attack(AttackDirection.downward);
			}
			else
			{
				Attack(AttackDirection.normal);
			}
		}
		else
		{
			Attack(AttackDirection.normal);
		}
	}

	public void IncrementAttackCounter()
	{
		cState.attackCount++;
	}

	private void Attack(AttackDirection attackDir)
	{
		TrySetCorrectFacing(force: true);
		if (WarriorState.IsInRageMode)
		{
			warriorRageAttackAudioTable.SpawnAndPlayOneShot(transform.position);
		}
		else
		{
			attackAudioTable.SpawnAndPlayOneShot(transform.position);
		}
		cState.floating = false;
		IncrementAttackCounter();
		UpdateConfig();
		if (Time.timeSinceLevelLoad - altAttackTime > Config.AttackRecoveryTime + ALT_ATTACK_RESET || attackDir != prevAttackDir)
		{
			cState.altAttack = false;
		}
		if (attackDir != AttackDirection.downward || Config.DownSlashType != HeroControllerConfig.DownSlashTypes.DownSpike)
		{
			cState.attacking = true;
			attackDuration = Config.AttackDuration;
			if (IsUsingQuickening)
			{
				attackDuration *= 1f / Config.QuickAttackSpeedMult;
			}
		}
		bool isSlashing = true;
		if (wandererDashComboWindowTimer > 0f && playerData.CurrentCrestID == "Wanderer")
		{
			sprintFSM.SendEvent("WANDERER DASH COMBO");
			return;
		}
		if ((cState.wallSliding || cState.wallScrambling) && attackDir == AttackDirection.normal && !Gameplay.ToolmasterCrest.IsEquipped)
		{
			if (Config.WallSlashSlowdown)
			{
				rb2d.SetVelocity(rb2d.linearVelocity.x, rb2d.linearVelocity.y / 2f);
				if (rb2d.linearVelocity.y < -5f)
				{
					rb2d.SetVelocity(rb2d.linearVelocity.x, -5f);
				}
			}
			wallSlashing = true;
			SlashComponent = wallSlash;
			currentSlashDamager = wallSlashDamager;
		}
		else
		{
			if ((cState.wallSliding || cState.wallScrambling) && attackDir == AttackDirection.normal && Gameplay.ToolmasterCrest.IsEquipped)
			{
				FlipSprite();
				CancelWallsliding();
			}
			wallSlashing = false;
			switch (attackDir)
			{
			case AttackDirection.normal:
				if (cState.altAttack)
				{
					SlashComponent = alternateSlash;
					currentSlashDamager = alternateSlashDamager;
					cState.altAttack = false;
					break;
				}
				SlashComponent = normalSlash;
				currentSlashDamager = normalSlashDamager;
				if ((bool)alternateSlash)
				{
					cState.altAttack = true;
				}
				break;
			case AttackDirection.upward:
				if (cState.wallSliding)
				{
					rb2d.MovePosition(rb2d.position + new Vector2(cState.facingRight ? (-0.8f) : 0.8f, 0f));
				}
				AttackCancelWallSlide();
				if (cState.altAttack)
				{
					SlashComponent = altUpSlash;
					currentSlashDamager = altUpSlashDamager;
					cState.altAttack = false;
				}
				else
				{
					SlashComponent = upSlash;
					currentSlashDamager = upSlashDamager;
					if ((bool)altUpSlash)
					{
						cState.altAttack = true;
					}
				}
				cState.upAttacking = true;
				break;
			case AttackDirection.downward:
				AttackCancelWallSlide();
				DownAttack(ref isSlashing);
				break;
			default:
				throw new ArgumentOutOfRangeException("attackDir", attackDir, null);
			}
		}
		if (isSlashing)
		{
			if (cState.wallSliding || cState.wallScrambling)
			{
				if (cState.facingRight)
				{
					currentSlashDamager.SetDirection(180f);
				}
				else
				{
					currentSlashDamager.SetDirection(0f);
				}
			}
			else if (attackDir == AttackDirection.normal && cState.facingRight)
			{
				currentSlashDamager.SetDirection(0f);
			}
			else if (attackDir == AttackDirection.normal && !cState.facingRight)
			{
				currentSlashDamager.SetDirection(180f);
			}
			else if (attackDir == AttackDirection.upward)
			{
				currentSlashDamager.SetDirection(90f);
			}
			else
			{
				currentSlashDamager.SetDirection(270f);
			}
			altAttackTime = Time.timeSinceLevelLoad;
			SlashComponent.StartSlash();
			DidAttack();
		}
		prevAttackDir = attackDir;
	}

	private void AttackCancelWallSlide()
	{
		if (cState.wallSliding)
		{
			FlipSprite();
			CancelWallsliding();
		}
	}

	public void QueueCancelDownAttack()
	{
		tryCancelDownSlash = true;
		_slashComponent = downSlash;
	}

	private void DownAttack(ref bool isSlashing)
	{
		CancelQueuedBounces();
		switch (Config.DownSlashType)
		{
		case HeroControllerConfig.DownSlashTypes.DownSpike:
		{
			isSlashing = false;
			if (transform.localScale.x > 0f)
			{
				downSpikeHorizontalSpeed = 0f - Config.DownspikeSpeed;
			}
			else
			{
				downSpikeHorizontalSpeed = Config.DownspikeSpeed;
			}
			Vector2 linearVelocity = rb2d.linearVelocity;
			if (Config.DownspikeThrusts)
			{
				RelinquishControl();
				linearVelocity.y = Mathf.Clamp(linearVelocity.y, DOWNSPIKE_ANTIC_CLAMP_VEL_Y.Start, DOWNSPIKE_ANTIC_CLAMP_VEL_Y.End);
				AffectedByGravity(gravityApplies: false);
			}
			else
			{
				CancelJump();
				if (linearVelocity.y > 0f)
				{
					linearVelocity.y = 0f;
				}
			}
			cState.downSpikeAntic = true;
			downSpikeTimer = 0f;
			if (cState.altAttack)
			{
				currentDownspike = altDownSpike;
				cState.altAttack = false;
			}
			else
			{
				currentDownspike = downSpike;
				if ((bool)altDownSpike)
				{
					cState.altAttack = true;
				}
			}
			rb2d.linearVelocity = linearVelocity;
			DidAttack();
			break;
		}
		case HeroControllerConfig.DownSlashTypes.Slash:
			if (cState.altAttack)
			{
				SlashComponent = altDownSlash;
				currentSlashDamager = altDownSlashDamager;
				cState.altAttack = false;
			}
			else
			{
				SlashComponent = downSlash;
				currentSlashDamager = downSlashDamager;
				if ((bool)altDownSlash)
				{
					cState.altAttack = true;
				}
			}
			cState.downAttacking = true;
			break;
		case HeroControllerConfig.DownSlashTypes.Custom:
			isSlashing = false;
			crestAttacksFSM.SendEvent(Config.DownSlashEvent);
			DidAttack();
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private void DidAttack()
	{
		float num = Config.AttackDuration;
		attack_cooldown = (IsUsingQuickening ? Config.QuickAttackCooldownTime : Config.AttackCooldownTime);
		if (attack_cooldown < num)
		{
			attack_cooldown = num;
		}
	}

	private void Dash()
	{
		if (CanWallSlide())
		{
			BeginWallSlide(requireInput: false);
			return;
		}
		AffectedByGravity(gravityApplies: false);
		HeroActions inputActions = inputHandler.inputActions;
		bool wasDashingDown = dashingDown;
		if (cState.onGround)
		{
			dashingDown = false;
		}
		cState.mantleRecovery = false;
		if (dashingDown)
		{
			cState.falling = true;
		}
		if (dash_timer <= 0f && (!dashingDown || !inputActions.Dash.IsPressed))
		{
			FinishedDashing(wasDashingDown);
			return;
		}
		float dASH_SPEED = DASH_SPEED;
		if (dashingDown)
		{
			float maxFallVelocity = GetMaxFallVelocity();
			rb2d.linearVelocity = new Vector2(0f, 0f - maxFallVelocity);
		}
		else
		{
			heroBox.HeroBoxAirdash();
			if (cState.facingRight)
			{
				rb2d.linearVelocity = new Vector2(dASH_SPEED, 0f);
				CheckForBump(CollisionSide.right);
			}
			else
			{
				rb2d.linearVelocity = new Vector2(0f - dASH_SPEED, 0f);
				CheckForBump(CollisionSide.left);
			}
		}
		dash_timer -= Time.deltaTime;
		dash_time += Time.deltaTime;
	}

	private void BackDash()
	{
	}

	private void Downspike()
	{
		if (downSpikeTimer > Config.DownSpikeTime)
		{
			FinishDownspike();
			return;
		}
		if (Config.DownspikeThrusts)
		{
			rb2d.linearVelocity = new Vector2(downSpikeHorizontalSpeed, 0f - Config.DownspikeSpeed);
		}
		downSpikeTimer += Time.deltaTime;
	}

	private void StartAirHang()
	{
		rb2d.gravityScale = AIR_HANG_GRAVITY;
	}

	public void FaceRight()
	{
		cState.facingRight = true;
		Vector3 localScale = transform.localScale;
		if (!(localScale.x < 0f))
		{
			localScale.x = -1f;
			transform.localScale = localScale;
			ChangedFacing();
		}
	}

	public void FaceLeft()
	{
		cState.facingRight = false;
		Vector3 localScale = transform.localScale;
		if (!(localScale.x > 0f))
		{
			localScale.x = 1f;
			transform.localScale = localScale;
			ChangedFacing();
		}
	}

	private void ChangedFacing()
	{
		airDashEffect.SetActive(value: false);
		this.FlippedSprite?.Invoke();
	}

	public void SetBackOnGround()
	{
		BackOnGround();
	}

	public void SetStartWithWallslide()
	{
		startWithWallslide = true;
	}

	public bool TryFsmCancelToWallSlide()
	{
		if (!CanStartWithWallSlide())
		{
			return false;
		}
		if (!CheckStillTouchingWall((!cState.facingRight) ? CollisionSide.left : CollisionSide.right))
		{
			return false;
		}
		if (controlReqlinquished)
		{
			EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
			RegainControl();
			StartAnimationControl();
		}
		ForceTouchingWall();
		BeginWallSlide(requireInput: false);
		return true;
	}

	public void SetStartWithShuttlecock()
	{
		startWithShuttlecock = true;
	}

	public void SetStartWithJump()
	{
		startWithJump = true;
	}

	public void SetStartWithWallJump()
	{
		startWithWallJump = true;
	}

	public void SetStartWithTinyJump()
	{
		startWithTinyJump = true;
	}

	public void SetStartWithFlipJump()
	{
		startWithFlipJump = true;
		SetStartFromMantle();
		SetStartWithFullJump();
	}

	public void SilkChargeEnd()
	{
		parryInvulnTimer = INVUL_TIME_SILKDASH;
		if (!cState.onGround)
		{
			AddExtraAirMoveVelocity(new DecayingVelocity
			{
				Velocity = new Vector2(rb2d.linearVelocity.x, 0f),
				Decay = 4f,
				CancelOnTurn = true,
				SkipBehaviour = DecayingVelocity.SkipBehaviours.WhileMoving
			});
			animCtrl.SetPlaySilkChargeEnd();
		}
	}

	public void HarpoonDashEnd()
	{
		if (!cState.onGround)
		{
			AddExtraAirMoveVelocity(new DecayingVelocity
			{
				Velocity = new Vector2(rb2d.linearVelocity.x, 0f),
				Decay = 4f,
				CancelOnTurn = true,
				SkipBehaviour = DecayingVelocity.SkipBehaviours.WhileMoving
			});
			animCtrl.SetPlaySilkChargeEnd();
		}
	}

	public void SetStartWithAnyJump()
	{
		startWithAnyJump = true;
	}

	public void SetStartWithFullJump()
	{
		startWithFullJump = true;
	}

	public void SetStartWithBackflipJump()
	{
		startWithBackflipJump = true;
		SetStartFromMantle();
		SetStartWithFullJump();
	}

	public void SetStartWithBrolly()
	{
		startWithBrolly = true;
	}

	public void SetStartWithDoubleJump()
	{
		startWithDoubleJump = true;
	}

	public void SetStartWithWallsprintLaunch()
	{
		startWithWallsprintLaunch = true;
	}

	public void SetStartWithDash()
	{
		startWithDash = true;
	}

	public void SetStartWithDashKeepFacing()
	{
		startWithDash = true;
		dashCurrentFacing = true;
	}

	public void SetStartWithAttack()
	{
		startWithAttack = true;
	}

	public void SetStartWithToolThrow()
	{
		startWithToolThrow = true;
		GetWillThrowTool(reportFailure: true);
	}

	public void SetStartWithDashStabBounce()
	{
		if (Config.ForceShortDashStabBounce)
		{
			startWithDownSpikeBounceShort = true;
			attack_cooldown = 0.1f;
		}
		else
		{
			if (Helper.IsRayHittingNoTriggers(rb2d.position, Vector2.up, 3.5f, 8448))
			{
				startWithDownSpikeBounceShort = true;
			}
			else
			{
				startWithDownSpikeBounce = true;
			}
			attack_cooldown = 0.15f;
		}
		isDashStabBouncing = true;
	}

	public void SetStartWithDownSpikeBounce()
	{
		startWithDownSpikeBounce = true;
		attack_cooldown = 0.15f;
	}

	public void SetStartWithDownSpikeBounceSlightlyShort()
	{
		startWithDownSpikeBounceSlightlyShort = true;
		attack_cooldown = 0.15f;
		dashCooldownTimer = 0.1f;
	}

	public void SetStartWithDownSpikeBounceShort()
	{
		startWithDownSpikeBounceShort = true;
	}

	public void ResetAnimationDownspikeBounce()
	{
		animCtrl.ResetDownspikeBounce();
	}

	public void SetStartWithDownSpikeEnd()
	{
		startWithDownSpikeEnd = true;
	}

	public void SetStartWithBalloonBounce()
	{
		startWithBalloonBounce = true;
	}

	public void SetStartWithHarpoonBounce()
	{
		startWithHarpoonBounce = true;
		attack_cooldown = 0.15f;
	}

	public void CancelQueuedBounces()
	{
		startWithBalloonBounce = false;
		startWithDownSpikeBounce = false;
		startWithDownSpikeBounceShort = false;
		startWithDownSpikeBounceSlightlyShort = false;
	}

	public void SetStartWithWitchSprintBounce()
	{
		startWithDownSpikeBounce = true;
		attack_cooldown = 0.15f;
		if (transform.localScale.x > 0f)
		{
			downspike_rebound_xspeed = DOWNSPIKE_REBOUND_SPEED;
		}
		else
		{
			downspike_rebound_xspeed = 0f - DOWNSPIKE_REBOUND_SPEED;
		}
	}

	public void SetStartWithUpdraftExit()
	{
		startWithUpdraftExit = true;
	}

	public void SetStartWithScrambleLeap()
	{
		startWithScrambleLeap = true;
	}

	public void SetStartWithRecoilBack()
	{
		startWithRecoilBack = true;
	}

	public void SetStartWithRecoilBackLong()
	{
		startWithRecoilBackLong = true;
	}

	public void SetStartWithWhipPullRecoil()
	{
		startWithWhipPullRecoil = true;
	}

	public void SetSuperDashExit()
	{
		exitedSuperDashing = true;
	}

	public void SetQuakeExit()
	{
		exitedQuake = true;
	}

	public void SetTakeNoDamage()
	{
		takeNoDamage = true;
	}

	public void EndTakeNoDamage()
	{
		takeNoDamage = false;
	}

	public void SetStartFromMantle()
	{
		startFromMantle = true;
	}

	public void SetStartFromReaperUpperslash()
	{
		jump_steps = JUMP_STEPS;
		animCtrl.SetPlayDashUpperRecovery();
	}

	public void SetHeroParent(Transform newParent)
	{
		if (!(transform.parent == newParent))
		{
			transform.parent = newParent;
			if (newParent == null)
			{
				UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
			}
		}
	}

	public void SetBlockFsmMove(bool blocked)
	{
		blockFsmMove = blocked;
	}

	public void IsSwimming()
	{
		if (!cState.swimming && GetTotalFrostSpeed() > Mathf.Epsilon)
		{
			tagDamageTaker.AddDamageTagToStack(frostWaterDamageTag);
			StatusVignette.AddStatus(StatusVignette.StatusTypes.InFrostWater);
			GameCameras.instance.cameraController.ScreenFlashFrostStart();
		}
		cState.downSpikeRecovery = false;
		cState.swimming = true;
		dashingDown = false;
		CancelFallEffects();
	}

	public void AddFrost(float percentage)
	{
		frostAmount += percentage / 100f;
	}

	public void SetFrostAmount(float amount)
	{
		frostAmount = amount;
	}

	public void NotSwimming()
	{
		if (cState.swimming)
		{
			cState.swimming = false;
			StatusVignette.RemoveStatus(StatusVignette.StatusTypes.InFrostWater);
			TimerGroup damageCooldownTimer = frostWaterDamageTag.DamageCooldownTimer;
			bool flag = (bool)damageCooldownTimer && !damageCooldownTimer.HasEnded;
			tagDamageTaker.RemoveDamageTagFromStack(frostWaterDamageTag, !flag);
		}
	}

	public bool GetAirdashed()
	{
		return airDashed;
	}

	public bool GetCanAirDashCancel()
	{
		if (playerData.hasDash)
		{
			return !airDashed;
		}
		return false;
	}

	public void EnableRenderer()
	{
		renderer.enabled = true;
	}

	public void ResetAirMoves()
	{
		if (!cState.doubleJumping)
		{
			doubleJumped = false;
		}
		airDashed = false;
	}

	public void SetConveyorSpeed(float speed)
	{
		conveyorSpeed = speed;
	}

	public void EnterWithoutInput(bool flag)
	{
		enterWithoutInput = flag;
	}

	public void SetDarkness(int darkness)
	{
	}

	public void CancelHeroJump()
	{
		if (cState.jumping || cState.doubleJumping)
		{
			CancelJump();
			CancelDoubleJump();
			if (rb2d.linearVelocity.y > 0f)
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, 0f);
			}
		}
	}

	public void StartHarpoonDashCooldown()
	{
		harpoonDashCooldown = 0.16f;
	}

	public void StartHarpoonDashCooldownShort()
	{
		harpoonDashCooldown = 0.1f;
	}

	public void CharmUpdate()
	{
		playerData.maxHealth = playerData.maxHealthBase;
		playerData.MaxHealth();
		UpdateBlueHealth();
	}

	public void UpdateBlueHealth()
	{
		playerData.healthBlue = 0;
	}

	public void HitMaxBlueHealth()
	{
		if (!IsInLifebloodState)
		{
			IsInLifebloodState = true;
			if (queuedLifeBloodState && gm.QueuedBlueHealth == 0)
			{
				EnterLifebloodStateQuick();
			}
		}
	}

	public void HitMaxBlueHealthBurst()
	{
		EventRegister.SendEvent(EventRegisterEvents.CharmIndicatorCheck);
		CharmUpdate();
		EventRegister.SendEvent(EventRegisterEvents.HealthUpdate);
	}

	public float GetMaxFallVelocity()
	{
		float result = MAX_FALL_VELOCITY;
		if (Gameplay.WeightedAnkletTool.Status.IsEquipped)
		{
			result = MAX_FALL_VELOCITY_WEIGHTED;
		}
		return result;
	}

	public void ResetLifebloodState()
	{
		if (IsInLifebloodState)
		{
			IsInLifebloodState = false;
			EventRegister.SendEvent(EventRegisterEvents.CharmIndicatorCheck);
		}
	}

	public void checkEnvironment()
	{
		if ((bool)enviroRegionListener)
		{
			enviroRegionListener.Refresh(fixRecursion: true);
		}
		EnvironmentTypes environmentType = playerData.environmentType;
		try
		{
			audioCtrl.SetFootstepsTable(footStepTables[(int)environmentType]);
		}
		catch (IndexOutOfRangeException)
		{
		}
	}

	public void SetBenchRespawn(string spawnMarker, string sceneName, int spawnType, bool facingRight)
	{
		playerData.SetBenchRespawn(spawnMarker, sceneName, spawnType, facingRight);
	}

	public void SetBenchRespawn(RespawnMarker spawnMarker, string sceneName, int spawnType)
	{
		playerData.SetBenchRespawn(spawnMarker, sceneName, spawnType);
	}

	public void SetHazardRespawn(Vector3 position, bool facingRight)
	{
		playerData.SetHazardRespawn(position, facingRight);
	}

	public void AddGeo(int amount)
	{
		CurrencyManager.AddGeo(amount);
	}

	public void ToZero()
	{
		CurrencyManager.ToZero();
	}

	public void AddGeoQuietly(int amount)
	{
		CurrencyManager.AddGeoQuietly(amount);
	}

	public void AddGeoToCounter(int amount)
	{
		CurrencyManager.AddGeoToCounter(amount);
	}

	public void TakeGeo(int amount)
	{
		CurrencyManager.TakeGeo(amount);
	}

	public void AddShards(int amount)
	{
		CurrencyManager.AddShards(amount);
	}

	public void TakeShards(int amount)
	{
		CurrencyManager.TakeShards(amount);
	}

	public void AddCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		CurrencyManager.ChangeCurrency(amount, type, showCounter);
	}

	public void TakeCurrency(int amount, CurrencyType type, bool showCounter = true)
	{
		CurrencyManager.ChangeCurrency(-amount, type, showCounter);
	}

	public int GetCurrencyAmount(CurrencyType type)
	{
		return CurrencyManager.GetCurrencyAmount(type);
	}

	public void TempStoreCurrency()
	{
		CurrencyManager.TempStoreCurrency();
	}

	public void RestoreTempStoredCurrency()
	{
		CurrencyManager.RestoreTempStoredCurrency();
	}

	public void UpdateGeo()
	{
	}

	public bool CanInput()
	{
		if (acceptingInput && !IsPaused() && transitionState == HeroTransitionState.WAITING_TO_TRANSITION && !gm.RespawningHero && !cState.hazardRespawning)
		{
			return !cState.hazardDeath;
		}
		return false;
	}

	public bool IsPaused()
	{
		if (!gm.isPaused)
		{
			return playerData.isInventoryOpen;
		}
		return true;
	}

	public bool IsHunterCrestEquipped()
	{
		if (!(playerData.CurrentCrestID == "Hunter") && !(playerData.CurrentCrestID == "Hunter_v2"))
		{
			return playerData.CurrentCrestID == "Hunter_v3";
		}
		return true;
	}

	public bool IsArchitectCrestEquipped()
	{
		return playerData.CurrentCrestID == "Toolmaster";
	}

	public bool IsShamanCrestEquipped()
	{
		return playerData.CurrentCrestID == "Spell";
	}

	public void FlipSprite()
	{
		cState.facingRight = !cState.facingRight;
		Vector3 localScale = transform.localScale;
		localScale.x *= -1f;
		transform.localScale = localScale;
		for (int num = extraAirMoveVelocities.Count - 1; num >= 0; num--)
		{
			if (extraAirMoveVelocities[num].CancelOnTurn)
			{
				extraAirMoveVelocities.RemoveAt(num);
			}
		}
		ChangedFacing();
	}

	public void RefreshFacing()
	{
		cState.facingRight = transform.lossyScale.x < 0f;
	}

	public void RefreshScale()
	{
		if (cState.facingRight)
		{
			FaceRight();
		}
		else
		{
			FaceLeft();
		}
	}

	public void NeedleArtRecovery()
	{
		attack_cooldown = 0.35f;
	}

	public void CrestAttackRecovery()
	{
		attack_cooldown = 0.2f;
	}

	public void NailParry()
	{
		parryInvulnTimer = INVUL_TIME_PARRY;
		sprintFSM.SendEvent("NAIL CLASH");
		parriedAttack = cState.attackCount;
	}

	public bool IsParrying()
	{
		if (!IsParryingActive())
		{
			return parryInvulnTimer > 0f;
		}
		return true;
	}

	public bool IsParryingActive()
	{
		if (!cState.parrying)
		{
			return cState.parryAttack;
		}
		return true;
	}

	public void NailParryRecover()
	{
		if (parriedAttack == cState.attackCount)
		{
			attackDuration = 0f;
			attack_cooldown = 0f;
			throwToolCooldown = 0f;
			CancelAttackNotDownspikeBounce();
			if (cState.onGround)
			{
				animCtrl.SetPlayRunToIdle();
			}
		}
	}

	public void QuakeInvuln()
	{
		parryInvulnTimer = INVUL_TIME_QUAKE;
	}

	public void CrossStitchInvuln()
	{
		parryInvulnTimer = INVUL_TIME_CROSS_STITCH;
	}

	public void StartRevengeWindow()
	{
		revengeWindowTimer = REVENGE_WINDOW_TIME;
	}

	public void StartWandererDashComboWindow()
	{
		wandererDashComboWindowTimer = DASHCOMBO_WINDOW_TIME;
	}

	public void ForceSoftLanding()
	{
		animCtrl.playLanding = true;
		PlaySoftLandingEffect();
		BackOnGround();
	}

	public void PlaySoftLandingEffect()
	{
		softLandingEffectPrefab.Spawn(transform.position);
	}

	public void TakeQuickDamage(int damageAmount, bool playEffects)
	{
		TakeQuickDamage(damageAmount, playEffects, allowFracturedMaskBreak: false);
	}

	public void TakeQuickDamage(int damageAmount, bool playEffects, bool allowFracturedMaskBreak)
	{
		if (Time.timeAsDouble - lastHazardRespawnTime <= 0.5)
		{
			lastHazardRespawnTime = Time.timeAsDouble + 1.0;
			Debug.LogError("<b>Possible hazard death loop</b>, canceling quick damage.", this);
		}
		else
		{
			DoSpecialDamage(damageAmount, playEffects, "DAMAGE", canDie: false, allowFracturedMaskBreak, justTakeHealth: false, isFrostDamage: false);
		}
	}

	public void TakeQuickDamageSimple(int damageAmount)
	{
		TakeQuickDamage(damageAmount, playEffects: true);
	}

	public void TakeFrostDamage(int damageAmount)
	{
		DoSpecialDamage(damageAmount, playEffects: true, "FROST DAMAGE", canDie: true, allowFracturedMaskBreak: true, justTakeHealth: true, isFrostDamage: true);
		GameCameras.instance.cameraController.ScreenFlashFrostDamage();
		TimerGroup damageCooldownTimer = frostWaterDamageTag.DamageCooldownTimer;
		if ((bool)damageCooldownTimer)
		{
			damageCooldownTimer.ResetTimer();
		}
	}

	public void TakeChompDamage()
	{
		DoSpecialDamage(1, playEffects: true, "DAMAGE", canDie: true, allowFracturedMaskBreak: true, justTakeHealth: true, isFrostDamage: false);
	}

	public bool ApplyTagDamage(DamageTag.DamageTagInstance damageTagInstance)
	{
		if (damageTagInstance.specialDamageType == DamageTag.SpecialDamageTypes.Frost)
		{
			TakeFrostDamage(damageTagInstance.amount);
		}
		else
		{
			DoSpecialDamage(damageTagInstance.amount, playEffects: true, "DAMAGE", canDie: true, allowFracturedMaskBreak: true, justTakeHealth: true, isFrostDamage: false);
		}
		return true;
	}

	private void DoSpecialDamage(int damageAmount, bool playEffects, string damageEvent, bool canDie, bool allowFracturedMaskBreak, bool justTakeHealth, bool isFrostDamage)
	{
		bool num = !takeNoDamage && CheatManager.Invincibility == CheatManager.InvincibilityStates.Off && ToolItemManager.ActiveState != ToolsActiveStates.Cutscene && !cState.transitioning;
		DoMossToolHit();
		if (num)
		{
			if (!isFrostDamage)
			{
				ToolItem barbedWireTool = Gameplay.BarbedWireTool;
				if ((bool)barbedWireTool && barbedWireTool.IsEquipped)
				{
					damageAmount = Mathf.FloorToInt((float)damageAmount * Gameplay.BarbedWireDamageTakenMultiplier);
				}
			}
			playerData.TakeHealth(damageAmount, IsInLifebloodState, allowFracturedMaskBreak);
			if (justTakeHealth)
			{
				EventRegister.SendEvent(EventRegisterEvents.HealthUpdate);
			}
			else
			{
				HeroDamaged();
			}
			ResetHunterUpgCrestState();
			DeliveryQuestItem.TakeHit(damageAmount);
		}
		if (playEffects)
		{
			damageEffectFSM.SendEvent(damageEvent);
			audioCtrl.PlaySound(HeroSounds.TAKE_HIT, playVibration: true);
			if (isFrostDamage)
			{
				woundFrostAudioTable.SpawnAndPlayOneShot(transform.position);
			}
		}
		EventRegister.SendEvent(EventRegisterEvents.HeroDamagedExtra);
		if (num && canDie)
		{
			if (playerData.health == 0)
			{
				deathAudioTable.SpawnAndPlayOneShot(transform.position);
				StartCoroutine(Die(nonLethal: false, isFrostDamage));
				return;
			}
			DoBindReminder();
		}
		EventRegister.SendEvent(EventRegisterEvents.HealthUpdate);
	}

	public void CriticalDamage()
	{
		if (!takeNoDamage && CheatManager.Invincibility == CheatManager.InvincibilityStates.Off && ToolItemManager.ActiveState != ToolsActiveStates.Cutscene)
		{
			DeliveryQuestItem.BreakAll();
			playerData.healthBlue = 0;
			TakeHealth(playerData.health - 1);
		}
	}

	public void CheckParry(DamageHero damageHero)
	{
		if (damageHero == null)
		{
			return;
		}
		HazardType hazardType = damageHero.hazardType;
		if ((hazardType != HazardType.ENEMY && hazardType != HazardType.EXPLOSION) || damageMode == DamageMode.HAZARD_ONLY || cState.shadowDashing)
		{
			return;
		}
		GameObject gameObject = damageHero.gameObject;
		if (cState.evading && gameObject.layer != 11)
		{
			if (!evadingDidClash)
			{
				evadingDidClash = true;
				GameObject scuttleEvadeEffect = Gameplay.ScuttleEvadeEffect;
				scuttleEvadeEffect.Spawn(transform.position, scuttleEvadeEffect.transform.localRotation).transform.localScale = scuttleEvadeEffect.transform.localScale.MultiplyElements(transform.localScale);
			}
		}
		else if ((!cState.whipLashing || gameObject.layer != 11) && (cState.downspikeInvulnerabilitySteps <= 0 || hazardType != HazardType.ENEMY || ((bool)damageHero && damageHero.noBounceCooldown)) && (!(parryInvulnTimer > 0f) || hazardType != HazardType.ENEMY) && cState.parrying && hazardType == HazardType.ENEMY)
		{
			if (gameObject.transform.position.x > transform.position.x)
			{
				FaceRight();
			}
			else
			{
				FaceLeft();
			}
			silkSpecialFSM.SendEvent("PARRIED");
			cState.parrying = false;
			cState.parryAttack = true;
		}
	}

	public void TakeDamage(GameObject go, CollisionSide damageSide, int damageAmount, HazardType hazardType, DamagePropertyFlags damagePropertyFlags = DamagePropertyFlags.None)
	{
		if ((damagePropertyFlags & DamagePropertyFlags.Self) != DamagePropertyFlags.None && InteractManager.BlockingInteractable != null)
		{
			return;
		}
		if (damageAmount > 0)
		{
			switch (hazardType)
			{
			case HazardType.SPIKES:
			case HazardType.ACID:
				damageAmount = 1;
				break;
			case HazardType.LAVA:
				damageAmount = 2;
				break;
			case HazardType.COAL:
			case HazardType.COAL_SPIKES:
				damageAmount = 1;
				break;
			case HazardType.RESPAWN_PIT:
				damageAmount = 0;
				break;
			case HazardType.ZAP:
				damageAmount = 1;
				break;
			case HazardType.STEAM:
				damageAmount = 2;
				break;
			default:
				if ((damagePropertyFlags & DamagePropertyFlags.Flame) != DamagePropertyFlags.None)
				{
					damageAmount = 2;
				}
				if ((damagePropertyFlags & DamagePropertyFlags.Void) != DamagePropertyFlags.None)
				{
					damageAmount = 2;
				}
				break;
			}
			if (BossSceneController.IsBossScene)
			{
				switch (BossSceneController.Instance.BossLevel)
				{
				case 1:
					damageAmount *= 2;
					break;
				case 2:
					damageAmount = 9999;
					break;
				}
			}
			if (!cState.hazardDeath && hazardType != HazardType.ENEMY && hazardType != HazardType.NON_HAZARD && hazardType != HazardType.EXPLOSION && Time.timeAsDouble - lastHazardRespawnTime <= 0.5)
			{
				if (!string.IsNullOrEmpty(gm.entryGateName))
				{
					foreach (TransitionPoint transitionPoint2 in TransitionPoint.TransitionPoints)
					{
						if (!(transitionPoint2.name != gm.entryGateName))
						{
							playerData.SetHazardRespawn(transitionPoint2.respawnMarker);
							break;
						}
					}
					foreach (RespawnMarker marker in RespawnMarker.Markers)
					{
						if (!(marker.name != gm.entryGateName))
						{
							playerData.SetHazardRespawn(marker.transform.position, marker.respawnFacingRight);
							break;
						}
					}
				}
				else
				{
					List<TransitionPoint> transitionPoints = TransitionPoint.TransitionPoints;
					if (transitionPoints.Count > 0)
					{
						TransitionPoint transitionPoint = transitionPoints[UnityEngine.Random.Range(0, transitionPoints.Count)];
						playerData.SetHazardRespawn(transitionPoint.respawnMarker);
					}
				}
				doingHazardRespawn = true;
				SetState(ActorStates.no_input);
				gm.HazardRespawn();
			}
			else
			{
				if (!CanTakeDamage() && ((!cState.Invulnerable && !(parryInvulnTimer > 0f)) || cState.hazardDeath || playerData.isInvincible || CheatManager.Invincibility == CheatManager.InvincibilityStates.FullInvincible || hazardType == HazardType.ENEMY || hazardType == HazardType.NON_HAZARD || hazardType == HazardType.EXPLOSION || HeroInvincibilitySource.IsActive) && hazardType != HazardType.SINK)
				{
					return;
				}
				DamageHero damageHero = (go ? go.GetComponent<DamageHero>() : null);
				RandomAudioClipTable randomAudioClipTable = woundAudioTable;
				if (hazardType == HazardType.ENEMY || hazardType == HazardType.EXPLOSION)
				{
					if (damageMode == DamageMode.HAZARD_ONLY || cState.shadowDashing)
					{
						return;
					}
					if (cState.evading && go.layer != 11)
					{
						if (!evadingDidClash)
						{
							evadingDidClash = true;
							GameObject scuttleEvadeEffect = Gameplay.ScuttleEvadeEffect;
							scuttleEvadeEffect.Spawn(this.transform.position, scuttleEvadeEffect.transform.localRotation).transform.localScale = scuttleEvadeEffect.transform.localScale.MultiplyElements(this.transform.localScale);
						}
						return;
					}
					if ((cState.whipLashing && go.layer == 11) || (cState.downspikeInvulnerabilitySteps > 0 && hazardType == HazardType.ENEMY && (!damageHero || !damageHero.noBounceCooldown)) || (parryInvulnTimer > 0f && hazardType == HazardType.ENEMY))
					{
						return;
					}
					if (cState.parrying && hazardType == HazardType.ENEMY)
					{
						if (go.transform.position.x > this.transform.position.x)
						{
							FaceRight();
						}
						else
						{
							FaceLeft();
						}
						silkSpecialFSM.SendEvent("PARRIED");
						cState.parrying = false;
						cState.parryAttack = true;
						return;
					}
					if (cState.parryAttack)
					{
						return;
					}
					if (WillDoBellBindHit())
					{
						bellBindFSM.SendEvent("HIT");
						damageAmount = 0;
						randomAudioClipTable = null;
					}
				}
				ToolItem barbedWireTool = Gameplay.BarbedWireTool;
				if ((bool)barbedWireTool && barbedWireTool.IsEquipped)
				{
					damageAmount = Mathf.FloorToInt((float)damageAmount * Gameplay.BarbedWireDamageTakenMultiplier);
				}
				if ((hazardType == HazardType.SPIKES || hazardType == HazardType.COAL_SPIKES) && cState.downTravelling && (bool)go.GetComponent<TinkEffect>())
				{
					return;
				}
				if (Gameplay.LuckyDiceTool.IsEquipped && hazardType == HazardType.ENEMY)
				{
					int luckyDiceShieldThreshold = GetLuckyDiceShieldThreshold(luckyDiceShieldedHits);
					bool flag = UnityEngine.Random.Range(1, 100) <= luckyDiceShieldThreshold;
					if (CheatManager.SuperLuckyDice)
					{
						flag = true;
					}
					if (flag)
					{
						luckyDiceShieldedHits = 0;
						spawnedLuckyDiceShieldEffect.SetActive(value: false);
						spawnedLuckyDiceShieldEffect.SetActive(value: true);
						damageAmount = 0;
					}
					else
					{
						luckyDiceShieldedHits++;
					}
				}
				else
				{
					luckyDiceShieldedHits = 0;
				}
				VibrationManager.GetMixer()?.StopAllEmissionsWithTag("heroAction");
				SetAllowNailChargingWhileRelinquished(value: false);
				bool flag2 = false;
				if (damageAmount > 0)
				{
					audioCtrl.PlaySound(HeroSounds.TAKE_HIT, playVibration: true);
					tagDamageTaker.RemoveDamageTagFromStack(acidDamageTag);
					if ((damagePropertyFlags & DamagePropertyFlags.Acid) != DamagePropertyFlags.None)
					{
						tagDamageTaker.AddDamageTagToStack(acidDamageTag);
					}
					bool flag3 = hazardType == HazardType.LAVA || (damagePropertyFlags & DamagePropertyFlags.Flame) != 0;
					if (flag3)
					{
						if (damageAmount > 1)
						{
							if (IsLavaBellActive())
							{
								flag2 = true;
								damageAmount /= 2;
								UseLavaBell();
								StatusVignette.AddTempStatus(StatusVignette.TempStatusTypes.FlameDamageLavaBell);
							}
							else
							{
								GameObject gameObject = afterDamageEffectsPrefab.Spawn(this.transform.position);
								if ((bool)gameObject)
								{
									gameObject.SetActiveChildren(value: false);
									Transform transform = gameObject.transform.Find("Flame");
									if ((bool)transform)
									{
										transform.gameObject.SetActive(value: true);
									}
								}
								StatusVignette.AddTempStatus(StatusVignette.TempStatusTypes.FlameDamage);
							}
						}
						else
						{
							StatusVignette.AddTempStatus(StatusVignette.TempStatusTypes.FlameDamage);
						}
					}
					if ((damagePropertyFlags & DamagePropertyFlags.SilkAcid) == DamagePropertyFlags.SilkAcid && (bool)spawnedSilkAcid)
					{
						spawnedSilkAcid.SetActive(value: false);
						spawnedSilkAcid.SetActive(value: true);
					}
					if ((damagePropertyFlags & DamagePropertyFlags.Void) != DamagePropertyFlags.None)
					{
						ActivateVoidAcid();
					}
					if (!takeNoDamage && (CheatManager.Invincibility == CheatManager.InvincibilityStates.Off || CheatManager.Invincibility == CheatManager.InvincibilityStates.PreventDeath) && !gm.sm.HeroKeepHealth && ToolItemManager.ActiveState != ToolsActiveStates.Cutscene)
					{
						playerData.TakeHealth(damageAmount, IsInLifebloodState, (damagePropertyFlags & DamagePropertyFlags.MidChain) == 0);
					}
					SendHeroDamagedEvent(new DamageInfo(hazardType));
					DoMossToolHit();
					ResetHunterUpgCrestState();
					if (warriorState.IsInRageMode)
					{
						warriorState.RageEffectTimeLeft -= Gameplay.WarriorRageDamagedRemoveTime;
						warriorState.RageHealTimeLeft -= Gameplay.WarriorRageDamagedRemoveTime;
					}
					if (reaperState.IsInReaperMode)
					{
						ResetReaperCrestState();
					}
					DeliveryQuestItem.TakeHit(damageAmount);
					if (damageAmount <= 1)
					{
						takeHitSingleEffectPrefab.Spawn(this.transform.position);
					}
					else
					{
						takeHitDoubleEffectPrefab.Spawn(this.transform.position);
						randomAudioClipTable = woundHeavyAudioTable;
						if ((damagePropertyFlags & DamagePropertyFlags.Void) != DamagePropertyFlags.None)
						{
							takeHitDoubleBlackThreadEffectPrefab.Spawn(this.transform.position);
						}
						else if (flag3)
						{
							takeHitDoubleFlameEffectPrefab.Spawn(this.transform.position);
						}
					}
					if (Gameplay.ThiefCharmTool.IsEquipped)
					{
						float randomValue = Gameplay.ThiefCharmGeoLoss.GetRandomValue();
						int num = Mathf.CeilToInt((float)playerData.geo * randomValue);
						int num2 = Gameplay.ThiefCharmGeoLossCap.GetRandomValue();
						if (damageAmount > 1)
						{
							num *= 2;
							num2 = Mathf.FloorToInt((float)num2 * 1.5f);
						}
						if (num > num2)
						{
							num = num2;
						}
						if (UnityEngine.Random.Range(0f, 1f) <= Gameplay.ThiefCharmGeoLossLooseChance)
						{
							int randomValue2 = Gameplay.ThiefCharmGeoLossLooseAmount.GetRandomValue();
							num += randomValue2;
							FlingUtils.SpawnAndFling(new FlingUtils.Config
							{
								Prefab = Gameplay.SmallGeoPrefab,
								AmountMin = randomValue2,
								AmountMax = randomValue2,
								SpeedMin = 10f,
								SpeedMax = 40f,
								AngleMin = 65f,
								AngleMax = 115f
							}, this.transform, Vector3.zero);
						}
						TakeGeo(num);
						GameObject thiefCharmHeroHitPrefab = Gameplay.ThiefCharmHeroHitPrefab;
						if ((bool)thiefCharmHeroHitPrefab)
						{
							Vector3 position = this.transform.position;
							position.z = thiefCharmHeroHitPrefab.transform.position.z;
							thiefCharmHeroHitPrefab.Spawn(position);
						}
					}
					if ((bool)damageHero)
					{
						damageHero.SendHeroDamagedEvent();
					}
				}
				HeroDamaged();
				CancelAttack();
				RegainControl();
				StartAnimationControl();
				if (cState.wallSliding)
				{
					cState.wallSliding = false;
					cState.wallClinging = false;
					vibrationCtrl.StopWallSlide();
					heroBox.HeroBoxNormal();
				}
				if (cState.touchingWall)
				{
					cState.touchingWall = false;
				}
				if (cState.recoilingLeft || cState.recoilingRight)
				{
					CancelRecoilHorizontal();
				}
				if (cState.bouncing)
				{
					CancelBounce();
					rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, 0f);
				}
				if (cState.shroomBouncing)
				{
					CancelBounce();
					rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, 0f);
				}
				if (cState.nailCharging || nailChargeTimer != 0f)
				{
					CancelNailCharge();
				}
				if (playerData.health == 0)
				{
					deathAudioTable.SpawnAndPlayOneShot(this.transform.position);
					if ((bool)audioSource && (bool)deathImpactClip)
					{
						audioSource.PlayOneShot(deathImpactClip, 1f);
					}
					StartCoroutine(Die((damagePropertyFlags & DamagePropertyFlags.NonLethal) != 0, frostDeath: false));
					return;
				}
				DoBindReminder();
				if (flag2)
				{
					GameObject prefab = lavaBellEffectPrefab;
					Vector3 position2 = this.transform.position;
					float? z = lavaBellEffectPrefab.transform.position.z;
					prefab.Spawn(position2.Where(null, null, z));
				}
				switch (hazardType)
				{
				case HazardType.SPIKES:
					StartCoroutine(DieFromHazard(HazardType.SPIKES, (go != null) ? go.transform.rotation.z : 0f));
					return;
				case HazardType.COAL_SPIKES:
					StartCoroutine(DieFromHazard(HazardType.COAL_SPIKES, (go != null) ? go.transform.rotation.z : 0f));
					return;
				case HazardType.ACID:
					StartCoroutine(DieFromHazard(HazardType.ACID, 0f));
					return;
				case HazardType.LAVA:
					StartCoroutine(DieFromHazard(HazardType.LAVA, 0f));
					return;
				case HazardType.PIT:
					StartCoroutine(DieFromHazard(HazardType.PIT, 0f));
					return;
				case HazardType.COAL:
					StartCoroutine(DieFromHazard(HazardType.COAL, 0f));
					return;
				case HazardType.ZAP:
					StartCoroutine(DieFromHazard(HazardType.ZAP, 0f));
					return;
				case HazardType.SINK:
					StartCoroutine(DieFromHazard(HazardType.SINK, 0f));
					return;
				case HazardType.STEAM:
					StartCoroutine(DieFromHazard(HazardType.STEAM, 0f));
					return;
				case HazardType.RESPAWN_PIT:
					StartCoroutine(DieFromHazard(HazardType.PIT, 0f));
					return;
				}
				if ((bool)randomAudioClipTable)
				{
					randomAudioClipTable.SpawnAndPlayOneShot(this.transform.position);
				}
				if ((bool)vibrationCtrl)
				{
					vibrationCtrl.PlayHeroDamage();
				}
				if (!cState.recoiling)
				{
					recoilRoutine = StartCoroutine(StartRecoil(damageSide, damageAmount));
				}
			}
		}
		else
		{
			DamageHero component = go.GetComponent<DamageHero>();
			if ((bool)component && component.AlwaysSendDamaged)
			{
				component.SendHeroDamagedEvent();
			}
		}
	}

	public void CheckDeathCatch()
	{
		if (!cState.dead && playerData.health <= 0)
		{
			StartCoroutine(Die(nonLethal: false, frostDeath: false));
		}
	}

	public void ActivateVoidAcid()
	{
		if ((bool)spawnedVoidAcid)
		{
			spawnedVoidAcid.SetActive(value: false);
			spawnedVoidAcid.SetActive(value: true);
		}
	}

	public bool IsLavaBellActive()
	{
		if (Gameplay.LavaBellTool.Status.IsEquipped)
		{
			return lavaBellCooldownTimeLeft <= 0f;
		}
		return false;
	}

	public void UseLavaBell()
	{
		lavaBellCooldownTimeLeft = Gameplay.LavaBellCooldownTime;
		EventRegister.SendEvent(EventRegisterEvents.LavaBellUsed);
	}

	private static int GetLuckyDiceShieldThreshold(int hits)
	{
		return hits switch
		{
			0 => 0, 
			1 => 2, 
			2 => 4, 
			3 => 6, 
			4 => 8, 
			_ => 10, 
		};
	}

	public float GetLuckModifier()
	{
		float num = 1f;
		if (Gameplay.LuckyDiceTool.Status.IsEquipped)
		{
			num += 0.1f;
		}
		return num;
	}

	public void DamageSelf(int amount)
	{
		TakeDamage(base.gameObject, (!cState.facingRight) ? CollisionSide.left : CollisionSide.right, amount, HazardType.ENEMY);
	}

	public bool CanTryHarpoonDash()
	{
		return Time.timeAsDouble > HARPOON_DASH_TIME;
	}

	private void HeroDamaged()
	{
		proxyFSM.SendEvent("HeroCtrl-HeroDamaged");
		CancelAttack();
		ResetTauntEffects();
		CancelFallEffects();
		HARPOON_DASH_TIME = Time.timeAsDouble + 0.10000000149011612;
		frostDamageTimer = 0f;
		if ((bool)vibrationCtrl)
		{
			vibrationCtrl.PlayHeroDamage();
		}
	}

	public void SendHeroDamagedEvent()
	{
		SendHeroDamagedEvent(default(DamageInfo));
	}

	public void SendHeroDamagedEvent(DamageInfo damageInfo)
	{
		this.OnTakenDamage?.Invoke();
		this.OnTakenDamageExtra?.Invoke(damageInfo);
	}

	private void HeroRespawned()
	{
		playerData.disableSaveQuit = false;
		proxyFSM.SendEvent("HeroCtrl-Respawned");
		CancelAttack();
	}

	private void DoBindReminder()
	{
		if (!playerData.SeenBindPrompt && !IsRefillSoundsSuppressed && playerData.health < 3 && playerData.silk >= 9)
		{
			EventRegister.SendEvent(EventRegisterEvents.ReminderBind);
			playerData.SeenBindPrompt = true;
		}
	}

	public bool WillDoBellBindHit()
	{
		return WillDoBellBindHit(sendEvent: false);
	}

	public bool WillDoBellBindHit(bool sendEvent)
	{
		if (!bellBindFSM)
		{
			return false;
		}
		bool num = bellBindFSM.FsmVariables.FindFsmBool("Is Shielding")?.Value ?? false;
		if (num && sendEvent)
		{
			bellBindFSM.SendEvent("HIT");
		}
		return num;
	}

	private void DoMossToolHit()
	{
		if (playerData.health > 0 && playerData.silk < playerData.CurrentSilkMax && (DoHitForMossTool(Gameplay.MossCreep1Tool, ref mossCreep1Hits, 1) || DoHitForMossTool(Gameplay.MossCreep2Tool, ref mossCreep2Hits, 2)) && (bool)mossDamageEffectPrefab)
		{
			mossDamageEffectPrefab.Spawn(transform, Vector3.zero, mossDamageEffectPrefab.transform.rotation);
		}
		bool DoHitForMossTool(ToolItem tool, ref int hitTracker, int silk)
		{
			if (!tool || !tool.Status.IsEquipped)
			{
				return false;
			}
			hitTracker++;
			if (hitTracker >= 2)
			{
				hitTracker = 0;
				AddSilk(silk, heroEffect: false, SilkSpool.SilkAddSource.Moss);
			}
			else
			{
				GameCameras.instance.silkSpool.SetMossState(hitTracker, silk);
			}
			return true;
		}
	}

	public string GetEntryGateName()
	{
		if (sceneEntryGate != null)
		{
			return sceneEntryGate.name;
		}
		return "";
	}

	public float GetShuttlecockTime()
	{
		return shuttlecockTime;
	}

	public void SilkGain()
	{
		AddSilk(1, heroEffect: false);
	}

	public void SilkGain(HitInstance hitInstance)
	{
		switch (hitInstance.SilkGeneration)
		{
		case HitSilkGeneration.Full:
			AddSilk(1, heroEffect: false);
			break;
		case HitSilkGeneration.FirstHit:
			if (hitInstance.IsFirstHit)
			{
				AddSilk(1, heroEffect: false);
			}
			break;
		default:
			throw new ArgumentOutOfRangeException();
		case HitSilkGeneration.None:
			break;
		}
	}

	public void NailHitEnemy(HealthManager enemyHealth, HitInstance hitInstance)
	{
		if (cState.isMaggoted)
		{
			return;
		}
		if (hitInstance.RageHit)
		{
			if (enemyHealth.ShouldIgnore(HealthManager.IgnoreFlags.RageHeal) || (warriorState.LastHealAttack == cState.attackCount && hitInstance.IsFirstHit))
			{
				return;
			}
			warriorState.LastHealAttack = cState.attackCount;
			if (warriorState.RageModeHealCount < GetRageModeHealCap() && playerData.health < playerData.CurrentMaxHealth)
			{
				int num = warriorState.RageModeHealCount;
				warriorState.RageModeHealCount++;
				AddHealth(1);
				Effects.RageHitHealthEffectPrefab.Spawn(enemyHealth.transform.position);
				spriteFlash.flashFocusHeal();
				RestartWarriorRageEffect();
				IReadOnlyList<float> warriorRageHitAddTimePerHit = Gameplay.WarriorRageHitAddTimePerHit;
				if (num >= warriorRageHitAddTimePerHit.Count)
				{
					num = warriorRageHitAddTimePerHit.Count - 1;
				}
				float num2 = warriorRageHitAddTimePerHit[num];
				if (!(num2 <= Mathf.Epsilon))
				{
					warriorState.RageEffectTimeLeft += num2;
					warriorState.RageHealTimeLeft += num2;
				}
			}
		}
		else if ((Gameplay.HunterCrest2.IsEquipped || Gameplay.HunterCrest3.IsEquipped) && hitInstance.SilkGeneration != HitSilkGeneration.None && !enemyHealth.DoNotGiveSilk)
		{
			hunterUpgState.CurrentMeterHits++;
		}
	}

	public int GetRageModeHealCap()
	{
		if (Gameplay.MultibindTool.IsEquipped)
		{
			return 4;
		}
		return 3;
	}

	public int GetWitchHealCap()
	{
		if (!Gameplay.MultibindTool.IsEquippedHud)
		{
			return 3;
		}
		return 4;
	}

	public int GetReaperPayout()
	{
		if (!reaperState.IsInReaperMode)
		{
			return 0;
		}
		return Probability.GetRandomItemByProbability<Probability.ProbabilityInt, int>(reaperBundleDrops);
	}

	private void ResetAllCrestStateMinimal(bool clearHunterState)
	{
		ResetWarriorCrestState();
		ResetReaperCrestState();
		if (clearHunterState)
		{
			ResetHunterUpgCrestState();
		}
		ResetWandererCrestState();
	}

	public void ResetAllCrestState()
	{
		ResetAllCrestState(!ToolItemManager.IsChangingActiveState);
	}

	public void ResetAllCrestState(bool clearHunterState)
	{
		ResetAllCrestStateMinimal(clearHunterState);
		ToolCrest crestByName = ToolItemManager.GetCrestByName(PlayerData.instance.CurrentCrestID);
		if (crestByName != null)
		{
			crestConfig = crestByName.HeroConfig;
		}
		UpdateConfig();
	}

	private void ResetWarriorRageEffect()
	{
		bool isInRageMode = warriorState.IsInRageMode;
		if (isInRageMode && (bool)rageModeEffect)
		{
			rageModeEffect.Fade();
		}
		if (isInRageMode)
		{
			StatusVignette.RemoveStatus(StatusVignette.StatusTypes.InRageMode);
			EventRegister.SendEvent(EventRegisterEvents.WarriorRageEnded);
		}
	}

	private void ResetWarriorCrestState()
	{
		ResetWarriorRageEffect();
		warriorState = default(WarriorCrestStateInfo);
	}

	private void ResetReaperCrestState()
	{
		if (reaperState.IsInReaperMode && (bool)reaperModeEffect)
		{
			reaperModeEffect.Fade();
		}
		reaperState = default(ReaperCrestStateInfo);
	}

	public void SetSilkPartsTimeLeft(float delay)
	{
		silkPartsTimeLeft = delay;
	}

	private void ResetHunterUpgCrestState()
	{
		hunterUpgState = default(HunterUpgCrestStateInfo);
	}

	private void ResetWandererCrestState()
	{
		WandererState = default(WandererCrestStateInfo);
	}

	public void AddSilk(int amount, bool heroEffect)
	{
		AddSilk(amount, heroEffect, SilkSpool.SilkAddSource.Normal);
	}

	public void ReduceOdours(int amount)
	{
		playerData.ReduceOdours(amount);
	}

	public void AddSilk(int amount, bool heroEffect, SilkSpool.SilkAddSource source)
	{
		AddSilk(amount, heroEffect, source, forceCanBindEffect: false);
	}

	public void AddSilk(int amount, bool heroEffect, SilkSpool.SilkAddSource source, bool forceCanBindEffect)
	{
		int silk = playerData.silk;
		playerData.AddSilk(amount);
		ResetSilkRegen();
		if (heroEffect)
		{
			spriteFlash.flashFocusHeal();
		}
		int num = (Gameplay.WitchCrest.IsEquipped ? 9 : 9);
		if ((forceCanBindEffect || silk < num) && playerData.silk >= num && !IsRefillSoundsSuppressed && ToolItemManager.ActiveState == ToolsActiveStates.Active && renderer.enabled)
		{
			canBindEffect.SetActive(value: false);
			canBindEffect.SetActive(value: true);
		}
		SilkSpool silkSpool = GameCameras.instance.silkSpool;
		silkSpool.RefreshSilk(source, SilkSpool.SilkTakeSource.Normal);
		if (playerData.silk == playerData.CurrentSilkMax || (playerData.silk == playerData.CurrentSilkMax - 1 && playerData.silkParts > 0))
		{
			mossCreep1Hits = 0;
			mossCreep2Hits = 0;
			silkSpool.SetMossState(0);
		}
		DoBindReminder();
	}

	public void RefillSilkToMax()
	{
		int num = playerData.CurrentSilkMax - playerData.silk;
		if (num > 0)
		{
			AddSilk(num, heroEffect: false, SilkSpool.SilkAddSource.Normal);
		}
	}

	public void RefreshSilk()
	{
		int num = playerData.silk - playerData.CurrentSilkMax;
		if (num > 0)
		{
			TakeSilk(num);
		}
	}

	public void AddFinalMaxSilk()
	{
		AddSilk(1, heroEffect: false);
	}

	public void AddSilkParts(int amount)
	{
		if (playerData.silk < playerData.CurrentSilkMax)
		{
			playerData.silkParts += amount;
			if (playerData.silkParts >= 3)
			{
				playerData.silkParts = 0;
				AddSilk(1, heroEffect: false);
			}
			else
			{
				ResetSilkRegen();
				GameCameras.instance.silkSpool.RefreshSilk();
			}
		}
	}

	public void AddSilkParts(int amount, bool heroEffect)
	{
		AddSilkParts(amount);
		if (heroEffect)
		{
			spriteFlash.flashFocusGet();
		}
	}

	public void TakeSilk(int amount)
	{
		TakeSilk(amount, SilkSpool.SilkTakeSource.Normal);
	}

	public void TakeSilk(int amount, SilkSpool.SilkTakeSource source)
	{
		if (ToolItemManager.ActiveState != ToolsActiveStates.Cutscene)
		{
			playerData.TakeSilk(amount);
			if (playerData.silk < 0)
			{
				playerData.silk = 0;
			}
			GameCameras.instance.silkSpool.RefreshSilk(SilkSpool.SilkAddSource.Normal, source);
			ResetSilkRegen();
		}
	}

	public void ClearSpoolMossChunks()
	{
		mossCreep1Hits = 0;
		mossCreep2Hits = 0;
		GameCameras.instance.silkSpool.SetMossState(0);
	}

	public void MaxRegenSilk()
	{
		if (playerData.CurrentSilkRegenMax - playerData.silk > 0)
		{
			doMaxSilkRegen = true;
			maxSilkRegenTimer = 0f;
		}
	}

	public void MaxRegenSilkInstant()
	{
		int num = playerData.CurrentSilkRegenMax - playerData.silk;
		if (num > 0)
		{
			AddSilk(num, heroEffect: false);
		}
	}

	private void StartSilkRegen()
	{
		int currentSilkRegenMax = playerData.CurrentSilkRegenMax;
		if (playerData.silk < currentSilkRegenMax && playerData.silk < playerData.CurrentSilkMax)
		{
			float num = SILK_REGEN_DURATION;
			float num2 = FIRST_SILK_REGEN_DURATION;
			if (Gameplay.WhiteRingTool.IsEquipped)
			{
				num *= Gameplay.WhiteRingSilkRegenTimeMultiplier;
				num2 *= Gameplay.WhiteRingSilkRegenTimeMultiplier;
			}
			silkRegenDurationLeft = ((playerData.silk > 0 && !isNextSilkRegenUpgraded) ? num : num2);
			SilkSpool.Instance.SetRegen(1, isNextSilkRegenUpgraded);
		}
		else
		{
			ResetSilkRegen();
		}
	}

	private void DoSilkRegen()
	{
		AddSilk(1, heroEffect: false);
		EventRegister.SendEvent(EventRegisterEvents.RegeneratedSilkChunk);
		isNextSilkRegenUpgraded = false;
	}

	private void ResetSilkRegen()
	{
		float num = SILK_REGEN_DELAY;
		float num2 = FIRST_SILK_REGEN_DELAY;
		if (Gameplay.WhiteRingTool.IsEquipped)
		{
			num *= Gameplay.WhiteRingSilkRegenTimeMultiplier;
			num2 *= Gameplay.WhiteRingSilkRegenTimeMultiplier;
		}
		silkRegenDelayLeft = ((playerData.silk > 0 && !isNextSilkRegenUpgraded) ? num : num2);
		silkRegenDurationLeft = 0f;
		SilkSpool silkSpool = SilkSpool.Instance;
		if ((bool)silkSpool)
		{
			silkSpool.SetRegen(0, isNextSilkRegenUpgraded);
		}
	}

	public void SetSilkRegenBlocked(bool isBlocked)
	{
		bool flag = false;
		if (isSilkRegenBlocked || isBlocked)
		{
			ResetSilkRegen();
			flag = true;
		}
		isSilkRegenBlocked = isBlocked;
		if (!isBlocked && !flag && silkRegenDelayLeft == 0f && silkRegenDurationLeft == 0f)
		{
			StartSilkRegen();
		}
	}

	public void SetSilkRegenBlockedSilkHeart(bool isBlocked)
	{
		if (!isBlocked)
		{
			isNextSilkRegenUpgraded = true;
		}
		SetSilkRegenBlocked(isBlocked);
		if (isBlocked)
		{
			TakeSilk(999);
			AddSilk(playerData.CurrentSilkRegenMax - 1, heroEffect: false);
		}
	}

	public void UpdateSilkCursed()
	{
		ResetSilkRegen();
		EventRegister.SendEvent(EventRegisterEvents.SilkCursedUpdate);
	}

	public void AddHealth(int amount)
	{
		playerData.AddHealth(amount);
		EventRegister.SendEvent(EventRegisterEvents.HeroHealed);
	}

	public void RefillHealthToMax()
	{
		int num = playerData.CurrentMaxHealth - playerData.health;
		if (num > 0)
		{
			AddHealth(num);
		}
	}

	public void SuppressRefillSound(int frames)
	{
		refillSoundSuppressFramesLeft = frames;
	}

	public void RefillAll()
	{
		SuppressRefillSound(2);
		RefillHealthToMax();
		RefillSilkToMax();
	}

	public void RefillSilkToMaxSilent()
	{
		SuppressRefillSound(2);
		RefillSilkToMax();
	}

	public void BindCompleted()
	{
		if (Gameplay.WarriorCrest.IsEquipped)
		{
			if (!warriorState.IsInRageMode)
			{
				AddFrost(-15f);
				StatusVignette.AddStatus(StatusVignette.StatusTypes.InRageMode);
			}
			warriorState = default(WarriorCrestStateInfo);
			warriorState.IsInRageMode = true;
			warriorState.RageEffectTimeLeft = Gameplay.WarriorRageDuration;
			warriorState.RageHealTimeLeft = Gameplay.WarriorRageDuration + Gameplay.WarriorRageGraceTime;
			RestartWarriorRageEffect();
			BrightnessEffect component = gm.cameraCtrl.GetComponent<BrightnessEffect>();
			component.ExtraEffectFadeTo(1.15f, 1.15f, 0f, 0f);
			component.ExtraEffectFadeTo(1f, 1f, 2f, 0.3f);
			EventRegister.SendEvent(EventRegisterEvents.WarriorRageStarted);
		}
		if (Gameplay.ReaperCrest.IsEquipped)
		{
			reaperState.IsInReaperMode = true;
			reaperState.ReaperModeDurationLeft = Gameplay.ReaperModeDuration;
			if ((bool)reaperModeEffect)
			{
				reaperModeEffect.gameObject.SetActive(value: false);
				reaperModeEffect.gameObject.SetActive(value: true);
			}
		}
	}

	private void RestartWarriorRageEffect()
	{
		if ((bool)rageModeEffect)
		{
			rageModeEffect.gameObject.SetActive(value: false);
			rageModeEffect.gameObject.SetActive(value: true);
		}
	}

	public void BindInterrupted()
	{
		mossCreep1Hits = 0;
		mossCreep2Hits = 0;
		GameCameras.instance.silkSpool.SetMossState(0);
		playerData.silkParts = 0;
	}

	public void TakeHealth(int amount)
	{
		playerData.TakeHealth(amount, IsInLifebloodState, allowFracturedMaskBreak: true);
		HeroDamaged();
	}

	public void MaxHealth()
	{
		ResetLifebloodState();
		proxyFSM.SendEvent("HeroCtrl-MaxHealth");
		playerData.MaxHealth();
		UpdateBlueHealth();
		EventRegister.SendEvent(EventRegisterEvents.HeroHealedToMax);
		ResetAllCrestState();
		ResetMaggotCharm();
	}

	public void MaxHealthKeepBlue()
	{
		int healthBlue = playerData.healthBlue;
		playerData.MaxHealth();
		UpdateBlueHealth();
		playerData.healthBlue = healthBlue;
		EventRegister.SendEvent(EventRegisterEvents.HeroHealed);
	}

	public void AddToMaxHealth(int amount)
	{
		playerData.AddToMaxHealth(amount);
		gm.QueueAchievement("FIRST_MASK");
		int current = (playerData.maxHealthBase - 5) * 4;
		gm.QueueAchievementProgress("ALL_MASKS", current, 20);
	}

	public void AddToMaxSilk(int amount)
	{
		playerData.silkMax += amount;
		gm.QueueAchievement("FIRST_SILK_SPOOL");
		int current = (playerData.silkMax - 9) * 2;
		gm.QueueAchievementProgress("ALL_SILK_SPOOLS", current, 18);
	}

	public void AddToMaxSilkRegen(int amount)
	{
		playerData.silkRegenMax += amount;
		gm.QueueAchievementProgress("ALL_SILK_HEARTS", playerData.silkRegenMax, 3);
	}

	public bool IsHealthCritical()
	{
		ToolItem fracturedMaskTool = Gameplay.FracturedMaskTool;
		if ((bool)fracturedMaskTool && fracturedMaskTool.IsEquipped && fracturedMaskTool.SavedData.AmountLeft > 0)
		{
			return false;
		}
		return playerData.health + playerData.healthBlue <= CriticalHealthValue;
	}

	public void DownspikeBounce(bool harpoonRecoil, HeroSlashBounceConfig bounceConfig = null)
	{
		animCtrl.ResetDownspikeBounce();
		cState.jumping = true;
		cState.bouncing = false;
		downspike_rebound_steps = 0;
		cState.downSpikeBouncing = true;
		allowAttackCancellingDownspikeRecovery = false;
		ResetAirMoves();
		downspike_rebound_xspeed = 0f;
		if (bounceConfig == null)
		{
			bounceConfig = HeroSlashBounceConfig.Default;
		}
		jump_steps = bounceConfig.JumpSteps;
		jumped_steps = bounceConfig.JumpedSteps;
		jumpQueueSteps = 0;
		downspikeBurstPrefab.SetActive(value: false);
		bool standardRecovery = !harpoonRecoil;
		FinishDownspike(standardRecovery);
		BecomeAirborne();
	}

	public void DownspikeBounceSlightlyShort(bool harpoonRecoil)
	{
		cState.jumping = true;
		downspike_rebound_steps = 0;
		cState.downSpikeBouncing = true;
		cState.downSpikeBouncingShort = true;
		allowAttackCancellingDownspikeRecovery = false;
		ResetAirMoves();
		if (harpoonRecoil)
		{
			if (transform.localScale.x > 0f)
			{
				downspike_rebound_xspeed = DOWNSPIKE_REBOUND_SPEED;
			}
			else
			{
				downspike_rebound_xspeed = 0f - DOWNSPIKE_REBOUND_SPEED;
			}
		}
		else
		{
			downspike_rebound_xspeed = 0f;
		}
		jump_steps = 5;
		jumped_steps = -20;
		jumpQueueSteps = 0;
		downspikeBurstPrefab.SetActive(value: false);
		FinishDownspike();
		Vector2 linearVelocity = rb2d.linearVelocity;
		linearVelocity.y = 0f;
		rb2d.linearVelocity = linearVelocity;
	}

	public void DownspikeBounceShort(bool harpoonRecoil)
	{
		cState.jumping = true;
		downspike_rebound_steps = 0;
		cState.downSpikeBouncing = true;
		cState.downSpikeBouncingShort = true;
		allowAttackCancellingDownspikeRecovery = false;
		ResetAirMoves();
		if (harpoonRecoil)
		{
			if (transform.localScale.x > 0f)
			{
				downspike_rebound_xspeed = DOWNSPIKE_REBOUND_SPEED;
			}
			else
			{
				downspike_rebound_xspeed = 0f - DOWNSPIKE_REBOUND_SPEED;
			}
		}
		else
		{
			downspike_rebound_xspeed = 0f;
		}
		jump_steps = 7;
		jumped_steps = -20;
		jumpQueueSteps = 0;
		downspikeBurstPrefab.SetActive(value: false);
		FinishDownspike();
	}

	public bool Bounce()
	{
		if (!cState.bouncing && !cState.shroomBouncing && !controlReqlinquished)
		{
			doubleJumped = false;
			airDashed = false;
			cState.bouncing = true;
			return true;
		}
		return false;
	}

	public void BounceShort()
	{
		if (Bounce())
		{
			bounceTimer = BOUNCE_TIME * 0.5f;
		}
	}

	public void BounceHigh()
	{
		if (!cState.bouncing && !controlReqlinquished)
		{
			doubleJumped = false;
			airDashed = false;
			cState.bouncing = true;
			bounceTimer = -0.03f;
			rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, BOUNCE_VELOCITY);
		}
	}

	public void ShroomBounce()
	{
		doubleJumped = false;
		airDashed = false;
		cState.bouncing = false;
		cState.shroomBouncing = true;
		rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, SHROOM_BOUNCE_VELOCITY);
	}

	public void RecoilLeft()
	{
		if (CanRecoil())
		{
			Recoil(isRight: false, isLong: false);
		}
	}

	public void RecoilRight()
	{
		if (CanRecoil())
		{
			Recoil(isRight: true, isLong: false);
		}
	}

	public void RecoilRightLong()
	{
		if (CanRecoil())
		{
			Recoil(isRight: true, isLong: true);
		}
	}

	public void RecoilLeftLong()
	{
		if (CanRecoil())
		{
			Recoil(isRight: false, isLong: true);
		}
	}

	private bool CanRecoil()
	{
		if (!CanCustomRecoil())
		{
			return false;
		}
		if ((cState.recoilingDrill || (!cState.recoilingLeft && !cState.recoilingRight)) && (!controlReqlinquished || allowRecoilWhileRelinquished))
		{
			return InteractManager.BlockingInteractable == null;
		}
		return false;
	}

	public bool CanCustomRecoil()
	{
		return Time.timeAsDouble >= recoilAllowTime;
	}

	public void PreventRecoil(float duration)
	{
		double num = Time.timeAsDouble + (double)duration;
		if (num > recoilAllowTime)
		{
			recoilAllowTime = num;
		}
	}

	public void AllowRecoil()
	{
		recoilAllowTime = 0.0;
	}

	private void Recoil(bool isRight, bool isLong)
	{
		int steps = RECOIL_HOR_STEPS;
		ToolItem weightedAnkletTool = Gameplay.WeightedAnkletTool;
		ToolCrest witchCrest = Gameplay.WitchCrest;
		ToolCrest cursedCrest = Gameplay.CursedCrest;
		bool flag = false;
		if (witchCrest.IsEquipped || cursedCrest.IsEquipped)
		{
			flag = true;
		}
		if (weightedAnkletTool.IsEquipped)
		{
			steps = ((!flag) ? Gameplay.WeightedAnkletRecoilSteps : 0);
		}
		else if (flag)
		{
			steps = Gameplay.WitchCrestRecoilSteps;
		}
		if (isLong)
		{
			Recoil(isRight, steps, RECOIL_HOR_VELOCITY_LONG);
			ResetAttacks();
		}
		else
		{
			Recoil(isRight, steps, RECOIL_HOR_VELOCITY);
		}
	}

	public void DrillDash(bool isRight)
	{
		float x = rb2d.linearVelocity.x;
		if (x < 6f && x > -6f)
		{
			Recoil(isRight, RECOIL_HOR_STEPS, RECOIL_HOR_VELOCITY_DRILLDASH);
		}
		else
		{
			Recoil(isRight, RECOIL_HOR_STEPS, RECOIL_HOR_VELOCITY_DRILLDASH * 0.6f);
		}
		cState.recoilingDrill = true;
	}

	public void DrillPull(bool isRight)
	{
		CancelRecoilHorizontal();
		Recoil(isRight, RECOIL_HOR_STEPS, RECOIL_HOR_VELOCITY);
	}

	public void Recoil(int steps, float velocity)
	{
		Recoil(!cState.facingRight, steps, velocity);
	}

	public void Recoil(bool isRight, int steps, float velocity)
	{
		if (cState.dashing || dashingDown)
		{
			CancelDash();
		}
		recoilStepsLeft = steps;
		cState.recoilingRight = isRight;
		cState.recoilingLeft = !isRight;
		cState.recoilingDrill = false;
		cState.wallJumping = false;
		wallJumpedL = false;
		wallJumpedR = false;
		recoilVelocity = velocity;
		rb2d.linearVelocity = new Vector2(isRight ? velocity : (0f - velocity), rb2d.linearVelocity.y);
	}

	public void ChargeSlashRecoilRight()
	{
		if (Config.ChargeSlashRecoils)
		{
			SetAllowRecoilWhileRelinquished(value: true);
			RecoilRight();
		}
	}

	public void ChargeSlashRecoilLeft()
	{
		if (Config.ChargeSlashRecoils)
		{
			SetAllowRecoilWhileRelinquished(value: true);
			RecoilLeft();
		}
	}

	public void RecoilDown()
	{
		CancelJump();
		bool flag = Gameplay.WitchCrest.IsEquipped || Gameplay.CursedCrest.IsEquipped || Gameplay.WeightedAnkletTool.IsEquipped;
		if ((Gameplay.WitchCrest.IsEquipped || Gameplay.CursedCrest.IsEquipped) && Gameplay.WeightedAnkletTool.IsEquipped)
		{
			return;
		}
		if (flag)
		{
			if (rb2d.linearVelocity.y > RECOIL_DOWN_VELOCITY && !controlReqlinquished)
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, rb2d.linearVelocity.y / 2f);
			}
		}
		else if (rb2d.linearVelocity.y > RECOIL_DOWN_VELOCITY && !controlReqlinquished)
		{
			rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, RECOIL_DOWN_VELOCITY);
		}
	}

	public void ForceHardLanding()
	{
		if (!cState.onGround)
		{
			cState.willHardLand = true;
		}
	}

	public void EnterUpdraft(float speed)
	{
		updraftsEntered++;
		cState.inUpdraft = true;
		if (updraftsEntered == 1)
		{
			fsm_brollyControl.FsmVariables.FindFsmFloat("Target Updraft Speed").Value = speed;
			fsm_brollyControl.SendEventSafe("UPDRAFT ENTER");
		}
	}

	public void ExitUpdraft()
	{
		updraftsEntered--;
		if (updraftsEntered <= 0)
		{
			updraftsEntered = 0;
			cState.inUpdraft = false;
			fsm_brollyControl.SendEventSafe("UPDRAFT EXIT");
		}
	}

	public void ResetUpdraft()
	{
		updraftsEntered = 0;
		cState.inUpdraft = false;
		fsm_brollyControl.SendEventSafe("UPDRAFT EXIT");
	}

	public void AllowMantle(bool allow)
	{
		allowMantle = allow;
	}

	public void EnterSceneDreamGate()
	{
		IgnoreInputWithoutReset();
		ResetMotion();
		airDashed = false;
		doubleJumped = false;
		ResetHardLandingTimer();
		ResetAttacksDash();
		AffectedByGravity(gravityApplies: false);
		sceneEntryGate = null;
		SetState(ActorStates.no_input);
		transitionState = HeroTransitionState.WAITING_TO_ENTER_LEVEL;
		vignetteFSM.SendEvent("RESET");
		SendHeroInPosition(forceDirect: false);
		FinishedEnteringScene();
	}

	public IEnumerator EnterScene(TransitionPoint enterGate, float delayBeforeEnter, bool forceCustomFade = false, Action onEnd = null, bool enterSkip = false)
	{
		enterGate.PrepareEntry();
		animCtrl.waitingToEnter = true;
		while (GameManager.IsCollectingGarbage)
		{
			yield return null;
		}
		float num = Platform.Current.EnterSceneWait;
		if (CheatManager.SceneEntryWait >= 0f)
		{
			num = CheatManager.SceneEntryWait;
		}
		if (num > 0f)
		{
			yield return new WaitForSecondsRealtime(num);
		}
		animCtrl.waitingToEnter = false;
		cState.fakeHurt = false;
		EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
		ElevatorReset();
		ConveyorReset();
		IgnoreInputWithoutReset();
		bool num2 = dashingDown;
		ResetMotion(resetNailCharge: false);
		if (num2)
		{
			dashingDown = true;
			HeroDash(startAlreadyDashing: true);
		}
		airDashed = false;
		doubleJumped = false;
		ResetHardLandingTimer();
		ResetAttacksDash();
		AffectedByGravity(gravityApplies: false);
		sceneEntryGate = enterGate;
		SetState(ActorStates.no_input);
		transitionState = HeroTransitionState.WAITING_TO_ENTER_LEVEL;
		renderer.enabled = true;
		enterGate.BeforeEntry();
		if (!cState.transitioning)
		{
			cState.transitioning = true;
		}
		gatePosition = enterGate.GetGatePosition();
		proxyFSM.SendEvent("HeroCtrl-EnteringScene");
		if (exitedSuperDashing)
		{
			superJumpFSM.SendEventSafe("PRE ENTER SUPERJUMPING");
		}
		SilkSpool.ResumeSilkAudio();
		if (gatePosition == GatePosition.top)
		{
			cState.onGround = false;
			enteringVertically = true;
			exitedSuperDashing = false;
			exitedSprinting = false;
			float x = enterGate.transform.position.x + enterGate.entryOffset.x;
			float y = enterGate.transform.position.y + enterGate.entryOffset.y;
			transform.SetPosition2D(x, y);
			SendHeroInPosition(forceDirect: false);
			yield return StartCoroutine(EnterHeroSubFadeUp(enterGate, forceCustomFade, delayBeforeEnter, enterSkip));
			if (exitedQuake)
			{
				IgnoreInput();
				proxyFSM.SendEvent("HeroCtrl-EnterQuake");
				yield return new WaitForSeconds(0.25f);
				FinishedEnteringScene();
			}
			else
			{
				rb2d.linearVelocity = new Vector2(0f, SPEED_TO_ENTER_SCENE_DOWN);
				transitionState = HeroTransitionState.ENTERING_SCENE;
				transitionState = HeroTransitionState.DROPPING_DOWN;
				AffectedByGravity(gravityApplies: true);
				if (enterGate.hardLandOnExit)
				{
					cState.willHardLand = true;
				}
				yield return new WaitForSeconds(0.33f);
				transitionState = HeroTransitionState.ENTERING_SCENE;
				if (transitionState != HeroTransitionState.WAITING_TO_TRANSITION)
				{
					FinishedEnteringScene();
				}
			}
		}
		else if (gatePosition == GatePosition.bottom)
		{
			cState.onGround = false;
			enteringVertically = true;
			exitedSprinting = false;
			if (enterGate.alwaysEnterRight)
			{
				FaceRight();
			}
			if (enterGate.alwaysEnterLeft)
			{
				FaceLeft();
			}
			float x2 = enterGate.transform.position.x + enterGate.entryOffset.x;
			float y2 = enterGate.transform.position.y + enterGate.entryOffset.y + 3f;
			transform.SetPosition2D(x2, y2);
			SendHeroInPosition(forceDirect: false);
			yield return StartCoroutine(EnterHeroSubFadeUp(enterGate, forceCustomFade, delayBeforeEnter, enterSkip));
			if (exitedSuperDashing)
			{
				IgnoreInput();
				superJumpFSM.SendEventSafe("ENTER SUPERJUMPING");
				Debug.Log("Entering Super Jumping");
				yield return null;
				FinishedEnteringScene();
			}
			else
			{
				transition_vel = (cState.facingRight ? new Vector2(SPEED_TO_ENTER_SCENE_HOR, SPEED_TO_ENTER_SCENE_UP) : new Vector2(0f - SPEED_TO_ENTER_SCENE_HOR, SPEED_TO_ENTER_SCENE_UP));
				transitionState = HeroTransitionState.ENTERING_SCENE;
				transform.SetPosition2D(x2, y2);
				yield return new WaitForSeconds(TIME_TO_ENTER_SCENE_BOT);
				transition_vel = new Vector2(rb2d.linearVelocity.x, 0f);
				AffectedByGravity(gravityApplies: true);
				transitionState = HeroTransitionState.DROPPING_DOWN;
			}
		}
		else if (gatePosition == GatePosition.left)
		{
			yield return StartCoroutine(EnterHeroSubHorizontal(enterGate, forceCustomFade, delayBeforeEnter, enterSkip, 1f));
		}
		else if (gatePosition == GatePosition.right)
		{
			yield return StartCoroutine(EnterHeroSubHorizontal(enterGate, forceCustomFade, delayBeforeEnter, enterSkip, -1f));
		}
		else if (gatePosition == GatePosition.door)
		{
			if (enterGate.alwaysEnterRight)
			{
				FaceRight();
			}
			if (enterGate.alwaysEnterLeft)
			{
				FaceLeft();
			}
			cState.falling = false;
			cState.onGround = true;
			if (enterGate.dontWalkOutOfDoor)
			{
				ForceWalkingSound = false;
			}
			else
			{
				ForceWalkingSound = true;
			}
			enteringVertically = false;
			SetState(ActorStates.idle);
			SetState(ActorStates.no_input);
			exitedSuperDashing = false;
			exitedSprinting = false;
			animCtrl.PlayClip("Idle");
			transform.SetPosition2D(FindGroundPoint(enterGate.transform.position));
			SendHeroInPosition(forceDirect: false);
			yield return null;
			yield return new WaitForFixedUpdate();
			yield return StartCoroutine(EnterHeroSubFadeUp(enterGate, forceCustomFade, delayBeforeEnter, enterSkip));
			if (enterGate.dontWalkOutOfDoor)
			{
				yield return new WaitForSeconds(0.33f);
			}
			else
			{
				animCtrl.PlayClip("Exit Door To Idle");
				yield return new WaitForTk2dAnimatorClipFinish(animCtrl.animator, delegate
				{
					animCtrl.SetPlayRunToIdle();
				});
			}
			FinishedEnteringScene();
		}
		onEnd?.Invoke();
	}

	private IEnumerator EnterHeroSubHorizontal(TransitionPoint enterGate, bool forceCustomFade, float delayBeforeEnter, bool enterSkip, float direction)
	{
		cState.falling = false;
		cState.onGround = true;
		canSoftLand = false;
		enteringVertically = false;
		exitedSuperDashing = false;
		SetState(ActorStates.no_input);
		if (enterWithoutInput || exitedSuperDashing || exitedQuake || exitedSprinting)
		{
			IgnoreInput();
		}
		ForceWalkingSound = true;
		Vector3 position = enterGate.transform.position;
		float x = position.x + enterGate.entryOffset.x;
		float y = FindGroundPointY(x + direction, position.y);
		ResetVelocity();
		extraAirMoveVelocities.Clear();
		PreventSoftLand(1f);
		transform.SetPosition2D(x, y);
		SendPreHeroInPosition(forceDirect: true);
		yield return null;
		transform.SetPosition2D(x, y);
		SendHeroInPosition(forceDirect: true);
		if (direction > 0f)
		{
			FaceRight();
		}
		else
		{
			FaceLeft();
		}
		animCtrl.ResetAll();
		renderer.enabled = false;
		yield return StartCoroutine(EnterHeroSubFadeUp(enterGate, forceCustomFade, delayBeforeEnter, enterSkip));
		renderer.enabled = true;
		cState.falling = false;
		cState.onGround = true;
		canSoftLand = false;
		PreventSoftLand(1f);
		SetState(ActorStates.no_input);
		if (skipNormalEntry)
		{
			transition_vel = new Vector2(GetRunSpeed() * direction, 0f);
			transitionState = HeroTransitionState.ENTERING_SCENE;
			yield return new WaitForSeconds(0.33f);
			FinishedEnteringScene(setHazardMarker: true, preventRunBob: true);
		}
		else if (enterSkip)
		{
			transitionState = HeroTransitionState.ENTERING_SCENE;
			float x2 = GetRunSpeed() * direction * 0.33f;
			rb2d.MovePosition(rb2d.position + new Vector2(x2, 0f));
			FinishedEnteringScene(setHazardMarker: true, preventRunBob: true);
			animCtrl.StartControl();
			ForceWalkingSound = false;
		}
		else if (exitedSprinting)
		{
			IgnoreInput();
			sprintFSM.SendEventSafe("ENTER SPRINTING");
			yield return null;
			FinishedEnteringScene();
		}
		else
		{
			transition_vel = new Vector2(GetRunSpeed() * direction, 0f);
			transitionState = HeroTransitionState.ENTERING_SCENE;
			ForceRunningSound = true;
			ForceWalkingSound = false;
			yield return new WaitForSeconds(0.33f);
			SetState(ActorStates.running);
			FinishedEnteringScene(setHazardMarker: true, preventRunBob: true);
		}
	}

	private IEnumerator EnterHeroSubFadeUp(TransitionPoint enterGate, bool forceCustomFade, float delayBeforeEnter, bool enterSkip)
	{
		yield return new WaitForSeconds(0.165f);
		float sceneFadeUpPadding = GetSceneFadeUpPadding();
		if (sceneFadeUpPadding > 0f)
		{
			yield return new WaitForSeconds(sceneFadeUpPadding);
		}
		if (!(enterGate.customFade || forceCustomFade))
		{
			while (CameraController.IsPositioningCamera && gm.cameraCtrl.StartLockedTimer > 0f)
			{
				yield return null;
			}
			gm.FadeSceneIn();
		}
		if (!enterSkip)
		{
			if (delayBeforeEnter > 0f)
			{
				yield return new WaitForSeconds(delayBeforeEnter);
			}
			if (enterGate.entryDelay > 0f)
			{
				yield return new WaitForSeconds(enterGate.entryDelay);
			}
		}
	}

	private float GetSceneFadeUpPadding()
	{
		return 0f;
	}

	public static void MoveIfNotInDontDestroyOnLoad(GameObject obj)
	{
		if (!(obj.scene.name == "DontDestroyOnLoad"))
		{
			if (obj.transform.parent != null)
			{
				obj.transform.SetParent(null);
			}
			UnityEngine.Object.DontDestroyOnLoad(obj);
		}
	}

	private void ResetHeroInPositionState()
	{
		isHeroInPosition = false;
		isHeroInPrePosition = false;
	}

	public void LeaveScene(GatePosition? gate = null)
	{
		ResetHeroInPositionState();
		SetBlockSteepSlopes(blocked: false);
		MoveIfNotInDontDestroyOnLoad(base.gameObject);
		updraftsEntered = 0;
		IgnoreInputWithoutReset();
		ResetHardLandingTimer();
		SetState(ActorStates.no_input);
		SetDamageMode(DamageMode.NO_DAMAGE);
		transitionState = HeroTransitionState.EXITING_SCENE;
		CancelFallEffects();
		tilemapTestActive = false;
		SetHeroParent(null);
		StopTilemapTest();
		if (gate.HasValue)
		{
			float num = 0f;
			switch (gate.Value)
			{
			case GatePosition.top:
				transition_vel = new Vector2(0f, MIN_JUMP_SPEED);
				cState.onGround = false;
				break;
			case GatePosition.bottom:
				transition_vel = Vector2.zero;
				cState.onGround = false;
				break;
			case GatePosition.right:
				num = rb2d.linearVelocity.x;
				transition_vel = new Vector2(GetRunSpeed(), 0f);
				if (num > transition_vel.x)
				{
					transition_vel = new Vector2(num, 0f);
				}
				if (cState.onGround)
				{
					ForceRunningSound = true;
				}
				break;
			case GatePosition.left:
				num = rb2d.linearVelocity.x;
				transition_vel = new Vector2(0f - GetRunSpeed(), 0f);
				if (num < transition_vel.x)
				{
					transition_vel = new Vector2(num, 0f);
				}
				if (cState.onGround)
				{
					ForceRunningSound = true;
				}
				break;
			}
		}
		cState.transitioning = true;
	}

	public IEnumerator BetaLeave(EndBeta betaEndTrigger)
	{
		if (!playerData.betaEnd)
		{
			endBeta = betaEndTrigger;
			IgnoreInput();
			playerData.disablePause = true;
			SetState(ActorStates.no_input);
			ResetInput();
			tilemapTestActive = false;
			yield return new WaitForSeconds(0.66f);
			GameObject.Find("Beta Ender").GetComponent<SimpleSpriteFade>().FadeIn();
			ResetMotion();
			yield return new WaitForSeconds(1.25f);
			playerData.betaEnd = true;
		}
	}

	public IEnumerator BetaReturn()
	{
		rb2d.linearVelocity = new Vector2(GetRunSpeed(), 0f);
		if (!cState.facingRight)
		{
			FlipSprite();
		}
		GameObject.Find("Beta Ender").GetComponent<SimpleSpriteFade>().FadeOut();
		animCtrl.PlayClip("Run");
		yield return new WaitForSeconds(1.4f);
		SetState(ActorStates.grounded);
		SetStartingMotionState();
		AcceptInput();
		playerData.betaEnd = false;
		playerData.disablePause = false;
		tilemapTestActive = true;
		if (endBeta != null)
		{
			endBeta.Reactivate();
		}
	}

	public IEnumerator Respawn(Transform spawnPoint)
	{
		bool wasDead = cState.dead;
		playerData = PlayerData.instance;
		playerData.disablePause = true;
		base.gameObject.layer = 9;
		renderer.enabled = true;
		heroBox.HeroBoxNormal();
		rb2d.isKinematic = false;
		ResetLook();
		cState.dead = false;
		cState.isFrostDeath = false;
		cState.onGround = true;
		cState.falling = false;
		cState.hazardDeath = false;
		cState.recoiling = false;
		enteringVertically = false;
		airDashed = false;
		doubleJumped = false;
		startFromMantle = false;
		if (wasDead)
		{
			CharmUpdate();
			MaxHealth();
		}
		ResetMotion();
		ResetHardLandingTimer();
		ResetAttacks();
		ResetInput();
		if (wasDead)
		{
			CharmUpdate();
		}
		ResetLavaBell();
		if (wasDead)
		{
			EventRegister.SendEvent("MAGGOT RESET");
			ClearEffects();
			if (frostedEffect.IsAlive())
			{
				frostedEffect.StopParticleSystems();
				frostedEffect.ClearParticleSystems();
			}
		}
		SilkSpool.ResumeSilkAudio();
		ResetSilkRegen();
		if (wasDead)
		{
			SilkSpool silkSpool = GameCameras.instance.silkSpool;
			silkSpool.RemoveUsing(SilkSpool.SilkUsingFlags.Maggot);
			silkSpool.RefreshSilk();
		}
		bool num = playerData.tempRespawnType == 0;
		playerData.ResetTempRespawn();
		PlayMakerFSM benchFSM = (((bool)spawnPoint && spawnPoint.gameObject.activeInHierarchy) ? FSMUtility.LocateFSM(spawnPoint.gameObject, "Bench Control") : null);
		if (spawnPoint != null)
		{
			if (!spawnPoint.gameObject.activeInHierarchy)
			{
				Transform transform = spawnPoint.Find("Alt Respawn Point");
				if ((bool)transform)
				{
					spawnPoint = transform;
				}
			}
			this.transform.SetPosition2D(FindGroundPoint(spawnPoint.transform.position));
			if (benchFSM != null)
			{
				FSMUtility.GetVector3(benchFSM, "Adjust Vector");
			}
		}
		RespawnMarker respawnMarker = spawnPoint.GetComponent<RespawnMarker>();
		TickFrostEffect(shouldTickInto: false);
		playerData.ResetCutsceneBools();
		playerData.isInvincible = false;
		if (!num && playerData.respawnType == 1 && benchFSM != null)
		{
			AffectedByGravity(gravityApplies: false);
			benchFSM.FsmVariables.GetFsmBool("RespawnResting").Value = true;
			yield return new WaitForEndOfFrame();
			SetFacingForSpawnPoint();
			SendHeroInPosition(forceDirect: false);
			HeroRespawned();
			FinishedEnteringScene();
			benchFSM.SendEvent("RESPAWN");
		}
		else
		{
			yield return new WaitForEndOfFrame();
			yield return null;
			IgnoreInput();
			SetFacingForSpawnPoint();
			SendHeroInPosition(forceDirect: false);
			if (!respawnMarker || !respawnMarker.customWakeUp)
			{
				StopAnimationControl();
				controlReqlinquished = true;
				if ((bool)respawnMarker && respawnMarker.customFadeDuration.IsEnabled)
				{
					animCtrl.PlayClipForced("Prostrate");
					yield return new WaitForSeconds(respawnMarker.customFadeDuration.Value);
				}
				float clipLength = animCtrl.GetClipDuration("Wake Up Ground");
				animCtrl.PlayClipForced("Wake Up Ground");
				tk2dSpriteAnimationClip clip = animCtrl.animator.CurrentClip;
				if (clip != null)
				{
					while (animCtrl.animator.IsPlaying(clip) && clipLength > 0f)
					{
						yield return null;
						clipLength -= Time.deltaTime;
					}
				}
				else
				{
					yield return new WaitForSeconds(clipLength);
				}
				SetState(ActorStates.grounded);
				StartAnimationControl();
				controlReqlinquished = false;
				GameCameras.instance.HUDIn();
			}
			if ((bool)respawnMarker)
			{
				respawnMarker.RespawnedHere();
			}
			HeroRespawned();
			FinishedEnteringScene();
		}
		if (wasDead && playerData.HasSeenGeo)
		{
			if (playerData.geo <= 0)
			{
				CurrencyCounter.ToZero(CurrencyType.Money);
			}
			else
			{
				CurrencyCounter.ToValue(playerData.geo, CurrencyType.Money);
			}
		}
		void SetFacingForSpawnPoint()
		{
			if ((bool)respawnMarker)
			{
				if (respawnMarker.respawnFacingRight)
				{
					FaceRight();
				}
				else
				{
					FaceLeft();
				}
			}
		}
	}

	public void HazardRespawnReset()
	{
		cState.hazardDeath = false;
		cState.onGround = true;
		cState.hazardRespawning = true;
		ResetMotion();
		ResetHardLandingTimer();
		ResetAttacks();
		ResetInput();
		cState.recoiling = false;
		enteringVertically = false;
		airDashed = false;
		doubleJumped = false;
		cState.lookingUp = false;
		cState.lookingDown = false;
		base.gameObject.layer = 9;
		renderer.enabled = true;
		heroBox.HeroBoxNormal();
		IsStunned = false;
		EventRegister.SendEvent(EventRegisterEvents.HazardRespawnReset);
	}

	public void ResetShuttlecock()
	{
		startWithShuttlecock = false;
		sprintBufferSteps = 0;
		syncBufferSteps = false;
	}

	public IEnumerator HazardRespawn()
	{
		doingHazardRespawn = true;
		SetState(ActorStates.no_input);
		lastHazardRespawnTime = Time.timeAsDouble;
		ResetLook();
		if (!TryFindGroundPoint(out var respawnPos, playerData.hazardRespawnLocation, useExtended: true))
		{
			string entryGateName = gm.entryGateName;
			if (!string.IsNullOrEmpty(entryGateName))
			{
				TransitionPoint transitionPoint = TransitionPoint.TransitionPoints.FirstOrDefault((TransitionPoint p) => p.gameObject.name == entryGateName);
				if (transitionPoint != null)
				{
					transform.SetPosition2D(new Vector2(-500f, -500f));
					HazardRespawnReset();
					gm.BeginSceneTransition(new GameManager.SceneLoadInfo
					{
						SceneName = transitionPoint.targetScene,
						EntryGateName = transitionPoint.entryPoint
					});
					yield break;
				}
			}
		}
		StartInvulnerable(INVUL_TIME * 2f + 0.3f);
		Vector2 posBeforeRespawn = transform.position;
		transform.SetPosition2D(respawnPos);
		rb2d.isKinematic = false;
		HazardRespawnReset();
		yield return null;
		transform.SetPosition2D(respawnPos);
		rb2d.linearVelocity = Vector2.zero;
		yield return new WaitForEndOfFrame();
		switch (playerData.hazardRespawnFacing)
		{
		case HazardRespawnMarker.FacingDirection.None:
			if (posBeforeRespawn.x < respawnPos.x)
			{
				FaceLeft();
			}
			else
			{
				FaceRight();
			}
			break;
		case HazardRespawnMarker.FacingDirection.Left:
			FaceLeft();
			break;
		case HazardRespawnMarker.FacingDirection.Right:
			FaceRight();
			break;
		default:
			throw new ArgumentOutOfRangeException();
		}
		SendHeroInPosition(forceDirect: false);
		if (this.OnHazardRespawn != null)
		{
			this.OnHazardRespawn();
		}
		yield return new WaitForSeconds(0.3f);
		GCManager.Collect();
		yield return null;
		gm.screenFader_fsm.SendEventSafe("HAZARD RESPAWN");
		proxyFSM.SendEvent("HeroCtrl-HazardRespawned");
		tk2dSpriteAnimationClip clip = animCtrl.GetClip("Hazard Respawn");
		yield return StartCoroutine(animCtrl.animator.PlayAnimWait(clip));
		cState.hazardRespawning = false;
		rb2d.interpolation = RigidbodyInterpolation2D.Interpolate;
		FinishedEnteringScene(setHazardMarker: false);
	}

	public bool GetState(string stateName)
	{
		return cState.GetState(stateName);
	}

	public bool GetCState(string stateName)
	{
		return cState.GetState(stateName);
	}

	public void SetCState(string stateName, bool value)
	{
		cState.SetState(stateName, value);
	}

	public static bool CStateExists(string stateName)
	{
		return HeroControllerStates.CStateExists(stateName);
	}

	public void ResetHardLandingTimer()
	{
		cState.willHardLand = false;
		hardLandingTimer = 0f;
		fallTimer = 0f;
		hardLanded = false;
	}

	public void CancelSuperDash()
	{
	}

	private void CancelDownSpike()
	{
		cState.downSpikeAntic = false;
		startWithDownSpikeBounce = false;
		startWithDownSpikeBounceShort = false;
		startWithDownSpikeBounceSlightlyShort = false;
		if (cState.downSpiking)
		{
			FinishDownspike();
		}
	}

	public void CancelDownSpikeBounces()
	{
		startWithDownSpikeBounce = false;
		startWithDownSpikeBounceShort = false;
		startWithDownSpikeBounceSlightlyShort = false;
		cState.downSpikeBouncing = false;
		cState.downSpikeBouncingShort = false;
	}

	public void RelinquishControlNotVelocity()
	{
		ControlVersion++;
		CancelDownSpike();
		if (!controlReqlinquished)
		{
			prev_hero_state = ActorStates.idle;
			ResetInput();
			ResetMotionNotVelocity();
			SetState(ActorStates.no_input);
			IgnoreInput();
			controlReqlinquished = true;
			controlRelinquishedFrame = Time.frameCount;
			ResetLook();
			ResetAttacks();
			CancelJump();
			touchingWallL = false;
			touchingWallR = false;
			touchingWallObj = null;
		}
	}

	public void RelinquishControl()
	{
		ControlVersion++;
		RemoveUnlockRequest(HeroLockStates.ControlLocked);
		CancelDownSpike();
		if ((!controlReqlinquished || acceptingInput) && !cState.dead)
		{
			ResetInput();
			ResetMotion();
			IgnoreInput();
			controlReqlinquished = true;
			controlRelinquishedFrame = Time.frameCount;
			ResetLook();
			CancelAttack();
			CancelJump();
			touchingWallL = false;
			touchingWallR = false;
			touchingWallObj = null;
		}
	}

	public void RegainControl()
	{
		RegainControl(allowInput: true);
	}

	public void RegainControl(bool allowInput)
	{
		if (CheckAndRequestUnlock(HeroLockStates.ControlLocked))
		{
			return;
		}
		RemoveUnlockRequest(HeroLockStates.ControlLocked);
		InteractManager.BlockingInteractable = null;
		enteringVertically = false;
		regainControlJumpQueued = false;
		cState.willHardLand = false;
		hardLandingTimer = 0f;
		if (cState.onGround)
		{
			fallTimer = 0f;
		}
		hardLanded = false;
		SetBlockFsmMove(blocked: false);
		audioCtrl.BlockFootstepAudio = false;
		heroBox.HeroBoxNormal();
		NoClamberRegion.RefreshInside();
		AcceptInput();
		inputHandler.ForceDreamNailRePress = true;
		if (!cState.hazardDeath && !cState.hazardRespawning)
		{
			hero_state = ActorStates.idle;
		}
		allowRecoilWhileRelinquished = false;
		recoilZeroVelocity = false;
		if (controlReqlinquished && !cState.dead)
		{
			AffectedByGravity(gravityApplies: true);
			controlReqlinquished = false;
			SetStartingMotionState();
			if (startWithWallslide)
			{
				startWithWallslide = false;
				ForceTouchingWall();
				BeginWallSlide(requireInput: false);
			}
			else if (startWithShuttlecock)
			{
				startWithShuttlecock = false;
				HeroJump(checkSprint: false);
				OnShuttleCockJump();
			}
			else if (startWithTinyJump)
			{
				HeroJump();
				cState.onGround = false;
				doubleJumpQueuing = false;
				startWithTinyJump = false;
				jump_steps = JUMP_STEPS;
				jumped_steps = JUMP_STEPS_MIN;
			}
			else if (startWithBrolly)
			{
				if (playerData.hasBrolly)
				{
					StartFloat();
				}
				startWithBrolly = false;
			}
			else if (startWithDoubleJump)
			{
				if (playerData.hasDoubleJump && !doubleJumped)
				{
					DoDoubleJump();
					doubleJumpQueuing = false;
				}
				else if (playerData.hasBrolly)
				{
					StartFloat();
				}
				startWithDoubleJump = false;
			}
			else if (startWithJump)
			{
				HeroJumpNoEffect();
				doubleJumpQueuing = false;
				startWithJump = false;
				startWithTinyJump = false;
			}
			else if (startWithAnyJump)
			{
				if (CanJump())
				{
					HeroJump();
				}
				else if (CanDoubleJump())
				{
					DoubleJump();
				}
				else if (CanFloat())
				{
					StartFloat();
				}
				startWithAnyJump = false;
			}
			else if (startWithWallsprintLaunch)
			{
				HeroJump();
				cState.onGround = false;
				doubleJumpQueuing = false;
				startWithWallsprintLaunch = false;
				jumped_steps = -7;
			}
			else if (startWithFullJump || startWithFlipJump || startWithBackflipJump)
			{
				if (startFromMantle)
				{
					startFromMantle = false;
					cState.mantleRecovery = true;
					TrySetCorrectFacing(force: true);
				}
				if (startWithBackflipJump)
				{
					HeroJumpNoEffect();
					jump_steps = 0;
					FlipSprite();
					animCtrl.SetPlayBackflip();
					if ((bool)backflipPuffPrefab)
					{
						backflipPuffPrefab.SetActive(value: false);
						backflipPuffPrefab.SetActive(value: true);
					}
					audioCtrl.PlaySound(HeroSounds.WALLJUMP, playVibration: true);
					gruntAudioTable.SpawnAndPlayOneShot(transform.position);
				}
				else if (startWithFlipJump)
				{
					HeroJumpNoEffect();
					animCtrl.SetPlayMantleCancel();
				}
				else
				{
					HeroJump(checkSprint: false);
				}
				cState.onGround = false;
				doubleJumpQueuing = false;
				startWithFullJump = false;
				startWithFlipJump = false;
				startWithBackflipJump = false;
			}
			else if (startWithWallJump)
			{
				startWithWallJump = false;
				TryDoWallJumpFromMove();
			}
			else if (startWithDash)
			{
				HeroDash(startAlreadyDashing: false);
				doubleJumpQueuing = false;
				startWithDash = false;
				dashCurrentFacing = false;
			}
			else if (startWithAttack)
			{
				if (cState.wallScrambling)
				{
					ForceTouchingWall();
					BeginWallSlide(requireInput: false);
				}
				DoAttack();
				doubleJumpQueuing = false;
				startWithAttack = false;
			}
			else if (CanStartWithThrowTool())
			{
				if (cState.wallScrambling)
				{
					ForceTouchingWall();
					BeginWallSlide(requireInput: false);
				}
				doubleJumpQueuing = false;
				startWithToolThrow = false;
				ThrowTool(isAutoThrow: false);
			}
			else if (startWithDownSpikeBounce)
			{
				startWithDownSpikeBounce = false;
				DownspikeBounce(harpoonRecoil: false);
			}
			else if (startWithDownSpikeBounceSlightlyShort)
			{
				startWithDownSpikeBounceSlightlyShort = false;
				DownspikeBounceSlightlyShort(harpoonRecoil: false);
			}
			else if (startWithDownSpikeBounceShort)
			{
				startWithDownSpikeBounceShort = false;
				DownspikeBounceShort(harpoonRecoil: false);
			}
			else if (startWithDownSpikeEnd)
			{
				startWithDownSpikeEnd = false;
				FinishDownspike();
				CancelJump();
				rb2d.linearVelocity = Vector2.zero;
			}
			else if (startWithHarpoonBounce)
			{
				startWithHarpoonBounce = false;
				DownspikeBounce(harpoonRecoil: true);
			}
			else if (startWithBalloonBounce)
			{
				useUpdraftExitJumpSpeed = true;
				TrySetCorrectFacing(force: true);
				DownspikeBounce(harpoonRecoil: false);
				allowAttackCancellingDownspikeRecovery = true;
				startWithBalloonBounce = false;
			}
			else if (startWithUpdraftExit)
			{
				startWithUpdraftExit = false;
				useUpdraftExitJumpSpeed = true;
				TrySetCorrectFacing(force: true);
				DownspikeBounce(harpoonRecoil: false);
			}
			else if (startWithScrambleLeap)
			{
				startWithScrambleLeap = false;
				cState.mantleRecovery = true;
			}
			else if (startFromMantle)
			{
				startFromMantle = false;
				cState.mantleRecovery = true;
			}
			else
			{
				cState.touchingWall = false;
				touchingWallL = false;
				touchingWallR = false;
				touchingWallObj = null;
				if (allowInput)
				{
					if (CanJump() && inputHandler.GetWasButtonPressedQueued(HeroActionButton.JUMP, consume: true))
					{
						HeroJump();
					}
					else if (jumpPressedWhileRelinquished)
					{
						regainControlJumpQueued = true;
					}
				}
			}
			if (startWithRecoilBack)
			{
				if (transform.localScale.x < 0f)
				{
					RecoilLeft();
				}
				else
				{
					RecoilRight();
				}
				startWithRecoilBack = false;
			}
			if (startWithRecoilBackLong)
			{
				if (transform.localScale.x < 0f)
				{
					RecoilLeftLong();
				}
				else
				{
					RecoilRightLong();
				}
				startWithRecoilBackLong = false;
			}
			if (startWithWhipPullRecoil)
			{
				if (transform.localScale.x < 0f)
				{
					RecoilRight();
				}
				else
				{
					RecoilLeft();
				}
				startWithWhipPullRecoil = false;
			}
		}
		jumpPressedWhileRelinquished = false;
		IsStunned = false;
	}

	private void OnShuttleCockJump()
	{
		shuttleCockJumpSteps = 2;
		jumped_steps = -5;
		cState.shuttleCock = true;
		dashCooldownTimer = 0.1f;
		if (transform.localScale.x < 0f)
		{
			shuttlecockSpeed = SHUTTLECOCK_SPEED;
		}
		else
		{
			shuttlecockSpeed = 0f - SHUTTLECOCK_SPEED;
		}
		doubleJumpQueuing = false;
		startWithJump = false;
		if ((bool)shuttleCockJumpAudio)
		{
			shuttleCockJumpAudio.Play();
		}
		vibrationCtrl.StartShuttlecock();
		if ((bool)shuttleCockJumpEffectPrefab)
		{
			GameObject obj = shuttleCockJumpEffectPrefab.Spawn();
			Vector3 localScale = shuttleCockJumpEffectPrefab.transform.localScale;
			Vector3 localScale2 = transform.localScale;
			obj.transform.localScale = new Vector3(localScale.x * (0f - localScale2.x), localScale.y * localScale2.y, localScale.z);
			obj.transform.position = transform.position;
		}
	}

	public void PreventCastByDialogueEnd()
	{
		preventCastByDialogueEndTimer = 0.3f;
	}

	public bool CanDoFsmMove()
	{
		return !blockFsmMove;
	}

	public bool IsHardLanding()
	{
		if (!hardLanded)
		{
			return hero_state == ActorStates.hard_landing;
		}
		return true;
	}

	public bool CanCast()
	{
		if (!CanDoFsmMove())
		{
			return false;
		}
		if (!gm.isPaused && !cState.dashing && hero_state != ActorStates.no_input && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.recoiling && !cState.recoilFrozen && !cState.transitioning && !cState.hazardDeath && !cState.hazardRespawning && CanInput() && preventCastByDialogueEndTimer <= 0f)
		{
			return true;
		}
		return false;
	}

	public bool CanBind()
	{
		if (!CanDoFsmMove())
		{
			return false;
		}
		if (IsHardLanding())
		{
			return false;
		}
		if (IsInputBlocked())
		{
			return false;
		}
		if (ToolItemManager.ActiveState == ToolsActiveStates.Cutscene)
		{
			return false;
		}
		if (!gm.isPaused && !cState.dashing && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.recoiling && !cState.transitioning && !cState.recoilFrozen && !cState.hazardDeath && !cState.hazardRespawning && !cState.dead && Config.CanBind && CanDoFSMCancelMove() && !ManagerSingleton<HeroChargeEffects>.Instance.IsCharging)
		{
			return true;
		}
		return false;
	}

	public bool CanDoFSMCancelMove()
	{
		if (!CanInput())
		{
			return cState.isInCancelableFSMMove;
		}
		return true;
	}

	public bool CanDoSpecial()
	{
		if (!CanDoFsmMove())
		{
			return false;
		}
		if (!gm.isPaused && !cState.dashing && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.recoiling && !cState.recoilFrozen && !cState.transitioning && !cState.hazardDeath && !cState.hazardRespawning && CanDoFSMCancelMove() && preventCastByDialogueEndTimer <= 0f)
		{
			return true;
		}
		return false;
	}

	public bool CanNailArt()
	{
		if (!CanDoFsmMove())
		{
			return false;
		}
		if (!cState.transitioning && (hero_state != ActorStates.no_input || allowNailChargingWhileRelinquished) && !cState.attacking && !cState.hazardDeath && !cState.hazardRespawning && nailChargeTimer >= CurrentNailChargeTime)
		{
			nailChargeTimer = 0f;
			return true;
		}
		nailChargeTimer = 0f;
		return false;
	}

	public bool CanQuickMap()
	{
		return CanQuickMap(onBench: false);
	}

	public bool CanQuickMapBench()
	{
		return CanQuickMap(onBench: true);
	}

	private bool CanQuickMap(bool onBench)
	{
		if (!onBench && (controlReqlinquished || hero_state == ActorStates.no_input))
		{
			return false;
		}
		if (IsInputBlocked())
		{
			return false;
		}
		if (!gm.isPaused && !playerData.disablePause && !playerData.disableInventory && hero_state != ActorStates.hard_landing && playerData.HasAnyMap && !IsLostInSlab(gm.gameMap) && !cState.onConveyor && !cState.dashing && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.recoiling && !cState.transitioning && !cState.hazardDeath && !cState.hazardRespawning && !cState.recoilFrozen && (cState.onGround || onBench))
		{
			if (!onBench)
			{
				return CanInput();
			}
			return true;
		}
		return false;
	}

	private static bool IsLostInSlab(GameMap gameMap)
	{
		if (CollectableItemManager.IsInHiddenMode())
		{
			return !gameMap.HasAnyMapForZone(MapZone.THE_SLAB);
		}
		return false;
	}

	public static bool HasNoMap(GameMap gameMap)
	{
		if (!IsLostInSlab(gameMap))
		{
			return gameMap.IsLostInAbyssPreMap();
		}
		return true;
	}

	public bool CanInspect()
	{
		if (!gm.isPaused && !cState.dashing && hero_state != ActorStates.no_input && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.recoiling && !cState.transitioning && !cState.hazardDeath && !cState.hazardRespawning && !cState.recoilFrozen && cState.onGround && CanInput())
		{
			return true;
		}
		return false;
	}

	public bool CanBackDash()
	{
		if (!gm.isPaused && !cState.dashing && hero_state != ActorStates.no_input && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.preventBackDash && !cState.backDashCooldown && !controlReqlinquished && !cState.recoilFrozen && !cState.recoiling && !cState.transitioning && cState.onGround)
		{
			return true;
		}
		return false;
	}

	public bool CanPlayNeedolin()
	{
		if (playerData.isInventoryOpen)
		{
			return false;
		}
		if (!hardLanded && !gm.isPaused && hero_state != ActorStates.no_input && !cState.dashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && ((!controlReqlinquished && cState.onGround) || playerData.atBench) && !cState.hazardDeath && rb2d.linearVelocity.y > -0.1f && !cState.hazardRespawning && !cState.recoilFrozen && !cState.recoiling && !cState.transitioning && HasNeedolin())
		{
			return true;
		}
		return false;
	}

	public bool HasNeedolin()
	{
		if (playerData.hasNeedolin)
		{
			return Config.CanPlayNeedolin;
		}
		return false;
	}

	public bool CanInteract()
	{
		ActorStates actorStates = hero_state;
		if ((actorStates == ActorStates.idle || actorStates == ActorStates.running || actorStates == ActorStates.grounded) && CanTakeControl())
		{
			return cState.onGround;
		}
		return false;
	}

	public bool CanTakeControl()
	{
		if (CanInput() && hero_state != ActorStates.no_input && !gm.isPaused && !cState.dashing && !cState.backDashing && !cState.attacking && !controlReqlinquished && !cState.hazardDeath && !cState.hazardRespawning && !cState.recoilFrozen && !cState.recoiling)
		{
			return !cState.transitioning;
		}
		return false;
	}

	public bool CanOpenInventory()
	{
		if (gm.isPaused || gm.RespawningHero)
		{
			return false;
		}
		if (IsInputBlocked())
		{
			return false;
		}
		if (!cState.recoiling && !cState.transitioning && !cState.hazardDeath && !cState.hazardRespawning && !cState.isInCutsceneMovement && !playerData.disablePause && !playerData.disableInventory && (CanInput() || controlReqlinquished) && InteractManager.BlockingInteractable == null && !GenericMessageCanvas.IsActive)
		{
			return true;
		}
		if (playerData.atBench && !playerData.disableInventory)
		{
			return true;
		}
		return false;
	}

	public void SetDamageMode(int invincibilityType)
	{
		switch (invincibilityType)
		{
		case 0:
			damageMode = DamageMode.FULL_DAMAGE;
			break;
		case 1:
			damageMode = DamageMode.HAZARD_ONLY;
			break;
		case 2:
			damageMode = DamageMode.NO_DAMAGE;
			break;
		}
	}

	public void SetDamageModeFSM(int invincibilityType)
	{
		switch (invincibilityType)
		{
		case 0:
			damageMode = DamageMode.FULL_DAMAGE;
			break;
		case 1:
			damageMode = DamageMode.HAZARD_ONLY;
			break;
		case 2:
			damageMode = DamageMode.NO_DAMAGE;
			break;
		}
	}

	public void ResetQuakeDamage()
	{
		if (damageMode == DamageMode.HAZARD_ONLY)
		{
			damageMode = DamageMode.FULL_DAMAGE;
		}
	}

	public void SetDamageMode(DamageMode newDamageMode)
	{
		damageMode = newDamageMode;
		playerData.isInvincible = newDamageMode == DamageMode.NO_DAMAGE;
	}

	public void StopAnimationControl()
	{
		RemoveUnlockRequest(HeroLockStates.AnimationLocked);
		AnimationControlVersion++;
		animCtrl.StopControl();
	}

	public int StopAnimationControlVersioned()
	{
		StopAnimationControl();
		return AnimationControlVersion;
	}

	public void StartAnimationControl()
	{
		if (!CheckAndRequestUnlock(HeroLockStates.AnimationLocked))
		{
			RemoveUnlockRequest(HeroLockStates.AnimationLocked);
			animCtrl.StartControl();
		}
	}

	public void StartAnimationControl(int version)
	{
		if (AnimationControlVersion == version)
		{
			StartAnimationControl();
		}
	}

	public void StartAnimationControlRunning()
	{
		animCtrl.StartControlRunning();
	}

	public void StartAnimationControlToIdle()
	{
		if (cState.onGround)
		{
			animCtrl.StartControlToIdle(forcePlay: false);
		}
		else
		{
			animCtrl.StartControl();
		}
	}

	public void StartAnimationControlToIdleForcePlay()
	{
		if (cState.onGround)
		{
			animCtrl.StartControlToIdle(forcePlay: true);
		}
		else
		{
			animCtrl.StartControl();
		}
	}

	public void IgnoreInput()
	{
		if (acceptingInput)
		{
			acceptingInput = false;
			ResetInput();
		}
	}

	public void IgnoreInputWithoutReset()
	{
		if (acceptingInput)
		{
			acceptingInput = false;
		}
	}

	public void AcceptInput()
	{
		if (isHeroInPosition && !cState.transitioning && !cState.hazardRespawning)
		{
			acceptingInput = true;
		}
	}

	public void Pause()
	{
		PauseInput();
		PauseAudio();
		JumpReleased();
		cState.isPaused = true;
	}

	public void UnPause()
	{
		cState.isPaused = false;
		UnPauseAudio();
		UnPauseInput();
	}

	public void NearBench(bool isNearBench)
	{
		cState.nearBench = isNearBench;
	}

	public void SetWalkZone(bool inWalkZone)
	{
		cState.inWalkZone = inWalkZone;
	}

	public void ResetState()
	{
		cState.Reset();
		heroBox.HeroBoxNormal();
	}

	public void StopPlayingAudio()
	{
		audioCtrl.StopAllSounds();
	}

	public void PauseAudio()
	{
		audioCtrl.PauseAllSounds();
	}

	public void UnPauseAudio()
	{
		audioCtrl.UnPauseAllSounds();
	}

	private void PauseInput()
	{
		if (acceptingInput)
		{
			acceptingInput = false;
		}
		lastInputState = new Vector2(move_input, vertical_input);
	}

	private void UnPauseInput()
	{
		if (!controlReqlinquished)
		{
			_ = lastInputState;
			if (inputHandler.inputActions.Right.IsPressed)
			{
				move_input = lastInputState.x;
			}
			else if (inputHandler.inputActions.Left.IsPressed)
			{
				move_input = lastInputState.x;
			}
			else
			{
				rb2d.linearVelocity = new Vector2(0f, rb2d.linearVelocity.y);
				move_input = 0f;
			}
			vertical_input = lastInputState.y;
			acceptingInput = true;
		}
	}

	public void SetCanSoftLand()
	{
		if (!canSoftLand)
		{
			canSoftLand = true;
			softLandTime = Time.frameCount + 1;
		}
	}

	public bool TrySpawnSoftLandingPrefab()
	{
		if (CanSoftLand())
		{
			SpawnSoftLandingPrefab();
			return true;
		}
		return false;
	}

	public bool CanSoftLand()
	{
		if (canSoftLand && Time.frameCount > softLandTime && cState.onGround)
		{
			return Time.time > preventSoftLandTimer;
		}
		return false;
	}

	public void PreventSoftLand(float duration)
	{
		canSoftLand = false;
		preventSoftLandTimer = Time.time + duration;
	}

	public void SpawnSoftLandingPrefab()
	{
		canSoftLand = false;
		softLandingEffectPrefab.Spawn(transform.position);
		vibrationCtrl.PlaySoftLand();
	}

	public void AffectedByGravity(bool gravityApplies)
	{
		if (!gravityApplies || !CheckAndRequestUnlock(HeroLockStates.GravityLocked))
		{
			RemoveUnlockRequest(HeroLockStates.GravityLocked);
			IsGravityApplied = gravityApplies;
			if (rb2d.gravityScale > Mathf.Epsilon && !gravityApplies)
			{
				prevGravityScale = rb2d.gravityScale;
				rb2d.gravityScale = 0f;
			}
			else if (rb2d.gravityScale <= Mathf.Epsilon && gravityApplies)
			{
				rb2d.gravityScale = prevGravityScale;
				prevGravityScale = 0f;
			}
		}
	}

	public void TryRestoreGravity()
	{
		AffectedByGravity(gravityApplies: true);
	}

	public void ResetGravity()
	{
		rb2d.gravityScale = DEFAULT_GRAVITY;
	}

	public void ResetVelocity()
	{
		rb2d.linearVelocity = Vector2.zero;
	}

	public void AddInputBlocker(object blocker)
	{
		inputBlockers.Add(blocker);
	}

	public void RemoveInputBlocker(object blocker)
	{
		inputBlockers.Remove(blocker);
	}

	public bool IsInputBlocked()
	{
		if (CheatManager.IsOpen)
		{
			return true;
		}
		foreach (object inputBlocker in inputBlockers)
		{
			if (inputBlocker is UnityEngine.Object obj)
			{
				if (obj == null)
				{
					continue;
				}
			}
			else if (inputBlocker == null)
			{
				continue;
			}
			return true;
		}
		return false;
	}

	private bool IsPressingOnlyDown()
	{
		if (inputHandler.inputActions.Down.IsPressed && !inputHandler.inputActions.Right.IsPressed)
		{
			return !inputHandler.inputActions.Left.IsPressed;
		}
		return false;
	}

	private void LookForInput()
	{
		if (IsInputBlocked() || gm.GameState != GameState.PLAYING)
		{
			move_input = 0f;
			vertical_input = 0f;
		}
		else
		{
			if (gm.isPaused || !isGameplayScene)
			{
				return;
			}
			if (inputHandler.inputActions.SuperDash.WasPressed && IsPressingOnlyDown() && CanSuperJump())
			{
				if (controlReqlinquished)
				{
					EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
					RegainControl();
					StartAnimationControlToIdle();
				}
				superJumpFSM.SendEventSafe("DO MOVE");
			}
			if (!acceptingInput)
			{
				return;
			}
			UpdateMoveInput();
			vertical_input = inputHandler.inputActions.MoveVector.Vector.y;
			FilterInput();
			if (CanWallSlide() && !cState.attacking)
			{
				BeginWallSlide(requireInput: true);
			}
			HeroActions inputActions = inputHandler.inputActions;
			if (cState.wallSliding && inputActions.Down.WasPressed && (!touchingWallL || !inputActions.Left.IsPressed) && (!touchingWallR || !inputActions.Right.IsPressed))
			{
				CancelWallsliding();
				FlipSprite();
			}
			if (wallLocked && wallJumpedL && inputActions.Right.IsPressed && wallLockSteps >= WJLOCK_STEPS_SHORT)
			{
				wallLocked = false;
			}
			if (wallLocked && wallJumpedR && inputActions.Left.IsPressed && wallLockSteps >= WJLOCK_STEPS_SHORT)
			{
				wallLocked = false;
			}
			if (inputHandler.inputActions.Jump.WasReleased)
			{
				if (jumpReleaseQueueingEnabled)
				{
					jumpReleaseQueueSteps = JUMP_RELEASE_QUEUE_STEPS;
					jumpReleaseQueuing = true;
				}
				if (cState.floating)
				{
					cState.floating = false;
				}
			}
			if (!inputHandler.inputActions.Jump.IsPressed)
			{
				JumpReleased();
			}
			if (!inputHandler.inputActions.Dash.IsPressed)
			{
				if (cState.preventDash && !cState.dashCooldown)
				{
					cState.preventDash = false;
				}
				dashQueuing = false;
			}
			if (!inputHandler.inputActions.Attack.IsPressed)
			{
				attackQueuing = false;
			}
			if (!inputHandler.inputActions.SuperDash.IsPressed)
			{
				harpoonQueuing = false;
			}
			if (!inputHandler.inputActions.QuickCast.IsPressed)
			{
				toolThrowQueueing = false;
			}
		}
	}

	private void ForceTouchingWall()
	{
		touchingWallL = !cState.facingRight;
		touchingWallR = cState.facingRight;
		cState.touchingWall = true;
	}

	private void BeginWallSlide(bool requireInput)
	{
		if (cState.wallSliding)
		{
			return;
		}
		bool flag = false;
		if (touchingWallL && (!requireInput || inputHandler.inputActions.Left.IsPressed))
		{
			wallSlidingL = true;
			wallSlidingR = false;
			FaceLeft();
			flag = true;
		}
		if (touchingWallR && (!requireInput || inputHandler.inputActions.Right.IsPressed))
		{
			wallSlidingL = false;
			wallSlidingR = true;
			FaceRight();
			flag = true;
		}
		if (!flag)
		{
			return;
		}
		if ((bool)touchingWallObj)
		{
			FSMUtility.SendEventUpwards(touchingWallObj, "HERO WALLSLIDE");
		}
		if (cState.shuttleCock)
		{
			ShuttleCockCancelInert();
			float num;
			if (jump_steps > 0)
			{
				num = 1f - (float)jump_steps / (float)JUMP_STEPS;
				if (num < 0.55f)
				{
					num = 0.55f;
				}
			}
			else
			{
				num = 0.55f;
			}
			wallStickStartVelocity = WALLSLIDE_SHUTTLECOCK_VEL * num;
		}
		else
		{
			wallStickStartVelocity = 0f;
		}
		CancelJump();
		if (cState.dashing)
		{
			CancelDash();
		}
		airDashed = false;
		doubleJumped = false;
		cState.wallSliding = true;
		AffectedByGravity(gravityApplies: false);
		cState.willHardLand = false;
		extraAirMoveVelocities.Clear();
		ParticleSystem.EmissionModule emission = wallslideDustPrefab.emission;
		emission.enabled = true;
		dashCooldownTimer = 0f;
		CancelFallEffects();
		heroBox.HeroBoxWallSlide();
	}

	public void WallKickoff()
	{
		float num;
		if (cState.wallSliding)
		{
			num = 10f;
			FlipSprite();
		}
		else
		{
			if (!cState.touchingWall)
			{
				return;
			}
			num = 8f;
			if (cState.facingRight)
			{
				if (touchingWallR)
				{
					FlipSprite();
				}
			}
			else if (touchingWallL)
			{
				FlipSprite();
			}
		}
		rb2d.linearVelocity = new Vector2(cState.facingRight ? num : (0f - num), rb2d.linearVelocity.y);
	}

	private void TryDoWallJumpFromMove()
	{
		if (CanWallJump(mustBeNearWall: false))
		{
			ForceTouchingWall();
			DoWallJump();
		}
	}

	private void LookForQueueInput()
	{
		if (IsInputBlocked() || InteractManager.BlockingInteractable != null || IsPaused() || !isGameplayScene)
		{
			return;
		}
		if (inputHandler.inputActions.SuperDash.WasPressed && (!IsPressingOnlyDown() || !CanSuperJump()))
		{
			if (CanHarpoonDash())
			{
				IncrementAttackCounter();
				startWithHarpoonBounce = false;
				harpoonDashFSM.SendEventSafe("DO MOVE");
				harpoonQueuing = false;
			}
			else
			{
				harpoonQueueSteps = 0;
				harpoonQueuing = true;
			}
		}
		else if (inputHandler.inputActions.SuperDash.IsPressed && harpoonQueueSteps <= HARPOON_QUEUE_STEPS && CanHarpoonDash() && harpoonQueuing)
		{
			IncrementAttackCounter();
			startWithHarpoonBounce = false;
			harpoonDashFSM.SendEventSafe("DO MOVE");
			harpoonQueuing = false;
		}
		if (acceptingInput && queuedWallJumpInterrupt)
		{
			TryDoWallJumpFromMove();
		}
		else if (inputHandler.inputActions.Jump.WasPressed)
		{
			if (acceptingInput && CanWallJump())
			{
				DoWallJump();
			}
			else if (acceptingInput && CanJump())
			{
				HeroJump();
			}
			else if (acceptingInput && !wallLocked && !cState.swimming && CanDoubleJump())
			{
				DoDoubleJump();
			}
			else if (acceptingInput && CanInfiniteAirJump())
			{
				CancelJump();
				audioCtrl.PlaySound(HeroSounds.JUMP, playVibration: true);
				ResetLook();
				cState.jumping = true;
				ResetAttacks(resetNailCharge: false);
			}
			else if (acceptingInput && !wallLocked && CanFloat() && !SlideSurface.IsInJumpGracePeriod)
			{
				StartFloat();
			}
			else if (!TryQueueWallJumpInterrupt())
			{
				jumpQueueSteps = 0;
				jumpQueuing = true;
				if (!cState.swimming && !cState.jumping && !SlideSurface.IsHeroSliding)
				{
					doubleJumpQueueSteps = 0;
					doubleJumpQueuing = true;
				}
			}
		}
		if (inputHandler.inputActions.Dash.WasPressed)
		{
			if (acceptingInput && CanDash())
			{
				HeroDashPressed();
			}
			else
			{
				dashQueueSteps = 0;
				dashQueuing = true;
			}
		}
		if (inputHandler.inputActions.Attack.WasPressed)
		{
			if (acceptingInput && CanAttack())
			{
				DoAttack();
			}
			else
			{
				attackQueueSteps = 0;
				attackQueuing = true;
			}
		}
		if (inputHandler.inputActions.QuickCast.WasPressed)
		{
			if (acceptingInput && CanThrowTool(checkGetWillThrow: false))
			{
				if (GetWillThrowTool(reportFailure: true))
				{
					ThrowTool(isAutoThrow: false);
				}
			}
			else
			{
				toolThrowQueueSteps = 0;
				toolThrowQueueing = true;
			}
		}
		if (!acceptingInput)
		{
			if (inputHandler.inputActions.Jump.WasPressed && Time.frameCount != controlRelinquishedFrame)
			{
				jumpPressedWhileRelinquished = true;
			}
			else if (!inputHandler.inputActions.Jump.IsPressed)
			{
				jumpPressedWhileRelinquished = false;
			}
			return;
		}
		if (inputHandler.inputActions.Jump.IsPressed)
		{
			if (jumpQueueSteps <= JUMP_QUEUE_STEPS && jumpQueuing && CanJump())
			{
				HeroJump();
			}
			else if (doubleJumpQueueSteps <= DOUBLE_JUMP_QUEUE_STEPS && doubleJumpQueuing && CanDoubleJump())
			{
				if (cState.onGround)
				{
					HeroJump();
				}
				else
				{
					DoDoubleJump();
				}
			}
			else if (regainControlJumpQueued && CanFloat())
			{
				StartFloat();
			}
			if (CanSwim() && hero_state != ActorStates.airborne)
			{
				SetState(ActorStates.airborne);
			}
		}
		if (inputHandler.inputActions.Dash.IsPressed && dashQueueSteps <= DASH_QUEUE_STEPS && dashQueuing && CanDash())
		{
			HeroDashPressed();
		}
		if (inputHandler.inputActions.Attack.IsPressed && attackQueueSteps <= ATTACK_QUEUE_STEPS && CanAttack() && attackQueuing)
		{
			DoAttack();
		}
		if (inputHandler.inputActions.QuickCast.IsPressed && toolThrowQueueing && toolThrowQueueSteps <= TOOLTHROW_QUEUE_STEPS && CanThrowTool())
		{
			ThrowTool(isAutoThrow: false);
		}
		regainControlJumpQueued = false;
	}

	public void ResetInputQueues()
	{
		jumpQueuing = false;
		jumpQueueSteps = 0;
		doubleJumpQueuing = false;
		doubleJumpQueueSteps = 0;
		dashQueuing = false;
		dashQueueSteps = 0;
		attackQueuing = false;
		attackQueueSteps = 0;
		toolThrowQueueing = false;
		toolThrowQueueSteps = 0;
		harpoonQueuing = false;
		harpoonQueueSteps = 0;
	}

	private void HeroJump()
	{
		HeroJump(checkSprint: true);
	}

	private void ResetStartWithJumps()
	{
		startWithAnyJump = false;
		startWithShuttlecock = false;
		startWithDoubleJump = false;
		startWithFlipJump = false;
		startWithBackflipJump = false;
		startWithFullJump = false;
	}

	public void PreventShuttlecock()
	{
		noShuttlecockTime = Time.timeAsDouble + 0.30000001192092896;
	}

	private void HeroJump(bool checkSprint)
	{
		animCtrl.UpdateWallScramble();
		cState.downSpikeRecovery = false;
		ResetStartWithJumps();
		if (checkSprint && (sprintBufferSteps > 0 || cState.dashing || cState.isSprinting) && Time.timeAsDouble > noShuttlecockTime)
		{
			OnShuttleCockJump();
		}
		Vector3 position = transform.position;
		jumpEffectPrefab.Spawn(position).Play(base.gameObject, rb2d.linearVelocity, Vector3.zero);
		audioCtrl.PlaySound(HeroSounds.JUMP, playVibration: true);
		gruntAudioTable.SpawnAndPlayOneShot(position);
		ResetLook();
		CancelDoubleJump();
		animCtrl.ResetPlaying();
		cState.recoiling = false;
		ClearJumpInputState();
		cState.jumping = true;
		jumpQueueSteps = 0;
		jumped_steps = 0;
		dashCooldownTimer = 0.05f;
		isDashStabBouncing = false;
		sprintBufferSteps = 0;
		syncBufferSteps = false;
		doubleJumpQueuing = false;
		if (cState.attacking && attack_time >= Config.AttackRecoveryTime)
		{
			CancelAttack();
		}
		OnHeroJumped();
		BecomeAirborne();
		ResetHardLandingTimer();
	}

	private void OnHeroJumped()
	{
		if (cState.onGround || cState.wallClinging || cState.wallSliding)
		{
			airDashed = false;
			doubleJumped = false;
			allowAttackCancellingDownspikeRecovery = false;
		}
	}

	private void BecomeAirborne()
	{
		bool onGround = cState.onGround;
		animCtrl.ResetPlays();
		if (onGround)
		{
			SetCanSoftLand();
		}
		cState.onGround = false;
		SetState(ActorStates.airborne);
		animCtrl.UpdateState(hero_state);
		if (onGround || CheckTouchingGround())
		{
			Vector2 linearVelocity = rb2d.linearVelocity;
			if (linearVelocity.y < 0f)
			{
				rb2d.linearVelocity = new Vector2(linearVelocity.x, 0f);
			}
			rb2d.position += new Vector2(0f, Physics2D.defaultContactOffset * 2f);
		}
	}

	private void HeroJumpNoEffect()
	{
		ClearJumpInputState();
		ResetLook();
		jump_steps = 5;
		cState.jumping = true;
		jumpQueueSteps = 0;
		jumped_steps = 0;
		OnHeroJumped();
	}

	public void ClearActionsInputState()
	{
		inputHandler.inputActions.ClearInputState();
	}

	public void ClearJumpInputState()
	{
		inputHandler.inputActions.Jump.ClearInputState();
		jumpPressedWhileRelinquished = false;
	}

	private void DoWallJump()
	{
		queuedWallJumpInterrupt = false;
		if ((bool)wallPuffPrefab)
		{
			if (wallPuffPrefab.activeInHierarchy)
			{
				FSMUtility.SendEventToGameObject(wallPuffPrefab, "DEACTIVATE");
			}
			wallPuffPrefab.SetActive(value: true);
			bool flag = matchScale != null;
			if (!flag)
			{
				matchScale = wallPuffPrefab.GetComponent<MatchXScaleSignOnEnable>();
				flag = matchScale != null;
			}
			if (flag)
			{
				if (wallSlidingR || touchingWallR)
				{
					matchScale.SetTargetSign(positive: false);
				}
				else if (wallSlidingL || touchingWallL)
				{
					matchScale.SetTargetSign(positive: true);
				}
			}
		}
		if (!playedMantisClawClip)
		{
			audioSource.PlayOneShot(mantisClawClip, 1f);
			playedMantisClawClip = true;
		}
		audioCtrl.PlaySound(HeroSounds.WALLJUMP);
		vibrationCtrl.PlayWallJump();
		if (touchingWallL)
		{
			FaceRight();
			wallJumpedR = true;
			wallJumpedL = false;
		}
		else if (touchingWallR)
		{
			FaceLeft();
			wallJumpedR = false;
			wallJumpedL = true;
		}
		if ((bool)touchingWallObj)
		{
			FSMUtility.SendEventUpwards(touchingWallObj, "HERO WALLSLIDE");
		}
		CancelWallsliding();
		cState.touchingWall = false;
		touchingWallL = false;
		touchingWallR = false;
		touchingWallObj = null;
		airDashed = false;
		doubleJumped = false;
		ShuttleCockCancel();
		cState.mantleRecovery = false;
		currentWalljumpSpeed = WJ_KICKOFF_SPEED;
		walljumpSpeedDecel = (WJ_KICKOFF_SPEED - GetRunSpeed()) / (float)WJLOCK_STEPS_LONG;
		cState.jumping = true;
		wallLockSteps = 0;
		wallLocked = true;
		jumpQueueSteps = 0;
		jumpQueuing = false;
		jumped_steps = 5;
		doubleJumpQueuing = false;
		animCtrl.SetWallJumped();
	}

	private void DoDoubleJump()
	{
		if (SlideSurface.IsInJumpGracePeriod)
		{
			return;
		}
		if (cState.inUpdraft && CanFloat())
		{
			fsm_brollyControl.SendEvent("FORCE UPDRAFT ENTER");
		}
		else if (shuttleCockJumpSteps <= 0)
		{
			if (cState.dashing && dashingDown)
			{
				FinishedDashing(wasDashingDown: true);
			}
			if (cState.jumping)
			{
				Jump();
			}
			doubleJumpEffectPrefab.Spawn(transform, Vector3.zero);
			if (Gameplay.BrollySpikeTool.IsEquipped)
			{
				GameObject obj = Gameplay.BrollySpikeObject_dj.Spawn(transform);
				obj.transform.Translate(0f, 0f, -0.001f);
				obj.transform.Rotate(0f, 0f, -10f);
			}
			vibrationCtrl.PlayDoubleJump();
			if ((bool)audioSource && (bool)doubleJumpClip)
			{
				audioSource.PlayOneShot(doubleJumpClip, 1f);
			}
			Vector2 linearVelocity = rb2d.linearVelocity;
			if (linearVelocity.y < 0f - MAX_FALL_VELOCITY_DJUMP)
			{
				rb2d.linearVelocity = new Vector2(linearVelocity.x, 0f - MAX_FALL_VELOCITY_DJUMP);
			}
			ShuttleCockCancel();
			ResetLook();
			startWithDownSpikeBounceShort = false;
			cState.downSpikeBouncingShort = false;
			startWithDownSpikeBounce = false;
			cState.downSpikeBouncing = false;
			cState.jumping = false;
			cState.doubleJumping = true;
			cState.downSpikeRecovery = false;
			animCtrl.AllowDoubleJumpReEntry();
			if (jumped_steps < JUMP_STEPS_MIN)
			{
				jumped_steps = JUMP_STEPS_MIN;
			}
			doubleJump_steps = 0;
			doubleJumped = true;
			ResetHardLandingTimer();
			this.OnDoubleJumped?.Invoke();
		}
	}

	public void SetBlockFootstepAudio(bool blockFootStep)
	{
		if (audioCtrl != null)
		{
			audioCtrl.BlockFootstepAudio = blockFootStep;
		}
	}

	private void StartFloat()
	{
		CancelQueuedBounces();
		umbrellaFSM.SendEvent("FLOAT");
	}

	public void DoHardLanding()
	{
		if (!cState.hazardRespawning && !cState.hazardDeath)
		{
			if (cState.dashing)
			{
				CancelDash();
			}
			sprintFSM.SendEvent("HARD LANDING");
			BackOnGround();
			AffectedByGravity(gravityApplies: true);
			ResetInput();
			SetState(ActorStates.hard_landing);
			CancelAttack();
			hardLanded = true;
			DoHardLandingEffect();
		}
	}

	public void DoHardLandingEffect()
	{
		DoHardLandingEffectNoHit();
		DeliveryQuestItem.TakeHit();
	}

	public void DoHardLandingEffectNoHit()
	{
		audioCtrl.PlaySound(HeroSounds.HARD_LANDING, playVibration: true);
		hardLandingEffectPrefab.Spawn(transform.position);
	}

	private void HeroDashPressed()
	{
		ToolItem scuttleCharmTool = Gameplay.ScuttleCharmTool;
		if (inputHandler.inputActions.Down.IsPressed && !inputHandler.inputActions.Left.IsPressed && !inputHandler.inputActions.Right.IsPressed && scuttleCharmTool.IsEquipped)
		{
			dashQueueSteps = 0;
			ResetAttacksDash();
			CancelBounce();
			audioCtrl.StopSound(HeroSounds.FOOTSTEPS_RUN);
			audioCtrl.StopSound(HeroSounds.FOOTSTEPS_WALK);
			audioCtrl.StopSound(HeroSounds.FOOTSTEPS_SPRINT);
			ResetLook();
			lookDownBlocked = true;
			gruntAudioTable.SpawnAndPlayOneShot(transform.position);
			toolEventTarget.SendEventSafe("TAKE CONTROL");
			toolEventTarget.SendEvent("SCUTTLE");
		}
		else if (CanWallScramble())
		{
			wallScrambleFSM.enabled = true;
			wallScrambleFSM.SendEvent("SCRAMBLE");
		}
		else
		{
			HeroDash(startAlreadyDashing: false);
		}
	}

	private void HeroDash(bool startAlreadyDashing)
	{
		HeroActions inputActions = inputHandler.inputActions;
		ResetAttacksDash();
		CancelBounce();
		CancelHeroJump();
		ShuttleCockCancelInert();
		audioCtrl.StopSound(HeroSounds.FOOTSTEPS_RUN);
		audioCtrl.StopSound(HeroSounds.FOOTSTEPS_WALK);
		audioCtrl.StopSound(HeroSounds.FOOTSTEPS_SPRINT);
		startWithDash = false;
		if (!startAlreadyDashing)
		{
			bool flag = !cState.onGround;
			audioCtrl.PlaySound(HeroSounds.DASH, !flag);
			gruntAudioTable.SpawnAndPlayOneShot(this.transform.position);
			dashingDown = inputActions.Down.IsPressed && !cState.onGround && !inputActions.Left.IsPressed && !inputActions.Right.IsPressed;
			if (flag)
			{
				vibrationCtrl.PlayAirDash();
			}
		}
		ResetLook();
		if (dashingDown && cState.jumping && jump_steps == 0)
		{
			dashingDown = false;
		}
		if (dashingDown)
		{
			onFlatGround = true;
			tryShove = false;
		}
		if (cState.onGround && !dashingDown)
		{
			dash_timer = DASH_TIME;
			cState.airDashing = false;
			dash_time = 0f;
		}
		else
		{
			if (startAlreadyDashing)
			{
				dash_timer = 0f;
			}
			else
			{
				dash_timer = (dashingDown ? DOWN_DASH_TIME : AIR_DASH_TIME);
				dash_time = 0f;
			}
			cState.airDashing = true;
			airDashed = true;
		}
		cState.recoiling = false;
		if (cState.wallSliding)
		{
			FlipSprite();
		}
		else if (!dashCurrentFacing)
		{
			if (inputActions.Right.IsPressed && !inputActions.Left.IsPressed)
			{
				FaceRight();
			}
			else if (inputActions.Left.IsPressed && !inputActions.Right.IsPressed)
			{
				FaceLeft();
			}
		}
		cState.dashing = true;
		dashQueueSteps = 0;
		wallLocked = false;
		if (!startAlreadyDashing)
		{
			StartDashEffect();
			if (cState.onGround && !cState.shadowDashing)
			{
				Transform transform = this.transform;
				Vector3 localScale = transform.localScale;
				dashEffect = backDashPrefab.Spawn(transform.position);
				dashEffect.transform.localScale = new Vector3(localScale.x * -1f, localScale.y, localScale.z);
				dashEffect.Play(base.gameObject);
			}
		}
		SetDashCooldownTimer();
		sprintFSM.SendEvent("DASHED");
	}

	public void SetDashCooldownTimer()
	{
		dashCooldownTimer = DASH_COOLDOWN;
	}

	public void StartDashEffect()
	{
		if (!cState.wallSliding)
		{
			float num = 1f;
			if (IsUsingQuickening)
			{
				num += 0.25f;
			}
			if (Gameplay.SprintmasterTool.IsEquipped)
			{
				num += 0.25f;
			}
			if (!Mathf.Approximately(sprintSpeedAddFloat.Value, 0f))
			{
				num += 0.25f;
			}
			if (cState.onGround)
			{
				dashBurstPrefab.transform.localScale = new Vector3(0f - num, num, num);
				dashBurstPrefab.SetActive(value: false);
				dashBurstPrefab.SetActive(value: true);
				return;
			}
			if (dashingDown)
			{
				airDashEffect.transform.SetLocalRotation2D(90f);
			}
			else
			{
				airDashEffect.transform.SetLocalRotation2D(0f);
			}
			airDashEffect.transform.localScale = new Vector3(num, num, num);
			airDashEffect.SetActive(value: false);
			airDashEffect.SetActive(value: true);
		}
		else
		{
			walldashKickoffEffect.SetActive(value: false);
			walldashKickoffEffect.SetActive(value: true);
		}
	}

	private void StartFallRumble()
	{
		fallRumble = true;
		audioCtrl.PlaySound(HeroSounds.FALLING);
		GameCameras.instance.cameraShakeFSM.Fsm.Variables.FindFsmBool("RumblingFall").Value = true;
	}

	public bool IsOnWall()
	{
		if (!cState.wallSliding && !cState.wallClinging)
		{
			return cState.wallScrambling;
		}
		return true;
	}

	private bool CanExitNoInput()
	{
		if (!doingHazardRespawn)
		{
			return !cState.dead;
		}
		return false;
	}

	private void SetState(ActorStates newState)
	{
		if (hero_state == ActorStates.no_input && !CanExitNoInput())
		{
			return;
		}
		switch (newState)
		{
		case ActorStates.grounded:
			newState = ((!(Mathf.Abs(move_input) > Mathf.Epsilon)) ? ActorStates.idle : ActorStates.running);
			heroBox.HeroBoxNormal();
			break;
		case ActorStates.idle:
		case ActorStates.running:
		case ActorStates.airborne:
			if (!cState.wallSliding && !cState.wallClinging)
			{
				heroBox.HeroBoxNormal();
			}
			break;
		case ActorStates.previous:
			newState = prev_hero_state;
			break;
		}
		if (newState != hero_state)
		{
			prev_hero_state = hero_state;
			hero_state = newState;
			animCtrl.UpdateState(newState);
		}
	}

	private void FinishedEnteringScene(bool setHazardMarker = true, bool preventRunBob = false)
	{
		if (isEnteringFirstLevel)
		{
			isEnteringFirstLevel = false;
		}
		else
		{
			playerData.disablePause = false;
		}
		animCtrl.waitingToEnter = false;
		Vector3 position = transform.position;
		doingHazardRespawn = false;
		cState.transitioning = false;
		transitionState = HeroTransitionState.WAITING_TO_TRANSITION;
		stopWalkingOut = false;
		ForceRunningSound = false;
		ForceWalkingSound = false;
		if (exitedSuperDashing || exitedQuake || exitedSprinting)
		{
			controlReqlinquished = true;
			IgnoreInput();
		}
		else
		{
			SetStartingMotionState(preventRunBob);
			AffectedByGravity(gravityApplies: true);
		}
		if (setHazardMarker)
		{
			if (gm.startedOnThisScene || sceneEntryGate == null)
			{
				playerData.SetHazardRespawn(position, cState.facingRight);
			}
			else if (!sceneEntryGate.nonHazardGate)
			{
				playerData.SetHazardRespawn(sceneEntryGate.respawnMarker);
			}
		}
		if (exitedQuake)
		{
			SetDamageMode(DamageMode.HAZARD_ONLY);
		}
		else
		{
			SetDamageMode(DamageMode.FULL_DAMAGE);
		}
		if (enterWithoutInput || exitedSuperDashing || exitedQuake || exitedSprinting)
		{
			enterWithoutInput = false;
		}
		else
		{
			AcceptInput();
		}
		SetSilkRegenBlocked(isBlocked: false);
		gm.FinishedEnteringScene();
		ResetSceneExitedStates();
		positionHistory[0] = position;
		positionHistory[1] = position;
		tilemapTestActive = true;
		if ((bool)sceneEntryGate)
		{
			sceneEntryGate.AfterEntry();
		}
		InteractManager.SetEnabledDelay(0.5f);
		skipNormalEntry = false;
	}

	public void ResetSceneExitedStates()
	{
		exitedSuperDashing = false;
		exitedQuake = false;
		exitedSprinting = false;
	}

	private IEnumerator Die(bool nonLethal, bool frostDeath)
	{
		if (hazardRespawnRoutine != null)
		{
			StopCoroutine(hazardRespawnRoutine);
			hazardRespawnRoutine = null;
		}
		ResetSilkRegen();
		audioCtrl.StopSound(HeroSounds.FOOTSTEPS_WALK);
		audioCtrl.StopSound(HeroSounds.FOOTSTEPS_RUN);
		this.OnDeath?.Invoke();
		DeliveryQuestItem.BreakAll();
		if (cState.dead)
		{
			yield break;
		}
		EventRegister.SendEvent(EventRegisterEvents.HeroDeath);
		EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
		playerData.disablePause = true;
		boundsChecking = false;
		updraftsEntered = 0;
		StopTilemapTest();
		cState.onConveyor = false;
		cState.onConveyorV = false;
		rb2d.linearVelocity = new Vector2(0f, 0f);
		CancelRecoilHorizontal();
		bool flag = gm.IsMemoryScene();
		if (flag)
		{
			nonLethal = true;
		}
		if (nonLethal)
		{
			if (!string.IsNullOrEmpty(playerData.nonLethalRespawnMarker))
			{
				playerData.tempRespawnMarker = playerData.nonLethalRespawnMarker;
				playerData.nonLethalRespawnMarker = null;
			}
			if (playerData.nonLethalRespawnType != 0)
			{
				playerData.tempRespawnType = playerData.nonLethalRespawnType;
				playerData.nonLethalRespawnType = 0;
			}
			if (!string.IsNullOrEmpty(playerData.nonLethalRespawnScene))
			{
				playerData.tempRespawnScene = playerData.nonLethalRespawnScene;
				playerData.nonLethalRespawnScene = null;
			}
		}
		else if (playerData.permadeathMode == PermadeathModes.On)
		{
			playerData.permadeathMode = PermadeathModes.Dead;
		}
		AffectedByGravity(gravityApplies: false);
		HeroBox.Inactive = true;
		cState.falling = false;
		rb2d.isKinematic = true;
		SetState(ActorStates.no_input);
		cState.dead = true;
		cState.isFrostDeath = frostDeath;
		ResetMotion();
		ResetHardLandingTimer();
		renderer.enabled = false;
		base.gameObject.layer = 2;
		heroBox.HeroBoxOff();
		GameObject gameObject = GetHeroDeathPrefab(nonLethal, flag, frostDeath);
		if ((bool)vibrationCtrl)
		{
			vibrationCtrl.PlayHeroDeath();
		}
		GameObject obj = gameObject.Spawn();
		obj.transform.position = transform.position;
		obj.transform.localScale = transform.localScale.MultiplyElements(gameObject.transform.localScale);
		obj.SetActive(value: true);
		tk2dSpriteAnimator component = obj.GetComponent<tk2dSpriteAnimator>();
		if ((bool)component)
		{
			component.Library = animCtrl.animator.Library;
		}
		if (!nonLethal)
		{
			HeroCorpseMarkerProxy heroCorpseMarkerProxy = HeroCorpseMarkerProxy.Instance;
			if ((bool)heroCorpseMarkerProxy)
			{
				playerData.HeroCorpseScene = heroCorpseMarkerProxy.TargetSceneName;
				playerData.HeroCorpseMarkerGuid = heroCorpseMarkerProxy.TargetGuid;
				playerData.HeroDeathScenePos = heroCorpseMarkerProxy.TargetScenePos;
			}
			else
			{
				Vector3 position = transform.position;
				playerData.HeroCorpseScene = gm.GetSceneNameString();
				HeroCorpseMarker closest = HeroCorpseMarker.GetClosest(position);
				if ((bool)closest)
				{
					playerData.HeroCorpseMarkerGuid = closest.Guid.ToByteArray();
					playerData.HeroDeathScenePos = closest.Position;
				}
				else
				{
					playerData.HeroCorpseMarkerGuid = null;
					playerData.HeroDeathScenePos = position;
				}
			}
			tk2dTileMap tilemap = gm.tilemap;
			playerData.HeroDeathSceneSize = new Vector2(tilemap.width, tilemap.height);
			gm.gameMap.PositionCompassAndCorpse();
			playerData.IsSilkSpoolBroken = true;
			playerData.HeroCorpseType = HeroDeathCocoonTypes.Normal;
			int num = playerData.geo;
			bool isEquipped = Gameplay.DeadPurseTool.IsEquipped;
			if (isEquipped)
			{
				int num2 = Mathf.RoundToInt((float)num * Gameplay.DeadPurseHoldPercent);
				num -= num2;
				playerData.geo = num2;
			}
			else
			{
				playerData.geo = 0;
			}
			playerData.HeroCorpseMoneyPool = Mathf.RoundToInt(num);
			if (playerData.IsAnyCursed)
			{
				playerData.HeroCorpseType |= HeroDeathCocoonTypes.Cursed;
			}
			if (isEquipped && playerData.HeroCorpseMoneyPool >= 10)
			{
				playerData.HeroCorpseType |= HeroDeathCocoonTypes.Rosaries;
			}
		}
		playerData.silk = 0;
		playerData.silkParts = 0;
		GameCameras.instance.silkSpool.RefreshSilk();
		ClearSpoolMossChunks();
		EventRegister.SendEvent("TOOL EQUIPS CHANGED");
		float deathWait = (frostDeath ? 5.1f : DEATH_WAIT);
		HeroDeathSequence component2 = obj.GetComponent<HeroDeathSequence>();
		if (component2 != null)
		{
			deathWait = component2.DeathWait;
		}
		yield return null;
		StartCoroutine(gm.PlayerDead(deathWait));
		if (!frostDeath)
		{
			yield return new WaitForSeconds(2.45f);
			frostAmount = 0f;
			StatusVignette.SetFrostVignetteAmount(frostAmount);
		}
	}

	private GameObject GetHeroDeathPrefab(bool nonLethal, bool inMemoryScene, bool isFrostDamage)
	{
		if (inMemoryScene && (bool)heroDeathMemoryPrefab)
		{
			return heroDeathMemoryPrefab;
		}
		if (nonLethal && (bool)heroDeathNonLethalPrefab)
		{
			return heroDeathNonLethalPrefab;
		}
		if (playerData.IsAnyCursed && (bool)heroDeathCursedPrefab)
		{
			return heroDeathCursedPrefab;
		}
		if (isFrostDamage && (bool)heroDeathFrostPrefab)
		{
			return heroDeathFrostPrefab;
		}
		return heroDeathPrefab;
	}

	private void ElevatorReset()
	{
		SetHeroParent(null);
		if (rb2d.interpolation != RigidbodyInterpolation2D.Interpolate)
		{
			rb2d.interpolation = RigidbodyInterpolation2D.Interpolate;
		}
	}

	private IEnumerator DieFromHazard(HazardType hazardType, float angle)
	{
		audioCtrl.StopSound(HeroSounds.FOOTSTEPS_WALK);
		audioCtrl.StopSound(HeroSounds.FOOTSTEPS_RUN);
		if (!cState.hazardDeath)
		{
			CancelDamageRecoil();
			playerData.disablePause = true;
			ElevatorReset();
			if (this.OnHazardDeath != null)
			{
				this.OnHazardDeath();
			}
			StopTilemapTest();
			SetState(ActorStates.no_input);
			cState.hazardDeath = true;
			ResetMotion();
			ResetHardLandingTimer();
			AffectedByGravity(gravityApplies: false);
			renderer.enabled = false;
			base.gameObject.layer = 2;
			heroBox.HeroBoxOff();
			EventRegister.SendEvent(EventRegisterEvents.HeroHazardDeath);
			if ((bool)vibrationCtrl)
			{
				vibrationCtrl.PlayHeroHazardDeath();
			}
			float hazardDeathWait = 0f;
			switch (hazardType)
			{
			case HazardType.SPIKES:
			{
				GameObject obj7 = spikeDeathPrefab.Spawn();
				obj7.transform.position = transform.position;
				FSMUtility.SetFloat(obj7.GetComponent<PlayMakerFSM>(), "Spike Direction", angle * 57.29578f);
				break;
			}
			case HazardType.COAL_SPIKES:
			{
				GameObject obj6 = coalSpikeDeathPrefab.Spawn();
				obj6.transform.position = transform.position;
				FSMUtility.SetFloat(obj6.GetComponent<PlayMakerFSM>(), "Spike Direction", angle * 57.29578f);
				break;
			}
			case HazardType.ACID:
			{
				GameObject obj5 = acidDeathPrefab.Spawn();
				obj5.transform.position = transform.position;
				obj5.transform.localScale = transform.localScale;
				break;
			}
			case HazardType.LAVA:
			{
				GameObject obj4 = lavaDeathPrefab.Spawn();
				obj4.transform.position = transform.position;
				obj4.transform.localScale = transform.localScale;
				DeliveryQuestItem.BreakAll();
				break;
			}
			case HazardType.COAL:
			{
				GameObject obj3 = coalDeathPrefab.Spawn();
				obj3.transform.position = transform.position;
				obj3.transform.localScale = transform.localScale;
				break;
			}
			case HazardType.ZAP:
				zapDeathPrefab.Spawn().transform.position = transform.position;
				hazardDeathWait = 0.5f;
				break;
			case HazardType.SINK:
			{
				GameObject obj2 = sinkDeathPrefab.Spawn();
				obj2.transform.position = transform.position;
				obj2.transform.localScale = transform.localScale;
				break;
			}
			case HazardType.STEAM:
			{
				GameObject obj = steamDeathPrefab.Spawn();
				obj.transform.position = transform.position;
				obj.transform.localScale = transform.localScale;
				break;
			}
			}
			switch (hazardType)
			{
			case HazardType.LAVA:
				hazardDamageAudioTable.SpawnAndPlayOneShot(transform.position);
				break;
			case HazardType.PIT:
			case HazardType.RESPAWN_PIT:
				pitFallAudioTable.SpawnAndPlayOneShot2D(transform.position);
				break;
			default:
				woundAudioTable.SpawnAndPlayOneShot(transform.position);
				break;
			}
			yield return null;
			hazardRespawnRoutine = StartCoroutine(gm.PlayerDeadFromHazard(hazardDeathWait));
		}
	}

	private IEnumerator StartRecoil(CollisionSide impactSide, int damageAmount)
	{
		if (!cState.recoiling)
		{
			float num = RECOIL_VELOCITY;
			float num2 = INVUL_TIME;
			ToolItem weightedAnkletTool = Gameplay.WeightedAnkletTool;
			if ((bool)weightedAnkletTool && weightedAnkletTool.IsEquipped)
			{
				num *= Gameplay.WeightedAnkletDmgKnockbackMult;
				num2 *= Gameplay.WeightedAnkletDmgInvulnMult;
			}
			playerData.disablePause = true;
			ResetMotion();
			AffectedByGravity(gravityApplies: false);
			switch (impactSide)
			{
			case CollisionSide.left:
				recoilVector = new Vector2(num, num * 0.5f);
				if (cState.facingRight)
				{
					FlipSprite();
				}
				break;
			case CollisionSide.right:
				recoilVector = new Vector2(0f - num, num * 0.5f);
				if (!cState.facingRight)
				{
					FlipSprite();
				}
				break;
			default:
				recoilVector = Vector2.zero;
				break;
			}
			SetState(ActorStates.no_input);
			cState.recoilFrozen = true;
			cState.onGround = false;
			cState.wasOnGround = false;
			ledgeBufferSteps = 0;
			sprintBufferSteps = 0;
			syncBufferSteps = false;
			if (damageAmount > 0)
			{
				damageEffectFSM.SendEvent("DAMAGE");
			}
			StartInvulnerable(num2);
			yield return takeDamageCoroutine = StartCoroutine(gm.FreezeMoment(DAMAGE_FREEZE_DOWN, DAMAGE_FREEZE_WAIT, DAMAGE_FREEZE_UP, DAMAGE_FREEZE_SPEED));
			cState.recoilFrozen = false;
			cState.recoiling = true;
			if (!cState.dead)
			{
				renderer.enabled = true;
			}
			playerData.disablePause = false;
		}
		recoilRoutine = null;
	}

	private void StartInvulnerable(float duration)
	{
		if (hazardInvulnRoutine == null)
		{
			cState.invulnerable = true;
			invulnerableFreezeDuration = DAMAGE_FREEZE_DOWN;
			invulnerableDuration = duration;
			hazardInvulnRoutine = StartCoroutine(Invulnerable());
		}
		else if (invulnerableFreezeDuration + invulnerableDuration < duration + DAMAGE_FREEZE_DOWN)
		{
			if (invulnerableFreezeDuration > 0f)
			{
				invulnerableFreezeDuration = DAMAGE_FREEZE_DOWN;
			}
			else
			{
				duration += DAMAGE_FREEZE_DOWN;
			}
			invulnerableDuration = duration;
		}
	}

	private IEnumerator Invulnerable()
	{
		cState.invulnerable = true;
		while (invulnerableFreezeDuration > 0f)
		{
			yield return null;
			invulnerableFreezeDuration -= Time.deltaTime;
		}
		invPulse.StartInvulnerablePulse();
		while (invulnerableDuration > 0f)
		{
			yield return null;
			invulnerableDuration -= Time.deltaTime;
		}
		invPulse.StopInvulnerablePulse();
		cState.invulnerable = false;
		cState.recoiling = false;
		hazardInvulnRoutine = null;
	}

	public void AddInvulnerabilitySource(object source)
	{
		cState.AddInvulnerabilitySource(source);
	}

	public void RemoveInvulnerabilitySource(object source)
	{
		cState.RemoveInvulnerabilitySource(source);
	}

	private IEnumerator FirstFadeIn()
	{
		yield return new WaitForSeconds(0.25f);
		gm.FadeSceneIn();
		fadedSceneIn = true;
	}

	private void FallCheck()
	{
		if (rb2d.linearVelocity.y <= Mathf.Epsilon && !CheckTouchingGround())
		{
			cState.falling = true;
			cState.wallJumping = false;
			LeftGround(hero_state != ActorStates.no_input);
			if ((!controlReqlinquished || cState.isSprinting || (cState.isBinding && Gameplay.SpellCrest.IsEquipped)) && rb2d.linearVelocity.y <= 0f - MAX_FALL_VELOCITY && !cState.wallSliding)
			{
				fallTimer += Time.deltaTime;
			}
			else
			{
				fallTimer = 0f;
			}
			if (fallTimer > BIG_FALL_TIME)
			{
				if (!cState.willHardLand)
				{
					cState.willHardLand = true;
				}
				if (!fallRumble)
				{
					StartFallRumble();
				}
			}
			if (fallCheckFlagged)
			{
				fallCheckFlagged = false;
			}
		}
		else
		{
			cState.falling = false;
			fallTimer = 0f;
			if (fallCheckFlagged)
			{
				fallCheckFlagged = false;
			}
			if (fallRumble)
			{
				CancelFallEffects();
			}
		}
	}

	private void OutOfBoundsCheck()
	{
		if (isGameplayScene)
		{
			Vector2 vector = transform.position;
			if ((vector.y < -60f || vector.y > gm.sceneHeight + 60f || vector.x < -60f || vector.x > gm.sceneWidth + 60f) && !cState.dead)
			{
				_ = boundsChecking;
			}
		}
	}

	private void ConfirmOutOfBounds()
	{
		if (!boundsChecking)
		{
			return;
		}
		Vector2 vector = transform.position;
		if (vector.y < -60f || vector.y > gm.sceneHeight + 60f || vector.x < -60f || vector.x > gm.sceneWidth + 60f)
		{
			if (!cState.dead)
			{
				rb2d.linearVelocity = Vector2.zero;
				Debug.LogFormat("Pos: {0} Transition State: {1}", transform.position, transitionState);
			}
		}
		else
		{
			boundsChecking = false;
		}
	}

	private void FailSafeChecks()
	{
		if (hero_state == ActorStates.hard_landing)
		{
			hardLandFailSafeTimer += Time.deltaTime;
			if (hardLandFailSafeTimer > HARD_LANDING_TIME + 0.3f)
			{
				SetState(ActorStates.grounded);
				BackOnGround();
				hardLandFailSafeTimer = 0f;
			}
		}
		else
		{
			hardLandFailSafeTimer = 0f;
		}
		if (cState.hazardDeath)
		{
			hazardDeathTimer += Time.deltaTime;
			if (hazardDeathTimer > HAZARD_DEATH_CHECK_TIME && hero_state != ActorStates.no_input)
			{
				ResetMotion();
				AffectedByGravity(gravityApplies: false);
				SetState(ActorStates.no_input);
				hazardDeathTimer = 0f;
			}
		}
		else
		{
			hazardDeathTimer = 0f;
		}
		if (rb2d.linearVelocity.y != 0f || cState.onGround || cState.falling || cState.jumping || cState.dashing || hero_state == ActorStates.hard_landing || hero_state == ActorStates.dash_landing || hero_state == ActorStates.no_input)
		{
			return;
		}
		if (CheckTouchingGround())
		{
			floatingBufferTimer += Time.deltaTime;
			if (floatingBufferTimer > FLOATING_CHECK_TIME)
			{
				if (cState.recoiling)
				{
					CancelDamageRecoil();
				}
				BackOnGround();
				floatingBufferTimer = 0f;
			}
		}
		else
		{
			floatingBufferTimer = 0f;
		}
	}

	public Transform LocateSpawnPoint()
	{
		string text = ((!string.IsNullOrEmpty(playerData.tempRespawnMarker)) ? playerData.tempRespawnMarker : (string.IsNullOrEmpty(gm.LastSceneLoad.SceneLoadInfo.EntryGateName) ? playerData.respawnMarkerName : gm.LastSceneLoad.SceneLoadInfo.EntryGateName));
		foreach (RespawnMarker marker in RespawnMarker.Markers)
		{
			if (marker.name == text && marker.gameObject.activeInHierarchy)
			{
				return marker.transform;
			}
		}
		foreach (RespawnMarker marker2 in RespawnMarker.Markers)
		{
			if (marker2.name == text)
			{
				return marker2.transform;
			}
		}
		RespawnMarker[] array = UnityEngine.Object.FindObjectsByType<RespawnMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		foreach (RespawnMarker respawnMarker in array)
		{
			if (respawnMarker.name == text)
			{
				return respawnMarker.transform;
			}
		}
		return null;
	}

	private void CancelJump()
	{
		cState.jumping = false;
		jumpReleaseQueuing = false;
		jump_steps = 0;
		useUpdraftExitJumpSpeed = false;
		wallLocked = false;
	}

	private void CancelDoubleJump()
	{
		cState.doubleJumping = false;
		doubleJump_steps = 0;
	}

	private void CancelDash(bool sendSprintEvent = true)
	{
		if (cState.shadowDashing)
		{
			cState.shadowDashing = false;
		}
		cState.dashing = false;
		dashQueuing = false;
		heroBox.HeroBoxNormal();
		dashingDown = false;
		cState.airDashing = false;
		dash_timer = 0f;
		AffectedByGravity(gravityApplies: true);
		StopDashEffect();
		if (sendSprintEvent)
		{
			sprintFSM.SendEvent("CANCEL SPRINT");
		}
	}

	public void StopDashEffect()
	{
	}

	private void CancelWallsliding()
	{
		ParticleSystem.EmissionModule emission = wallslideDustPrefab.emission;
		emission.enabled = false;
		if (cState.wallSliding)
		{
			cState.wallSliding = false;
			cState.wallClinging = false;
			vibrationCtrl.StopWallSlide();
			heroBox.HeroBoxNormal();
		}
		AffectedByGravity(gravityApplies: true);
		cState.touchingWall = false;
		wallSlidingL = false;
		wallSlidingR = false;
		touchingWallL = false;
		touchingWallR = false;
		touchingWallObj = null;
	}

	private void CancelBackDash()
	{
		cState.backDashing = false;
	}

	private void CancelDownAttack()
	{
		if (cState.downAttacking)
		{
			ResetAttacks();
		}
	}

	public void CancelAttack()
	{
		CancelAttack(resetNailCharge: true);
	}

	public void CancelAttack(bool resetNailCharge)
	{
		CancelAttackNotDownspikeBounce(resetNailCharge);
		if (cState.downSpikeBouncing)
		{
			cState.downSpikeBouncing = false;
		}
	}

	public void CancelDownspike()
	{
		if ((bool)currentDownspike)
		{
			currentDownspike.CancelAttack();
		}
	}

	public void CancelDownspikeAndDamage()
	{
		if ((bool)currentDownspike)
		{
			currentDownspike.CancelAttackAndDamage();
		}
	}

	public void CancelAttackNotDownspikeBounce()
	{
		CancelAttackNotDownspikeBounce(resetNailCharge: true);
	}

	public void CancelAttackNotDownspikeBounce(bool resetNailCharge)
	{
		if (cState.attacking && SlashComponent != null)
		{
			SlashComponent.CancelAttack(forceHide: false);
		}
		CancelAttackNotSlash(resetNailCharge);
	}

	private void CancelAttackNotSlash(bool resetNailCharge)
	{
		ResetAttacks(resetNailCharge);
		if (cState.downSpikeAntic)
		{
			cState.downSpikeAntic = false;
			if (!cState.downSpiking)
			{
				FinishDownspike();
			}
		}
		if (cState.downSpiking)
		{
			FinishDownspike();
		}
		if (cState.isToolThrowing)
		{
			ThrowToolEnd();
		}
	}

	private void CancelBounce()
	{
		cState.bouncing = false;
		cState.shroomBouncing = false;
		bounceTimer = 0f;
		cState.downSpikeBouncing = false;
		cState.downSpikeBouncingShort = false;
		CancelQueuedBounces();
	}

	public void CancelRecoilHorizontal()
	{
		cState.recoilingLeft = false;
		cState.recoilingRight = false;
		cState.recoilingDrill = false;
		recoilStepsLeft = 0;
	}

	public void CancelDamageRecoil()
	{
		if (recoilRoutine != null)
		{
			StopCoroutine(recoilRoutine);
			recoilRoutine = null;
			cState.recoilFrozen = false;
			if (!cState.dead)
			{
				renderer.enabled = true;
			}
			playerData.disablePause = false;
		}
		cState.recoiling = false;
		recoilTimer = 0f;
		ResetMotion();
		AffectedByGravity(gravityApplies: true);
		SetDamageMode(DamageMode.FULL_DAMAGE);
	}

	public void CancelDamageRecoilSimple()
	{
		if (recoilRoutine != null)
		{
			StopCoroutine(recoilRoutine);
			recoilRoutine = null;
			cState.recoilFrozen = false;
			if (!cState.dead)
			{
				renderer.enabled = true;
			}
			playerData.disablePause = false;
		}
		cState.recoiling = false;
		recoilTimer = 0f;
	}

	private void CancelFallEffects()
	{
		fallRumble = false;
		audioCtrl.StopSound(HeroSounds.FALLING);
		GameCameras.instance.cameraShakeFSM.Fsm.Variables.FindFsmBool("RumblingFall").Value = false;
	}

	private void ResetAttacksShared()
	{
		cState.attacking = false;
		cState.upAttacking = false;
		if (cState.downAttacking || tryCancelDownSlash)
		{
			cState.downAttacking = false;
			tryCancelDownSlash = false;
			if ((bool)SlashComponent)
			{
				SlashComponent.CancelAttack();
			}
		}
		attack_time = 0f;
	}

	private void ResetAttacks(bool resetNailCharge = true)
	{
		ResetAttacksShared();
		wallSlashing = false;
		isDashStabBouncing = false;
		if (!allowNailChargingWhileRelinquished && resetNailCharge)
		{
			CancelNailCharge();
		}
		if (!queuedAutoThrowTool || resetNailCharge)
		{
			ThrowToolEnd();
		}
	}

	private void StopNailChargeEffects()
	{
		artChargeEffect.SetActive(value: false);
		audioCtrl.StopSound(HeroSounds.NAIL_ART_CHARGE);
	}

	private void CancelNailCharge()
	{
		StopNailChargeEffects();
		cState.nailCharging = false;
		nailChargeTimer = 0f;
	}

	private void ResetAttacksDash()
	{
		ResetAttacksShared();
		ThrowToolEnd();
	}

	private void ResetMotion(bool resetNailCharge = true)
	{
		CancelDownAttack();
		CancelJump();
		CancelDoubleJump();
		CancelDash();
		CancelBackDash();
		CancelBounce();
		CancelRecoilHorizontal();
		CancelWallsliding();
		cState.floating = false;
		cState.downSpiking = false;
		ShuttleCockCancel();
		ResetShuttlecock();
		rb2d.linearVelocity = Vector2.zero;
		transition_vel = Vector2.zero;
		wallLocked = false;
		queuedWallJumpInterrupt = false;
		startWithRecoilBack = false;
		startWithWhipPullRecoil = false;
		extraAirMoveVelocities.Clear();
		if (!allowNailChargingWhileRelinquished && resetNailCharge)
		{
			nailChargeTimer = 0f;
		}
		ResetGravity();
	}

	private void ResetMotionNotVelocity()
	{
		CancelJump();
		ShuttleCockCancel();
		CancelDoubleJump();
		CancelDash();
		CancelBackDash();
		CancelBounce();
		CancelRecoilHorizontal();
		CancelWallsliding();
		transition_vel = Vector2.zero;
		wallLocked = false;
		ResetGravity();
	}

	public void ResetLook()
	{
		cState.lookingUp = false;
		cState.lookingDown = false;
		cState.lookingUpAnim = false;
		cState.lookingDownAnim = false;
		lookDelayTimer = 0f;
	}

	private void ResetInput()
	{
		move_input = 0f;
		vertical_input = 0f;
	}

	public bool CheckAndRequestUnlock(HeroLockStates lockStates)
	{
		if (IsBlocked(lockStates))
		{
			AddUnlockRequest(lockStates);
			return true;
		}
		return false;
	}

	public bool IsBlocked(HeroLockStates lockStates)
	{
		if (lockStates == HeroLockStates.None)
		{
			return false;
		}
		return (HeroLockState & lockStates) == lockStates;
	}

	public void AddLockStates(HeroLockStates lockStates)
	{
		HeroLockState |= lockStates;
	}

	public void RemoveLockStates(HeroLockStates lockStates)
	{
		HeroLockState &= ~lockStates;
		if (lockStates.HasFlag(HeroLockStates.AnimationLocked) && unlockRequests.HasFlag(HeroLockStates.AnimationLocked))
		{
			StartAnimationControl();
		}
		if (lockStates.HasFlag(HeroLockStates.ControlLocked) && unlockRequests.HasFlag(HeroLockStates.ControlLocked))
		{
			RegainControl();
		}
		if (lockStates.HasFlag(HeroLockStates.GravityLocked) && unlockRequests.HasFlag(HeroLockStates.GravityLocked))
		{
			AffectedByGravity(gravityApplies: true);
		}
	}

	public void SetLockStates(HeroLockStates lockStates)
	{
		HeroLockState = lockStates;
	}

	public void AddUnlockRequest(HeroLockStates lockStates)
	{
		unlockRequests |= lockStates;
	}

	public void RemoveUnlockRequest(HeroLockStates lockStates)
	{
		unlockRequests &= ~lockStates;
	}

	private void BackOnGround(bool force = false)
	{
		cState.willHardLand = false;
		hardLandingTimer = 0f;
		hardLanded = false;
		sprintBufferSteps = 0;
		syncBufferSteps = false;
		if (cState.onGround && !force)
		{
			return;
		}
		if (landingBufferSteps <= 0 && isHeroInPosition)
		{
			landingBufferSteps = LANDING_BUFFER_STEPS;
			if (!hardLanded && !cState.superDashing && (!controlReqlinquished || airDashed))
			{
				SpawnSoftLandingPrefab();
			}
		}
		cState.falling = false;
		fallTimer = 0f;
		dashLandingTimer = 0f;
		cState.floating = false;
		doFullJump = false;
		jump_steps = 0;
		extraAirMoveVelocities.Clear();
		if (hero_state != ActorStates.no_input)
		{
			if (cState.doubleJumping)
			{
				HeroJump();
			}
			SetState(ActorStates.grounded);
			if (!SlashComponent || (!SlashComponent.IsSlashOut && !SlashComponent.IsStartingSlash))
			{
				ResetAttacks(resetNailCharge: false);
			}
		}
		cState.onGround = true;
		airDashed = false;
		doubleJumped = false;
		allowAttackCancellingDownspikeRecovery = false;
	}

	private void JumpReleased()
	{
		if (rb2d.linearVelocity.y > 0f && jumped_steps >= JUMP_STEPS_MIN && !cState.shroomBouncing && !doFullJump)
		{
			if (jumpReleaseQueueingEnabled)
			{
				if (jumpReleaseQueuing && jumpReleaseQueueSteps <= 0)
				{
					rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, rb2d.linearVelocity.y * 0.5f);
					CancelJump();
				}
			}
			else
			{
				rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, rb2d.linearVelocity.y * 0.5f);
				CancelJump();
			}
		}
		jumpQueuing = false;
		doubleJumpQueuing = false;
	}

	public void SetDoFullJump()
	{
		doFullJump = true;
	}

	public void FinishedDashing(bool wasDashingDown)
	{
		if (cState.dashing)
		{
			audioCtrl.AllowFootstepsGrace();
		}
		CancelDash(sendSprintEvent: false);
		AffectedByGravity(gravityApplies: true);
		animCtrl.FinishedDash();
		proxyFSM.SendEvent("HeroCtrl-DashEnd");
		if (cState.onGround)
		{
			if (wasDashingDown)
			{
				sprintFSM.SendEvent("CANCEL SPRINT");
			}
			else if (!IsInputBlocked())
			{
				sprintFSM.SendEvent("TRY SPRINT");
			}
		}
		else if (wasDashingDown)
		{
			sprintFSM.SendEvent("CANCEL SPRINT");
			animCtrl.SetDownDashEnded();
			if ((bool)audioSource && (bool)downDashCancelClip)
			{
				audioSource.PlayOneShot(downDashCancelClip, 1f);
			}
		}
		else
		{
			ResetHardLandingTimer();
			if (!IsInputBlocked())
			{
				sprintFSM.SendEvent("TRY SPRINT");
			}
		}
	}

	public void FinishDownspike()
	{
		FinishDownspike(standardRecovery: true);
	}

	public void FinishDownspike(bool standardRecovery)
	{
		cState.downSpiking = false;
		if (standardRecovery)
		{
			downSpikeRecoveryTimer = 0f;
		}
		else
		{
			downSpikeRecoveryTimer = 0.1f;
		}
		if (!cState.floating)
		{
			cState.downSpikeRecovery = true;
			ResetGravity();
			RegainControl();
			crestAttacksFSM.SendEvent("CREST ATTACK CANCEL");
			if (!startWithBalloonBounce)
			{
				rb2d.linearVelocity = new Vector3(rb2d.linearVelocity.x, -10f, 0f);
			}
		}
		if ((bool)currentDownspike)
		{
			currentDownspike.CancelAttack();
		}
	}

	private void SetStartingMotionState()
	{
		SetStartingMotionState(preventRunDip: false);
	}

	private void SetStartingMotionState(bool preventRunDip)
	{
		if (IsInputBlocked() || (gm.GameState != GameState.PLAYING && gm.GameState != GameState.ENTERING_LEVEL))
		{
			move_input = 0f;
		}
		else
		{
			move_input = ((acceptingInput || preventRunDip) ? inputHandler.inputActions.MoveVector.X : 0f);
		}
		FilterInput();
		extraAirMoveVelocities.Clear();
		cState.touchingWall = false;
		if (CheckTouchingGround() && !startWithFullJump)
		{
			if (!cState.onGround && cState.isSprinting)
			{
				SetState(ActorStates.airborne);
				BackOnGround();
			}
			cState.onGround = true;
			allowAttackCancellingDownspikeRecovery = false;
			SetState(ActorStates.grounded);
			if (enteringVertically)
			{
				enteringVertically = false;
				if (playerData.bindCutscenePlayed)
				{
					SpawnSoftLandingPrefab();
					animCtrl.playLanding = true;
				}
			}
		}
		else
		{
			cState.onGround = false;
			SetState(ActorStates.airborne);
		}
		animCtrl.UpdateState(hero_state);
	}

	private void TileMapTest()
	{
		if (!tilemapTestActive || cState.jumping)
		{
			return;
		}
		Vector2 origin = transform.position;
		Vector2 direction = new Vector2(positionHistory[0].x - origin.x, positionHistory[0].y - origin.y);
		float magnitude = direction.magnitude;
		if (Helper.IsRayHittingNoTriggers(origin, direction, magnitude, 8448))
		{
			ResetMotion();
			rb2d.linearVelocity = Vector2.zero;
			if (cState.spellQuake)
			{
				spellControl.SendEvent("Hero Landed");
				transform.SetPosition2D(positionHistory[1]);
			}
			tilemapTestActive = false;
			tilemapTestCoroutine = StartCoroutine(TilemapTestPause());
		}
	}

	private IEnumerator TilemapTestPause()
	{
		yield return new WaitForSeconds(0.1f);
		tilemapTestActive = true;
	}

	private void StopTilemapTest()
	{
		if (tilemapTestCoroutine != null)
		{
			StopCoroutine(tilemapTestCoroutine);
			tilemapTestActive = false;
		}
	}

	[Obsolete]
	public bool TryDoTerrainThunk(AttackDirection attackDir, Collider2D thunkAgainst, Vector2 effectPoint, bool doHeroRecoil)
	{
		if (!thunkAgainst)
		{
			return false;
		}
		if (thunkAgainst.isTrigger)
		{
			return false;
		}
		bool doRecoil = false;
		TerrainThunkUtils.GetThunkProperties(thunkAgainst.gameObject, out var shouldThunk, ref doRecoil);
		if (shouldThunk)
		{
			nailTerrainImpactEffectPrefab.Spawn(effectPoint, Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)));
			if (doHeroRecoil)
			{
				switch (attackDir)
				{
				case AttackDirection.normal:
					if (cState.facingRight)
					{
						RecoilLeft();
					}
					else
					{
						RecoilRight();
					}
					break;
				case AttackDirection.upward:
					RecoilDown();
					break;
				}
			}
		}
		else if (doRecoil && doHeroRecoil)
		{
			switch (attackDir)
			{
			case AttackDirection.normal:
				if (cState.facingRight)
				{
					RecoilLeft();
				}
				else
				{
					RecoilRight();
				}
				break;
			case AttackDirection.upward:
				RecoilDown();
				break;
			}
		}
		return true;
	}

	private void ResetFixedUpdateCaches()
	{
		leftCache.Reset();
		rightCache.Reset();
	}

	public bool CheckStillTouchingWall(CollisionSide side, bool checkTop = false, bool checkNonSliders = true)
	{
		WallTouchCache.HitInfo top;
		WallTouchCache.HitInfo mid;
		WallTouchCache.HitInfo bottom;
		switch (side)
		{
		case CollisionSide.left:
			leftCache.Update(col2d, side, force: false);
			top = leftCache.top;
			mid = leftCache.mid;
			bottom = leftCache.bottom;
			break;
		case CollisionSide.right:
			rightCache.Update(col2d, side, force: false);
			top = rightCache.top;
			mid = rightCache.mid;
			bottom = rightCache.bottom;
			break;
		default:
			return false;
		}
		if (mid.HasCollider)
		{
			bool flag = !mid.IsSteepSlope;
			if (flag && checkNonSliders && mid.IsNonSlider)
			{
				flag = false;
			}
			if (flag)
			{
				return true;
			}
		}
		if (bottom.HasCollider)
		{
			bool flag2 = !bottom.IsSteepSlope;
			if (flag2 && checkNonSliders && bottom.IsNonSlider)
			{
				flag2 = false;
			}
			if (flag2)
			{
				return true;
			}
		}
		if (checkTop && top.HasCollider)
		{
			bool flag3 = !top.IsSteepSlope;
			if (flag3 && checkNonSliders && top.IsNonSlider)
			{
				flag3 = false;
			}
			if (flag3)
			{
				return true;
			}
		}
		return false;
	}

	private void CheckForBump(CollisionSide side)
	{
		CheckForBump(side, out var _, out var _, out var _);
	}

	public void CheckForBump(CollisionSide side, out bool hitBump, out bool hitWall, out bool hitHighWall)
	{
		bumpChecker.CheckForBump(side, out hitBump, out hitWall, out hitHighWall);
	}

	public bool CheckNearRoof()
	{
		Bounds bounds = col2d.bounds;
		Vector3 min = bounds.min;
		Vector3 max = bounds.max;
		Vector3 center = bounds.center;
		Vector3 size = bounds.size;
		Vector2 origin = max;
		Vector2 origin2 = new Vector2(min.x, max.y);
		Vector2 origin3 = new Vector2(center.x + size.x / 4f, max.y);
		Vector2 origin4 = new Vector2(center.x - size.x / 4f, max.y);
		Vector2 direction = new Vector2(-0.5f, 1f);
		Vector2 direction2 = new Vector2(0.5f, 1f);
		Vector2 up = Vector2.up;
		if (!Helper.IsRayHittingNoTriggers(origin2, direction, 2f, 8448) && !Helper.IsRayHittingNoTriggers(origin, direction2, 2f, 8448) && !Helper.IsRayHittingNoTriggers(origin3, up, 1f, 8448))
		{
			return Helper.IsRayHittingNoTriggers(origin4, up, 1f, 8448);
		}
		return true;
	}

	public bool CheckClamberLedge(out float y, out Collider2D clamberedCollider)
	{
		y = 0f;
		clamberedCollider = null;
		if (NoClamberRegion.IsClamberBlocked)
		{
			return false;
		}
		if (CheckNearRoof())
		{
			return false;
		}
		Vector2 vector = transform.position;
		Vector2 direction;
		Vector2 origin;
		Vector2 origin2;
		if (cState.facingRight)
		{
			direction = new Vector2(1f, 0f);
			origin = vector + new Vector2(0.77f, 0.67f);
			origin2 = vector + new Vector2(0.37f, 0.67f);
		}
		else
		{
			direction = new Vector2(-1f, 0f);
			origin = vector + new Vector2(-0.77f, 0.67f);
			origin2 = vector + new Vector2(-0.37f, 0.67f);
		}
		if (Helper.IsRayHittingNoTriggers(vector + new Vector2(0f, 0.67f), direction, 0.75f, 8448))
		{
			return false;
		}
		Vector2 origin3 = Vector2.zero;
		bool flag = false;
		Vector2 origin4 = Vector2.zero;
		bool flag2 = false;
		Vector2 direction2 = new Vector2(0f, -1f);
		if (Helper.IsRayHittingNoTriggers(origin, direction2, 2.26f, 8448, out var closestHit))
		{
			origin3 = closestHit.point;
			flag = true;
			clamberedCollider = closestHit.collider;
		}
		if (Helper.IsRayHittingNoTriggers(origin2, direction2, 2.26f, 8448, out var closestHit2))
		{
			origin4 = closestHit2.point;
			flag2 = true;
			clamberedCollider = closestHit2.collider;
		}
		origin3.y += 0.1f;
		origin4.y += 0.1f;
		Vector2 direction3 = new Vector2(0f, 1f);
		if (Helper.IsRayHittingNoTriggers(origin3, direction3, 2.16f, 8448))
		{
			return false;
		}
		if (Helper.IsRayHittingNoTriggers(origin4, direction3, 2.16f, 8448))
		{
			return false;
		}
		if (!flag || !flag2)
		{
			return false;
		}
		if (!origin3.y.IsWithinTolerance(0.1f, origin4.y))
		{
			return false;
		}
		if (Helper.IsRayHittingNoTriggers(new Vector2(vector.x, col2d.bounds.min.y + 0.2f), Vector2.down, 2f, 8448, out var closestHit3) && origin3.y - closestHit3.point.y < 1.5f)
		{
			return false;
		}
		y = origin3.y;
		return true;
	}

	public bool CheckTouchingGround()
	{
		return CheckTouchingGround(forced: false);
	}

	public bool CheckTouchingGround(bool forced)
	{
		checkTouchGround.Update(col2d, forced);
		return checkTouchGround.IsTouchingGround;
	}

	private List<CollisionSide> CheckTouching(PhysLayers layer)
	{
		List<CollisionSide> list = new List<CollisionSide>(4);
		Bounds bounds = col2d.bounds;
		Vector3 center = bounds.center;
		float distance = bounds.extents.x + 0.16f;
		float distance2 = bounds.extents.y + 0.16f;
		RaycastHit2D raycastHit2D = Helper.Raycast2D(center, Vector2.up, distance2, 1 << (int)layer);
		RaycastHit2D raycastHit2D2 = Helper.Raycast2D(center, Vector2.right, distance, 1 << (int)layer);
		RaycastHit2D raycastHit2D3 = Helper.Raycast2D(center, Vector2.down, distance2, 1 << (int)layer);
		RaycastHit2D raycastHit2D4 = Helper.Raycast2D(center, Vector2.left, distance, 1 << (int)layer);
		if (raycastHit2D.collider != null)
		{
			list.Add(CollisionSide.top);
		}
		if (raycastHit2D2.collider != null)
		{
			list.Add(CollisionSide.right);
		}
		if (raycastHit2D3.collider != null)
		{
			list.Add(CollisionSide.bottom);
		}
		if (raycastHit2D4.collider != null)
		{
			list.Add(CollisionSide.left);
		}
		return list;
	}

	private List<CollisionSide> CheckTouchingAdvanced(PhysLayers layer)
	{
		List<CollisionSide> list = new List<CollisionSide>();
		Bounds bounds = col2d.bounds;
		Vector2 origin = new Vector2(bounds.min.x, bounds.max.y);
		Vector2 origin2 = new Vector2(bounds.center.x, bounds.max.y);
		Vector2 origin3 = new Vector2(bounds.max.x, bounds.max.y);
		Vector2 origin4 = new Vector2(bounds.min.x, bounds.center.y);
		Vector2 origin5 = new Vector2(bounds.max.x, bounds.center.y);
		Vector2 origin6 = new Vector2(bounds.min.x, bounds.min.y);
		Vector2 origin7 = new Vector2(bounds.center.x, bounds.min.y);
		Vector2 origin8 = new Vector2(bounds.max.x, bounds.min.y);
		RaycastHit2D raycastHit2D = Helper.Raycast2D(origin, Vector2.up, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D2 = Helper.Raycast2D(origin2, Vector2.up, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D3 = Helper.Raycast2D(origin3, Vector2.up, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D4 = Helper.Raycast2D(origin3, Vector2.right, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D5 = Helper.Raycast2D(origin5, Vector2.right, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D6 = Helper.Raycast2D(origin8, Vector2.right, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D7 = Helper.Raycast2D(origin8, Vector2.down, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D8 = Helper.Raycast2D(origin7, Vector2.down, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D9 = Helper.Raycast2D(origin6, Vector2.down, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D10 = Helper.Raycast2D(origin6, Vector2.left, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D11 = Helper.Raycast2D(origin4, Vector2.left, 0.16f, 1 << (int)layer);
		RaycastHit2D raycastHit2D12 = Helper.Raycast2D(origin, Vector2.left, 0.16f, 1 << (int)layer);
		if (raycastHit2D.collider != null || raycastHit2D2.collider != null || raycastHit2D3.collider != null)
		{
			list.Add(CollisionSide.top);
		}
		if (raycastHit2D4.collider != null || raycastHit2D5.collider != null || raycastHit2D6.collider != null)
		{
			list.Add(CollisionSide.right);
		}
		if (raycastHit2D7.collider != null || raycastHit2D8.collider != null || raycastHit2D9.collider != null)
		{
			list.Add(CollisionSide.bottom);
		}
		if (raycastHit2D10.collider != null || raycastHit2D11.collider != null || raycastHit2D12.collider != null)
		{
			list.Add(CollisionSide.left);
		}
		return list;
	}

	private CollisionSide FindCollisionDirection(Collision2D collision)
	{
		Vector2 normal = collision.GetSafeContact().Normal;
		float x = normal.x;
		float y = normal.y;
		if (y >= 0.5f)
		{
			return CollisionSide.bottom;
		}
		if (y <= -0.5f)
		{
			return CollisionSide.top;
		}
		if (x < 0f)
		{
			return CollisionSide.right;
		}
		if (x > 0f)
		{
			return CollisionSide.left;
		}
		Debug.LogError("ERROR: unable to determine direction of collision - contact points at (" + normal.x + "," + normal.y + ")");
		return CollisionSide.bottom;
	}

	public bool CanJump()
	{
		if (IsInputBlocked())
		{
			return false;
		}
		if (hero_state != ActorStates.no_input && hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && !cState.wallSliding && !cState.dashing && !cState.isSprinting && !cState.backDashing && !cState.jumping && !cState.bouncing && !cState.shroomBouncing && !cState.downSpikeRecovery)
		{
			if (cState.onGround)
			{
				return true;
			}
			if (ledgeBufferSteps > 0 && !cState.dead && !cState.hazardDeath && !controlReqlinquished && headBumpSteps <= 0 && !CheckNearRoof())
			{
				ledgeBufferSteps = 0;
				return true;
			}
			return false;
		}
		return false;
	}

	public void AllowShuttleCock()
	{
		if (sprintBufferSteps < ledgeBufferSteps)
		{
			sprintBufferSteps = ledgeBufferSteps;
		}
		syncBufferSteps = true;
	}

	public bool CouldJumpCancel()
	{
		if (!CanDoubleJump(checkControlState: false))
		{
			return CanFloat(checkControlState: false);
		}
		return true;
	}

	public bool CanDoubleJump(bool checkControlState = true)
	{
		if (checkControlState && (hero_state == ActorStates.no_input || hero_state == ActorStates.hard_landing || hero_state == ActorStates.dash_landing || controlReqlinquished))
		{
			return false;
		}
		if (playerData.hasDoubleJump && !doubleJumped && !IsDashLocked() && !cState.wallSliding && !cState.backDashing && !IsAttackLocked() && !cState.bouncing && !cState.shroomBouncing && !cState.onGround && !cState.doubleJumping && Config.CanDoubleJump)
		{
			if (TryQueueWallJumpInterrupt())
			{
				return false;
			}
			if (IsApproachingSolidGround())
			{
				return false;
			}
			return true;
		}
		return false;
	}

	private bool CanInfiniteAirJump()
	{
		if (playerData.infiniteAirJump && hero_state != ActorStates.hard_landing && !cState.onGround)
		{
			return true;
		}
		return false;
	}

	private bool CanSwim()
	{
		if (hero_state != ActorStates.no_input && hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && !cState.attacking && !cState.dashing && !cState.jumping && !cState.bouncing && !cState.shroomBouncing && !cState.onGround)
		{
			return true;
		}
		return false;
	}

	public bool CanDash()
	{
		if (hero_state != ActorStates.no_input && hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && dashCooldownTimer <= 0f && !cState.dashing && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.preventDash && (cState.onGround || !airDashed || cState.wallSliding) && !cState.hazardDeath && playerData.hasDash)
		{
			return true;
		}
		return false;
	}

	public bool DashCooldownReady()
	{
		if (dashCooldownTimer <= 0f)
		{
			return true;
		}
		return false;
	}

	public bool HasHarpoonDash()
	{
		if (playerData.hasHarpoonDash)
		{
			return Config.CanHarpoonDash;
		}
		return false;
	}

	public bool CanHarpoonDash()
	{
		if (hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && harpoonDashCooldown <= 0f && (!cState.dashing || dash_timer <= 0f) && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && CanDoFSMCancelMove() && HasHarpoonDash() && !cState.dead && !cState.hazardDeath && !cState.hazardRespawning)
		{
			return true;
		}
		return false;
	}

	public bool CanSprint()
	{
		if (hero_state != ActorStates.no_input && hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && !cState.dashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.preventDash && !controlReqlinquished)
		{
			return true;
		}
		return false;
	}

	public bool CanSuperJump()
	{
		if (!gm.isPaused && hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && cState.onGround && !cState.dashing && !cState.hazardDeath && !cState.hazardRespawning && !cState.backDashing && (!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && CanDoFSMCancelMove() && !cState.recoilFrozen && !cState.recoiling && !cState.transitioning && playerData.hasSuperJump)
		{
			return true;
		}
		return false;
	}

	public bool CanAttack()
	{
		if (attack_cooldown > 0f)
		{
			return false;
		}
		return CanAttackAction();
	}

	public bool ThrowToolCooldownReady()
	{
		return throwToolCooldown <= 0f;
	}

	public bool CanThrowTool()
	{
		return CanThrowTool(checkGetWillThrow: true);
	}

	public bool CanThrowTool(bool checkGetWillThrow)
	{
		if (!ThrowToolCooldownReady())
		{
			return false;
		}
		if (!CanAttackAction())
		{
			return false;
		}
		if (!checkGetWillThrow)
		{
			return true;
		}
		return GetWillThrowTool(reportFailure: true);
	}

	private bool CanStartWithThrowTool()
	{
		if (startWithToolThrow && CanThrowTool())
		{
			return true;
		}
		startWithToolThrow = false;
		return false;
	}

	private bool CanAttackAction()
	{
		if (!cState.attacking && (!cState.dashing || dashingDown) && !cState.dead && !cState.hazardDeath && !cState.hazardRespawning && !controlReqlinquished && hero_state != ActorStates.no_input && hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && CanInput() && (!inputHandler.inputActions.Down.IsPressed || CanDownAttack()))
		{
			return true;
		}
		return false;
	}

	private bool CanDownAttack()
	{
		if (!allowAttackCancellingDownspikeRecovery)
		{
			if (!cState.downSpikeBouncing)
			{
				return !cState.downSpikeRecovery;
			}
			return false;
		}
		return true;
	}

	private bool TryQueueWallJumpInterrupt()
	{
		if (queuedWallJumpInterrupt)
		{
			return true;
		}
		if (playerData.hasWalljump && IsFacingNearWall(WALLJUMP_BROLLY_RAY_LENGTH, Color.green, out var wallCollider))
		{
			if (!CanChainWallJumps())
			{
				return false;
			}
			if (cState.touchingNonSlider)
			{
				return false;
			}
			queuedWallJumpInterrupt = true;
			touchingWallObj = (wallCollider ? wallCollider.gameObject : null);
			return true;
		}
		return false;
	}

	private bool IsAttackLocked()
	{
		if (cState.attacking)
		{
			return attack_time < Config.AttackRecoveryTime;
		}
		return false;
	}

	private bool IsDashLocked()
	{
		if (cState.dashing)
		{
			if (dashingDown)
			{
				return dash_time < AIR_DASH_TIME;
			}
			return true;
		}
		return false;
	}

	private bool CanFloat(bool checkControlState = true)
	{
		if (checkControlState && (hero_state == ActorStates.no_input || hero_state == ActorStates.hard_landing || hero_state == ActorStates.dash_landing || controlReqlinquished))
		{
			return false;
		}
		if (CanInfiniteAirJump())
		{
			return false;
		}
		if (playerData.hasBrolly && !cState.onGround && !IsDashLocked() && !cState.swimming && ledgeBufferSteps <= 0 && !cState.wallSliding && !IsAttackLocked() && !cState.hazardDeath && !cState.hazardRespawning && (!cState.doubleJumping || cState.inUpdraft) && CanDoFSMCancelMove() && Config.CanBrolly)
		{
			if (TryQueueWallJumpInterrupt())
			{
				return false;
			}
			if (IsApproachingSolidGround())
			{
				return false;
			}
			return true;
		}
		return false;
	}

	private bool IsApproachingSolidGround()
	{
		if (rb2d.linearVelocity.y > 0f)
		{
			return false;
		}
		Bounds bounds = col2d.bounds;
		Vector3 min = bounds.min;
		Vector3 max = bounds.max;
		Vector2 origin = new Vector2(min.x, min.y);
		Vector2 origin2 = new Vector2(max.x, min.y);
		float length = JUMP_ABILITY_GROUND_RAY_LENGTH + 0.1f;
		if (Helper.IsRayHittingNoTriggers(origin, Vector2.down, length, 8448))
		{
			return Helper.IsRayHittingNoTriggers(origin2, Vector2.down, length, 8448);
		}
		return false;
	}

	private bool CanNailCharge()
	{
		if (!cState.dead && !cState.attacking && (!controlReqlinquished || allowNailChargingWhileRelinquished) && !cState.recoiling && !cState.recoilingLeft && !cState.recoilingRight && !cState.hazardDeath && !cState.hazardRespawning && playerData.hasChargeSlash && Config.CanNailCharge && !IsInputBlocked() && InteractManager.BlockingInteractable == null)
		{
			return true;
		}
		return false;
	}

	private bool IsFacingNearWall(float rayLength, Color debugColor)
	{
		Collider2D wallCollider;
		return IsFacingNearWall(cState.facingRight, rayLength, debugColor, out wallCollider);
	}

	public bool IsFacingNearWall(bool facingRight, float rayLength, Color debugColor)
	{
		Collider2D wallCollider;
		return IsFacingNearWall(facingRight, rayLength, debugColor, out wallCollider);
	}

	public bool IsFacingNearWall(float rayLength, Color debugColor, out Collider2D wallCollider)
	{
		return IsFacingNearWall(cState.facingRight, rayLength, debugColor, out wallCollider);
	}

	private bool IsFacingNearWall(bool facingRight, float rayLength, Color debugColor, out Collider2D wallCollider)
	{
		wallCollider = null;
		Vector2 vector = transform.position;
		Vector2 vector2 = new Vector2(vector.x, vector.y);
		Vector2 direction = (facingRight ? Vector2.right : Vector2.left);
		if (!Helper.IsRayHittingNoTriggers(vector2, direction, rayLength, 8448, out var closestHit))
		{
			return false;
		}
		if (NonSlider.TryGetNonSlider(closestHit.collider, out var nonSlider) && nonSlider.IsActive)
		{
			return false;
		}
		float num = 0f;
		for (float num2 = 0.1f; num2 < 1.5f && Helper.IsRayHittingNoTriggers(vector2 + new Vector2(0f, num2), direction, rayLength, 8448); num2 += 0.1f)
		{
			num += 0.1f;
		}
		for (float num2 = 0.1f; num2 < 1.5f && Helper.IsRayHittingNoTriggers(vector2 - new Vector2(0f, num2), direction, rayLength, 8448); num2 += 0.1f)
		{
			num += 0.1f;
		}
		if (num < 1.5f)
		{
			return false;
		}
		wallCollider = closestHit.collider;
		return true;
	}

	public bool IsFacingNearSlideableWall()
	{
		if (!playerData.hasWalljump)
		{
			return false;
		}
		if (SlideSurface.IsHeroInside)
		{
			return false;
		}
		if (cState.wallSliding && gm.isPaused)
		{
			return true;
		}
		return IsFacingNearWall(WALLJUMP_RAY_LENGTH, Color.blue);
	}

	private bool CanStartWithWallSlide()
	{
		bool flag = !cState.touchingNonSlider && !cState.onGround && !cState.recoiling && !cState.transitioning && !cState.doubleJumping && (cState.falling || cState.wallSliding || controlReqlinquished || cState.shuttleCock) && (!cState.attacking || wallSlashing);
		if (flag && !IsFacingNearSlideableWall())
		{
			return false;
		}
		return flag;
	}

	private bool CanWallSlide()
	{
		if (CanInput())
		{
			return CanContinueWallSlide();
		}
		return false;
	}

	private bool CanContinueWallSlide()
	{
		if (!controlReqlinquished && (touchingWallL || touchingWallR))
		{
			return CanStartWithWallSlide();
		}
		return false;
	}

	public bool CanTakeDamage()
	{
		if (damageMode != DamageMode.NO_DAMAGE && transitionState == HeroTransitionState.WAITING_TO_TRANSITION && !cState.transitioning && !cState.Invulnerable && !cState.recoiling && !playerData.isInvincible && parryInvulnTimer <= 0f && CheatManager.Invincibility != CheatManager.InvincibilityStates.FullInvincible && !cState.dead && !cState.hazardDeath && !BossSceneController.IsTransitioning && !HeroInvincibilitySource.IsActive)
		{
			return true;
		}
		return false;
	}

	public bool CanTakeDamageIgnoreInvul()
	{
		if (damageMode != DamageMode.NO_DAMAGE && transitionState == HeroTransitionState.WAITING_TO_TRANSITION && !playerData.isInvincible && CheatManager.Invincibility != CheatManager.InvincibilityStates.FullInvincible && !cState.dead && !cState.hazardDeath && !BossSceneController.IsTransitioning && !HeroInvincibilitySource.IsActive)
		{
			return true;
		}
		return false;
	}

	public bool CanBeGrabbed(bool ignoreParryState)
	{
		if (!ignoreParryState && IsParrying())
		{
			return false;
		}
		if (CanTakeDamage() && cState.downspikeInvulnerabilitySteps <= 0)
		{
			return true;
		}
		return false;
	}

	public bool CanBeGrabbed()
	{
		return CanBeGrabbed(ignoreParryState: false);
	}

	public bool CanBeBarnacleGrabbed()
	{
		if (damageMode != DamageMode.NO_DAMAGE && transitionState == HeroTransitionState.WAITING_TO_TRANSITION && !cState.recoiling && !cState.dead && !cState.hazardDeath && !BossSceneController.IsTransitioning && !WillDoBellBindHit(sendEvent: true))
		{
			return true;
		}
		return false;
	}

	private bool CanWallJump(bool mustBeNearWall = true)
	{
		if (SlideSurface.IsHeroInside)
		{
			return false;
		}
		if (!playerData.hasWalljump)
		{
			return false;
		}
		if (cState.touchingNonSlider)
		{
			return false;
		}
		if (cState.wallSliding || !mustBeNearWall)
		{
			return true;
		}
		if (cState.touchingWall && !cState.onGround)
		{
			return CanChainWallJumps();
		}
		return false;
	}

	private bool CanChainWallJumps()
	{
		if (dashingDown)
		{
			return false;
		}
		if (!wallLocked && wallJumpChainStepsLeft <= 0)
		{
			return Math.Abs(move_input) > Mathf.Epsilon;
		}
		return true;
	}

	private bool CanWallScramble()
	{
		if (!cState.wallSliding)
		{
			return false;
		}
		if (!playerData.hasWalljump)
		{
			return false;
		}
		if (hero_state == ActorStates.no_input || hero_state == ActorStates.hard_landing || hero_state == ActorStates.dash_landing || controlReqlinquished)
		{
			return false;
		}
		if (touchingWallL)
		{
			if (!inputHandler.inputActions.Left.IsPressed)
			{
				return false;
			}
		}
		else
		{
			if (!touchingWallR)
			{
				return false;
			}
			if (!inputHandler.inputActions.Right.IsPressed)
			{
				return false;
			}
		}
		if ((!cState.attacking || !(attack_time < Config.AttackRecoveryTime)) && !cState.hazardDeath)
		{
			return !cState.hazardRespawning;
		}
		return false;
	}

	public bool ShouldHardLand(GameObject obj)
	{
		if (cState.hazardDeath)
		{
			return false;
		}
		if ((bool)obj.GetComponent<NoHardLanding>())
		{
			return false;
		}
		if (cState.willHardLand && hero_state != ActorStates.hard_landing)
		{
			if ((bool)GameManager.instance)
			{
				return GameManager.instance.GameState == GameState.PLAYING;
			}
			return true;
		}
		return false;
	}

	private bool IsOnGroundLayer(GameObject obj)
	{
		return ((1 << obj.layer) & 0x2100) != 0;
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		bool flag = false;
		GameObject gameObject = collision.gameObject;
		if (IsOnGroundLayer(gameObject) || gameObject.CompareTag("HeroWalkable"))
		{
			flag = CheckTouchingGround();
			if (flag)
			{
				proxyFSM.SendEvent("HeroCtrl-Landed");
				umbrellaFSM.SendEvent("LAND");
			}
		}
		HandleCollisionTouching(collision);
		if (hero_state != ActorStates.no_input)
		{
			CollisionSide collisionSide = FindCollisionDirection(collision);
			if (!IsOnGroundLayer(gameObject) && !gameObject.CompareTag("HeroWalkable"))
			{
				return;
			}
			fallTrailGenerated = false;
			if (collisionSide == CollisionSide.top)
			{
				headBumpSteps = HEAD_BUMP_STEPS;
				if (cState.jumping)
				{
					CancelJump();
					CancelDoubleJump();
				}
				if (cState.bouncing)
				{
					CancelBounce();
					rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, 0f);
				}
				if (cState.shroomBouncing)
				{
					CancelBounce();
					rb2d.linearVelocity = new Vector2(rb2d.linearVelocity.x, 0f);
				}
			}
			if (collisionSide == CollisionSide.bottom)
			{
				if (cState.attacking)
				{
					CancelDownAttack();
				}
				if (ShouldHardLand(gameObject))
				{
					DoHardLanding();
				}
				else if (cState.dashing && dashingDown)
				{
					AffectedByGravity(gravityApplies: true);
					SetState(ActorStates.dash_landing);
					hardLanded = true;
				}
				else if (!SteepSlope.IsSteepSlope(gameObject) && hero_state != ActorStates.hard_landing && !cState.onGround)
				{
					BackOnGround();
				}
			}
		}
		else
		{
			if (hero_state != ActorStates.no_input)
			{
				return;
			}
			if (flag)
			{
				sprintFSM.SendEvent("HERO TOUCHED GROUND");
			}
			if (transitionState == HeroTransitionState.DROPPING_DOWN)
			{
				if (gatePosition == GatePosition.bottom || gatePosition == GatePosition.top)
				{
					if (gatePosition == GatePosition.bottom)
					{
						attack_cooldown = 0.1f;
					}
					FinishedEnteringScene();
				}
			}
			else if (flag && cState.isSprinting && !cState.willHardLand && !cState.onGround)
			{
				BackOnGround();
			}
		}
	}

	private void HandleCollisionTouching(Collision2D collision)
	{
		if (cState.downSpiking && FindCollisionDirection(collision) == CollisionSide.bottom)
		{
			FinishDownspike();
			Vector3 vector = (downspikeEffectPrefabSpawnPoint ? downspikeEffectPrefabSpawnPoint.position : transform.position);
			nailTerrainImpactEffectPrefabDownSpike.Spawn(vector, Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)));
			NailSlashTerrainThunk.ReportDownspikeHitGround(vector);
		}
		if (cState.shuttleCock && IsFacingNearWall(WALLJUMP_RAY_LENGTH, Color.blue))
		{
			TryFsmCancelToWallSlide();
		}
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		GameObject gameObject = collision.gameObject;
		if (gameObject.CompareTag("Geo"))
		{
			return;
		}
		HandleCollisionTouching(collision);
		if (hero_state != ActorStates.no_input)
		{
			if (!IsOnGroundLayer(gameObject))
			{
				return;
			}
			bool flag = false;
			if (!NonSlider.IsNonSlider(gameObject))
			{
				cState.touchingNonSlider = false;
				if (CheckStillTouchingWall(CollisionSide.left))
				{
					cState.touchingWall = true;
					touchingWallL = true;
					touchingWallR = false;
					touchingWallObj = gameObject;
				}
				else if (CheckStillTouchingWall(CollisionSide.right))
				{
					cState.touchingWall = true;
					touchingWallL = false;
					touchingWallR = true;
					touchingWallObj = gameObject;
				}
				else
				{
					cState.touchingWall = false;
					touchingWallL = false;
					touchingWallR = false;
					touchingWallObj = null;
				}
				if (CheckTouchingGround())
				{
					flag = true;
					if (ShouldHardLand(gameObject))
					{
						DoHardLanding();
					}
					else if (hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && cState.falling)
					{
						BackOnGround();
					}
				}
				else if (cState.jumping || cState.falling)
				{
					LeftGround(setState: true);
				}
			}
			else
			{
				cState.touchingNonSlider = true;
				if (FindCollisionDirection(collision) == CollisionSide.bottom && CheckTouchingGround() && !SteepSlope.IsSteepSlope(collision.gameObject))
				{
					flag = true;
					if (ShouldHardLand(gameObject))
					{
						DoHardLanding();
					}
					else if (hero_state != ActorStates.hard_landing && hero_state != ActorStates.dash_landing && cState.falling)
					{
						BackOnGround();
					}
				}
			}
			if (!flag && collision.contactCount > 0 && collision.GetContact(0).normal.y >= 1f && (!dashingDown || rb2d.linearVelocity.y == 0f))
			{
				tryShove = true;
				onFlatGround = true;
			}
			return;
		}
		FsmBool fsmBool = isUmbrellaActive;
		if (fsmBool != null && fsmBool.Value && gameObject.layer == 8)
		{
			Collision2DUtils.Collision2DSafeContact safeContact = collision.GetSafeContact();
			if (!safeContact.IsLegitimate || safeContact.Normal != Vector2.up)
			{
				return;
			}
			ShoveOff();
		}
		if (cState.onGround || !cState.isSprinting)
		{
			return;
		}
		bool flag2 = false;
		if (IsOnGroundLayer(gameObject) || gameObject.CompareTag("HeroWalkable"))
		{
			flag2 = CheckTouchingGround();
			if (flag2)
			{
				proxyFSM.SendEvent("HeroCtrl-Landed");
				umbrellaFSM.SendEvent("LAND");
			}
		}
		if (flag2 && !cState.willHardLand && !cState.onGround)
		{
			BackOnGround();
		}
	}

	private void OnCollisionExit2D(Collision2D collision)
	{
		if (cState.recoilingLeft || cState.recoilingRight)
		{
			cState.touchingWall = false;
			touchingWallL = false;
			touchingWallR = false;
			touchingWallObj = null;
			cState.touchingNonSlider = false;
		}
		if (cState.touchingWall && collision.gameObject == touchingWallObj)
		{
			cState.touchingWall = false;
			touchingWallL = false;
			touchingWallR = false;
			touchingWallObj = null;
		}
		if (hero_state != ActorStates.no_input && !cState.recoiling && IsOnGroundLayer(collision.gameObject) && !CheckTouchingGround())
		{
			LeftGround(setState: true);
		}
	}

	private void LeftGround(bool setState)
	{
		if (cState.onGround)
		{
			if (!cState.jumping && !fallTrailGenerated)
			{
				if (playerData.environmentType != EnvironmentTypes.Moss && playerData.environmentType != EnvironmentTypes.WetMetal && !playerData.atBench)
				{
					fsm_fallTrail.SendEvent("PLAY");
				}
				fallTrailGenerated = true;
			}
			SetCanSoftLand();
			cState.onGround = false;
			proxyFSM.SendEvent("HeroCtrl-LeftGround");
			if (cState.wasOnGround)
			{
				ledgeBufferSteps = LEDGE_BUFFER_STEPS;
				if (syncBufferSteps || cState.isSprinting || cState.dashing)
				{
					sprintBufferSteps = ledgeBufferSteps;
				}
			}
		}
		if (setState)
		{
			SetState(ActorStates.airborne);
		}
	}

	private void SetupGameRefs()
	{
		if (cState == null)
		{
			cState = new HeroControllerStates();
		}
		gm = GameManager.instance;
		animCtrl = GetComponent<HeroAnimationController>();
		rb2d = GetComponent<Rigidbody2D>();
		col2d = GetComponent<Collider2D>();
		transform = GetComponent<Transform>();
		renderer = GetComponent<MeshRenderer>();
		audioCtrl = GetComponent<HeroAudioController>();
		inputHandler = gm.GetComponent<InputHandler>();
		proxyFSM = FSMUtility.LocateFSM(base.gameObject, "ProxyFSM");
		audioSource = GetComponent<AudioSource>();
		enviroRegionListener = GetComponent<EnviroRegionListener>();
		NailImbuement = GetComponent<HeroNailImbuement>();
		vibrationCtrl = GetComponent<HeroVibrationController>();
		invPulse = GetComponent<InvulnerablePulse>();
		spriteFlash = GetComponent<SpriteFlash>();
		if ((bool)runningWaterEffect)
		{
			areaEffectTint = runningWaterEffect.GetComponent<AreaEffectTint>();
		}
		gm.UnloadingLevel += OnLevelUnload;
		prevGravityScale = DEFAULT_GRAVITY;
		transition_vel = Vector2.zero;
		current_velocity = Vector2.zero;
		acceptingInput = true;
		positionHistory = new Vector2[2];
	}

	private void SetupPools()
	{
	}

	private void FilterInput()
	{
		if (move_input > 0.3f)
		{
			move_input = 1f;
		}
		else if (move_input < -0.3f)
		{
			move_input = -1f;
		}
		else
		{
			move_input = 0f;
		}
		if (vertical_input > 0.5f)
		{
			vertical_input = 1f;
		}
		else if (vertical_input < -0.5f)
		{
			vertical_input = -1f;
		}
		else
		{
			vertical_input = 0f;
		}
	}

	public Vector3 FindGroundPoint(Vector2 startPoint, bool useExtended = false)
	{
		Vector2 groundPos;
		return TryFindGroundPoint(out groundPos, startPoint, useExtended) ? groundPos : startPoint;
	}

	private float FindGroundPointY(float x, float y, bool useExtended = false)
	{
		float length = (useExtended ? FIND_GROUND_POINT_DISTANCE_EXT : FIND_GROUND_POINT_DISTANCE);
		Helper.IsRayHittingNoTriggers(new Vector2(x, y), Vector2.down, length, 8448, out var closestHit);
		return closestHit.point.y + col2d.bounds.extents.y - col2d.offset.y + Physics2D.defaultContactOffset;
	}

	private bool TryFindGroundPoint(out Vector2 groundPos, Vector2 startPos, bool useExtended)
	{
		float num = (useExtended ? FIND_GROUND_POINT_DISTANCE_EXT : FIND_GROUND_POINT_DISTANCE);
		if (!Helper.IsRayHittingNoTriggers(startPos, Vector2.down, num, 8448, out var closestHit))
		{
			Debug.LogErrorFormat("FindGroundPoint: Could not find ground point below {0}, check reference position is not too high (more than {1} tiles).", startPos.ToString(), num);
			groundPos = startPos;
			return false;
		}
		groundPos = new Vector2(closestHit.point.x, closestHit.point.y + col2d.bounds.extents.y - col2d.offset.y + 0.01f);
		return true;
	}

	public void StartDownspikeInvulnerability()
	{
		cState.downspikeInvulnerabilitySteps = DOWNSPIKE_INVULNERABILITY_STEPS;
	}

	public void StartDownspikeInvulnerabilityLong()
	{
		cState.downspikeInvulnerabilitySteps = DOWNSPIKE_INVULNERABILITY_STEPS_LONG;
	}

	public void CancelDownspikeInvulnerability()
	{
		cState.downspikeInvulnerabilitySteps = 0;
	}

	public void DetachHeroLight()
	{
		if ((bool)heroLight)
		{
			heroLight.Detach();
		}
	}

	public void ReattachHeroLight()
	{
		if ((bool)heroLight)
		{
			heroLight.Reattach();
		}
	}

	public void SetAllowNailChargingWhileRelinquished(bool value)
	{
		allowNailChargingWhileRelinquished = value;
	}

	public void SetAllowRecoilWhileRelinquished(bool value)
	{
		allowRecoilWhileRelinquished = value;
	}

	public void SetRecoilZeroVelocity(bool value)
	{
		recoilZeroVelocity = value;
	}

	public IEnumerator MoveToPositionX(float targetX, Action onEnd)
	{
		float dir = Mathf.Sign(targetX - transform.position.x);
		Func<bool> shouldMove = delegate
		{
			if (dir < 0f)
			{
				if (transform.position.x <= targetX)
				{
					return false;
				}
			}
			else if (transform.position.x >= targetX)
			{
				return false;
			}
			return true;
		};
		bool hadControl = !controlReqlinquished;
		if (hadControl)
		{
			RelinquishControl();
		}
		StopAnimationControl();
		tk2dSpriteAnimator component = GetComponent<tk2dSpriteAnimator>();
		if (shouldMove())
		{
			float velX = GetWalkSpeed() * dir;
			Vector2 linearVelocity = rb2d.linearVelocity;
			linearVelocity.x = velX;
			rb2d.linearVelocity = linearVelocity;
			if ((cState.facingRight && dir < 0f) || (!cState.facingRight && dir > 0f))
			{
				if (dir > 0f)
				{
					FaceRight();
				}
				else
				{
					FaceLeft();
				}
				yield return StartCoroutine(component.PlayAnimWait("Turn"));
			}
			animCtrl.PlayClipForced("Walk");
			ForceWalkingSound = true;
			float stationaryElapsed = 0f;
			float lastXPos = transform.position.x;
			while (stationaryElapsed < 3f && shouldMove())
			{
				yield return null;
				CheckForBump((!(velX > 0f)) ? CollisionSide.left : CollisionSide.right);
				linearVelocity = rb2d.linearVelocity;
				linearVelocity.x = velX;
				rb2d.linearVelocity = linearVelocity;
				float x = transform.position.x;
				stationaryElapsed = ((!(Mathf.Abs(x - lastXPos) < 0.01f)) ? 0f : (stationaryElapsed + Time.deltaTime));
				lastXPos = x;
			}
			linearVelocity = rb2d.linearVelocity;
			linearVelocity.x = 0f;
			rb2d.linearVelocity = linearVelocity;
			transform.SetPositionX(targetX);
			animCtrl.PlayClipForced("Idle");
			ForceWalkingSound = false;
			yield return null;
			StartAnimationControl();
			if (hadControl)
			{
				RegainControl();
			}
		}
		onEnd?.Invoke();
	}

	public void ToggleNoClip()
	{
		if (GetIsNoClip())
		{
			col2d.enabled = true;
			playerData.isInvincible = false;
			playerData.infiniteAirJump = false;
		}
		else
		{
			col2d.enabled = false;
			playerData.isInvincible = true;
			playerData.infiniteAirJump = true;
		}
	}

	public bool GetIsNoClip()
	{
		return !col2d.enabled;
	}

	public float GetRunSpeed()
	{
		if (IsUsingQuickening)
		{
			return QUICKENING_RUN_SPEED;
		}
		return RUN_SPEED;
	}

	public float GetWalkSpeed()
	{
		if (IsUsingQuickening)
		{
			return QUICKENING_WALK_SPEED;
		}
		return WALK_SPEED;
	}

	public void ActivateQuickening()
	{
		quickeningTimeLeft = QUICKENING_DURATION;
		if ((bool)spawnedQuickeningEffect)
		{
			spawnedQuickeningEffect.Recycle();
			spawnedQuickeningEffect = null;
		}
		bool isEquipped = Gameplay.PoisonPouchTool.IsEquipped;
		PlayParticleEffects playParticleEffects = (isEquipped ? quickeningPoisonEffectPrefab : quickeningEffectPrefab);
		if ((bool)playParticleEffects)
		{
			PlayParticleEffects playParticleEffects2 = playParticleEffects.Spawn();
			playParticleEffects2.transform.SetParent(transform, worldPositionStays: true);
			playParticleEffects2.transform.SetLocalPosition2D(Vector2.zero);
			spawnedQuickeningEffect = playParticleEffects2;
			playParticleEffects2.PlayParticleSystems();
		}
		sprintFSMIsQuickening.Value = true;
		toolsFSMIsQuickening.Value = true;
		if (!spriteFlash.IsFlashing(repeating: true, quickeningFlash))
		{
			quickeningFlash = spriteFlash.Flash(isEquipped ? Gameplay.PoisonPouchHeroTintColour : new Color(1f, 0.85f, 0.47f, 1f), 0.7f, 0.2f, 0.01f, 0.22f, 0f, repeating: true, 0, 1, requireExplicitCancel: true);
		}
	}

	public void StartRoarLock()
	{
		FSMUtility.SendEventToGameObject(base.gameObject, "ROAR ENTER");
	}

	public void StartRoarLockNoRecoil()
	{
		PlayMakerFSM playMakerFSM = PlayMakerFSM.FindFsmOnGameObject(base.gameObject, "Roar and Wound States");
		if ((bool)playMakerFSM)
		{
			playMakerFSM.FsmVariables.FindFsmFloat("Push Strength").Value = 0f;
		}
		FSMUtility.SendEventToGameObject(base.gameObject, "ROAR ENTER");
	}

	public void StopRoarLock()
	{
		FSMUtility.SendEventToGameObject(base.gameObject, "ROAR EXIT");
	}

	public void CocoonBroken()
	{
		CocoonBroken(doAirPause: false, forceCanBind: false);
	}

	public void CocoonBroken(bool doAirPause)
	{
		CocoonBroken(doAirPause, forceCanBind: false);
	}

	public void CocoonBroken(bool doAirPause, bool forceCanBind)
	{
		int heroCorpseMoneyPool = playerData.HeroCorpseMoneyPool;
		playerData.HeroCorpseScene = string.Empty;
		playerData.HeroCorpseMoneyPool = 0;
		playerData.HeroCorpseMarkerGuid = null;
		if (heroCorpseMoneyPool > 0)
		{
			CurrencyManager.AddCurrency(heroCorpseMoneyPool, CurrencyType.Money);
		}
		if (playerData.silkMax > 9)
		{
			playerData.IsSilkSpoolBroken = false;
			EventRegister.SendEvent(EventRegisterEvents.SpoolUnbroken);
		}
		AddSilk(9, heroEffect: true, SilkSpool.SilkAddSource.Normal, forceCanBind);
		if (doAirPause && hero_state == ActorStates.airborne && !cState.swimming)
		{
			if (cocoonFloatRoutine != null)
			{
				StopCoroutine(cocoonFloatRoutine);
			}
			cocoonFloatRoutine = StartCoroutine(CocoonFloatRoutine());
		}
	}

	private IEnumerator CocoonFloatRoutine()
	{
		bool didRelinquish = !playerData.atBench;
		if (didRelinquish)
		{
			RelinquishControl();
			AffectedByGravity(gravityApplies: false);
			SetState(ActorStates.no_input);
		}
		int controlVersion = ControlVersion;
		yield return new WaitForSeconds(0.25f);
		if (didRelinquish && controlVersion == ControlVersion)
		{
			RegainControl();
		}
	}

	public void RecordLeaveSceneCState()
	{
		if (cState.superDashing)
		{
			exitedSuperDashing = true;
		}
		if (cState.spellQuake)
		{
			exitedQuake = true;
		}
		if (cState.dashing || cState.isSprinting || sprintFSM.GetFsmBoolIfExists("Is Sprinting"))
		{
			exitedSprinting = true;
		}
	}

	public void CleanSpawnedDeliveryEffects()
	{
		if (DeliveryQuestItem.GetActiveItems().Count() != 0)
		{
			return;
		}
		foreach (GameObject value in spawnedDeliveryEffects.Values)
		{
			if (value != null)
			{
				UnityEngine.Object.Destroy(value);
			}
		}
		spawnedDeliveryEffects.Clear();
	}

	public void LeavingScene()
	{
		PersistentAudioManager.OnLeaveScene();
		ResetFixedUpdateCaches();
		cState.ClearInvulnerabilitySources();
		CleanSpawnedDeliveryEffects();
		RecordLeaveSceneCState();
		cState.downSpikeAntic = false;
		if (HeroPerformanceRegion.IsPerforming)
		{
			EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
		}
		GameCameras.instance.forceCameraAspect.SetFovOffset(0f, 0f, null);
		this.HeroLeavingScene?.Invoke();
		proxyFSM.SendEvent("HeroCtrl-LeavingScene");
		inputBlockers.Clear();
		HeroInvincibilitySource.Clear();
		foreach (DamageEnemies damageEnemies in damageEnemiesList)
		{
			damageEnemies.ClearLists();
		}
		IsStunned = false;
	}

	public void DoSprintSkid()
	{
		sprintFSM.SendEventSafe("DO SPRINT SKID");
	}

	public void AddExtraAirMoveVelocity(DecayingVelocity velocity)
	{
		extraAirMoveVelocities.Add(velocity);
	}

	public void ClearEffectsInstant()
	{
		EventRegister.SendEvent(EventRegisterEvents.ClearEffectsInstant);
		ClearEffects();
	}

	public void ClearEffects()
	{
		ResetLifebloodState();
		ClearEffectsLite();
	}

	public void ClearEffectsLite()
	{
		StopQuickening();
		SetIsMaggoted(value: false);
		ResetAllCrestState(clearHunterState: false);
		ResetMaggotCharm();
		NailImbuement.SetElement(NailElements.None);
		EventRegister.SendEvent(EventRegisterEvents.ClearEffects);
	}

	private void ResetMaggotCharm()
	{
		playerData.MaggotCharmHits = 0;
		maggotCharmTimer = 0f;
		EventRegister.SendEvent(EventRegisterEvents.MaggotCheck);
	}

	private void StopQuickening()
	{
		if ((bool)spawnedQuickeningEffect)
		{
			spawnedQuickeningEffect.StopParticleSystems();
			spawnedQuickeningEffect = null;
		}
		spriteFlash.CancelRepeatingFlash(quickeningFlash);
		sprintFSMIsQuickening.Value = false;
		toolsFSMIsQuickening.Value = false;
		quickeningTimeLeft = 0f;
	}

	public void SilkTaunted()
	{
		silkTauntEffectTimeLeft = 6f;
	}

	public bool SilkTauntEffectConsume()
	{
		bool result = silkTauntEffectTimeLeft > 0f;
		silkTauntEffectTimeLeft = 0f;
		return result;
	}

	public void RingTaunted()
	{
		ringTauntEffectTimeLeft = 6f;
	}

	public bool RingTauntEffectConsume()
	{
		bool result = ringTauntEffectTimeLeft > 0f;
		ringTauntEffectTimeLeft = 0f;
		return result;
	}

	public void ResetTauntEffects()
	{
		silkTauntEffectTimeLeft = 0f;
		ringTauntEffectTimeLeft = 0f;
	}

	private void ResetLavaBell()
	{
		lavaBellCooldownTimeLeft = 0f;
		spawnedLavaBellRechargeEffect.SetActive(value: false);
		EventRegister.SendEvent(EventRegisterEvents.LavaBellReset);
	}

	public HeroVibrationController GetVibrationCtrl()
	{
		return vibrationCtrl;
	}

	public void ReportPoisonHealthAdded()
	{
		PoisonHealthCount++;
		if (!spriteFlash.IsFlashing(repeating: true, poisonHealthFlash))
		{
			poisonHealthFlash = spriteFlash.Flash(Gameplay.PoisonPouchHeroTintColour, 0.7f, 0.2f, 0.01f, 0.22f, 0f, repeating: true, 0, 1, requireExplicitCancel: true);
		}
	}

	public void ReportPoisonHealthRemoved()
	{
		PoisonHealthCount--;
		if (PoisonHealthCount < 0)
		{
			PoisonHealthCount = 0;
		}
		if (PoisonHealthCount == 0)
		{
			spriteFlash.CancelRepeatingFlash(poisonHealthFlash);
		}
	}
}
