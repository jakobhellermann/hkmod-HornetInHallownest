using System;
using GlobalEnums;
using GlobalSettings;
using UnityEngine;

namespace Silksong;

public class HeroAnimationController : MonoBehaviour, IHeroAnimationController
{
	private class ProbabilityString : Probability.ProbabilityBase<string>
	{
		private string item;

		public override string Item => item;

		public ProbabilityString(string item, float probability)
		{
			this.item = item;
			Probability = probability;
		}
	}

	public tk2dSpriteAnimator animator;

	private MeshRenderer meshRenderer;

	private HeroController heroCtrl;

	private HeroControllerStates cState;

	private PlayerData pd;

	private HeroAudioController audioCtrl;

	[SerializeField]
	private tk2dSpriteAnimation windyAnimLib;

	[HideInInspector]
	public bool playLanding;

	private bool _playRunToIdle;

	private bool playRunToIdle;

	private bool playDashToIdle;

	private bool playBackDashToIdleEnd;

	private bool playedDownSpikeBounce;

	private bool justWallJumped;

	private bool wallJumpedFromScramble;

	private bool isPlayingSlashLand;

	private bool playSlashLand;

	private bool playSlashEnd;

	private int previousAttackCount;

	private bool canceledSlash;

	private bool playSilkChargeEnd;

	public bool skipIdleToRun;

	public bool idleToRunShort;

	public bool startWithFallAnim;

	[Space]
	[SerializeField]
	private AudioEvent wakeUpGround1;

	[SerializeField]
	private AudioEvent wakeUpGround2;

	[SerializeField]
	private AudioEvent wakeUpGroundCloakless;

	[SerializeField]
	private AudioEvent backflipSpin;

	private AudioSource backflipSpawnedAudio;

	private Action clearBackflipSpawnedAudio;

	private bool playSprintToRun;

	private bool didSkid;

	private bool wasFacingRight;

	[HideInInspector]
	public bool setEntryAnim;

	private int previousToolThrowCount;

	private int animEventsTriggered;

	private bool attackComplete;

	private readonly ProbabilityString[] downspikeAnims = new ProbabilityString[3]
	{
		new ProbabilityString("DownSpikeBounce 1", 1f),
		new ProbabilityString("DownSpikeBounce 2", 1f),
		new ProbabilityString("Recoil Twirl", 1f)
	};

	private float[] downspikeAnimProbabilities;

	private bool wasPlayingAirRecovery;

	private bool wasJumping;

	private bool wantsToJump;

	private bool wasWallSliding;

	private bool wasWallClinging;

	private bool wasInWalkZone;

	private bool didJustLand;

	private bool playingDoubleJump;

	private bool canForceDoubleJump;

	private bool playingDownDashEnd;

	private bool wasFacingRightWhenStopped;

	private string selectedMantleCancelJumpAnim;

	private bool playMantleCancel;

	private bool checkMantleCancel;

	private bool playBackflip;

	private bool playSuperJumpFall;

	private bool playDashUpperRecovery;

	private float nextIdleLookTime;

	private bool playingIdleRest;

	private HeroControllerConfig config;

	private bool isCursed;

	public bool waitingToEnter;

	public const string HURT_IDLE_ANIM = "Idle Hurt";

	public ActorStates actorState { get; private set; }

	public ActorStates prevActorState { get; private set; }

	public ActorStates stateBeforeControl { get; private set; }

	public bool controlEnabled { get; private set; }

	public bool IsPlayingUpdraftAnim { get; private set; }

	public bool IsPlayingWindyAnim { get; private set; }

	public bool IsPlayingHurtAnim { get; private set; }

	private void Awake()
	{
		animator = GetComponent<tk2dSpriteAnimator>();
		meshRenderer = GetComponent<MeshRenderer>();
		heroCtrl = GetComponent<HeroController>();
		audioCtrl = GetComponent<HeroAudioController>();
		cState = heroCtrl.cState;
		clearBackflipSpawnedAudio = delegate
		{
			backflipSpawnedAudio = null;
		};
	}

	private void Start()
	{
		pd = PlayerData.instance;
		ResetAll();
		actorState = heroCtrl.hero_state;
		if (controlEnabled)
		{
			if (heroCtrl.hero_state == ActorStates.airborne)
			{
				PlayFromFrame("Airborne", 7);
			}
			else
			{
				PlayIdle();
			}
		}
		else
		{
			animator.Stop();
		}
		if (windyAnimLib != null)
		{
			tk2dSpriteAnimationClip[] clips = windyAnimLib.clips;
			foreach (tk2dSpriteAnimationClip tk2dSpriteAnimationClip2 in clips)
			{
				if (tk2dSpriteAnimationClip2 != null && !tk2dSpriteAnimationClip2.Empty && !(tk2dSpriteAnimationClip2.frames[0].spriteCollection == null))
				{
					_ = tk2dSpriteAnimationClip2.frames[0].spriteCollection.inst;
				}
			}
		}
		EventRegister.GetRegisterGuaranteed(base.gameObject, "TOOL EQUIPS CHANGED").ReceivedEvent += UpdateToolEquipFlags;
		UpdateToolEquipFlags();
	}

	private void UpdateToolEquipFlags()
	{
		isCursed = Gameplay.CursedCrest.IsEquipped;
	}

	private void Update()
	{
		if (controlEnabled && !waitingToEnter)
		{
			UpdateAnimation();
		}
		else if (cState.facingRight)
		{
			wasFacingRight = true;
		}
		else
		{
			wasFacingRight = false;
		}
		if (pd.betaEnd)
		{
			PlayRun();
		}
	}

	public void SetHeroControllerConfig(HeroControllerConfig config)
	{
		this.config = config;
	}

	public void ResetAll()
	{
		playRunToIdle = false;
		playDashToIdle = false;
		playLanding = false;
		playSlashLand = false;
		playSilkChargeEnd = false;
		controlEnabled = true;
		isPlayingSlashLand = false;
		wasFacingRight = cState.facingRight;
		wasPlayingAirRecovery = false;
		ResetIdleLook();
	}

	public void ResetDownspikeBounce()
	{
		playedDownSpikeBounce = false;
	}

	public void ResetIdleLook()
	{
		playingIdleRest = false;
		nextIdleLookTime = UnityEngine.Random.Range(10f, 15f);
	}

	public void ResetPlays()
	{
		playLanding = false;
		playRunToIdle = false;
		playDashToIdle = false;
	}

	public void UpdateState(ActorStates newState)
	{
		if (!controlEnabled || newState == actorState)
		{
			return;
		}
		if (actorState == ActorStates.airborne && newState == ActorStates.idle && !playLanding)
		{
			if (cState.attacking)
			{
				playSlashLand = true;
				canceledSlash = true;
			}
			else
			{
				playLanding = true;
			}
			playMantleCancel = false;
			playBackflip = false;
			playSuperJumpFall = false;
			playDashUpperRecovery = false;
			cState.mantleRecovery = false;
			cState.downSpikeRecovery = false;
		}
		if (actorState == ActorStates.airborne && newState == ActorStates.running)
		{
			didJustLand = true;
			if (cState.attacking)
			{
				skipIdleToRun = true;
				playSlashLand = true;
				canceledSlash = true;
			}
			else
			{
				skipIdleToRun = false;
				idleToRunShort = false;
			}
		}
		ActorStates actorStates = actorState;
		if ((actorStates == ActorStates.idle || actorStates == ActorStates.running) && newState == ActorStates.airborne)
		{
			playSlashLand = false;
			playSlashEnd = false;
			canceledSlash = true;
			playSprintToRun = false;
			playDashToIdle = false;
			playRunToIdle = false;
			playLanding = false;
		}
		if (actorState == ActorStates.idle && newState != ActorStates.idle)
		{
			playSlashEnd = false;
		}
		if (actorState == ActorStates.running && newState == ActorStates.idle && !playRunToIdle && !cState.attacking && !cState.downSpikeRecovery && !cState.isToolThrowing)
		{
			SetPlayRunToIdle();
			cState.mantleRecovery = false;
			playMantleCancel = false;
			playBackflip = false;
		}
		actorStates = actorState;
		if ((actorStates == ActorStates.idle || actorStates == ActorStates.running) && newState != ActorStates.idle && newState != ActorStates.running)
		{
			cState.mantleRecovery = false;
			playMantleCancel = false;
			playBackflip = false;
		}
		if (newState == ActorStates.hard_landing)
		{
			playSlashLand = false;
			playSlashEnd = false;
		}
		if (newState == ActorStates.idle)
		{
			nextIdleLookTime = UnityEngine.Random.Range(4f, 10f);
		}
		prevActorState = actorState;
		actorState = newState;
	}

	public void PlayClip(string clipName)
	{
		if (controlEnabled)
		{
			PlayClipForced(clipName);
		}
	}

	public void PlayClipForced(string clipName)
	{
		animEventsTriggered = 0;
		if (!(clipName == "Exit Door To Idle"))
		{
			if (clipName == "Wake Up Ground")
			{
				animator.AnimationEventTriggered = AnimationEventTriggered;
			}
		}
		else
		{
			animator.AnimationCompleted = AnimationCompleteDelegate;
		}
		Play(clipName);
	}

	private void UpdateAnimation()
	{
		IsPlayingUpdraftAnim = false;
		IsPlayingWindyAnim = false;
		if (playLanding)
		{
			PlayLand();
			animator.AnimationCompleted = AnimationCompleteDelegate;
			playLanding = false;
		}
		if (playRunToIdle)
		{
			if (cState.inWalkZone)
			{
				Play("Walk To Idle");
			}
			else
			{
				Play("Run To Idle");
			}
			animator.AnimationCompleted = AnimationCompleteDelegate;
			playRunToIdle = false;
		}
		if (playBackDashToIdleEnd)
		{
			Play("Backdash Land 2");
			animator.AnimationCompleted = AnimationCompleteDelegate;
			playBackDashToIdleEnd = false;
		}
		if (playDashToIdle)
		{
			Play("Dash To Idle");
			animator.AnimationCompleted = AnimationCompleteDelegate;
			playDashToIdle = false;
		}
		if (playSuperJumpFall)
		{
			Play("Super Jump Fall");
			animator.AnimationCompleted = AnimationCompleteDelegate;
			playSuperJumpFall = false;
		}
		if (playDashUpperRecovery)
		{
			if (CanPlayDashUpperRecovery())
			{
				Play("Dash Upper Recovery");
				animator.AnimationCompleted = AnimationCompleteDelegate;
			}
			playDashUpperRecovery = false;
		}
		if (playSilkChargeEnd)
		{
			Play("Silk Charge End");
			playSilkChargeEnd = false;
		}
		if (actorState == ActorStates.no_input)
		{
			if (cState.recoilFrozen)
			{
				Play("Stun");
			}
			else if (cState.recoiling)
			{
				Play("Recoil");
			}
			else if (cState.transitioning)
			{
				if (cState.onGround)
				{
					if (heroCtrl.transitionState == HeroTransitionState.EXITING_SCENE)
					{
						if (!UpdateCheckIsPlayingRun() && !animator.IsPlaying("Dash") && !animator.IsPlaying("Idle To Run") && !animator.IsPlaying("Idle To Run Short") && !animator.IsPlaying("Land To Run") && !animator.IsPlaying("Idle To Run Weak"))
						{
							PlayRun();
						}
					}
					else if (heroCtrl.transitionState == HeroTransitionState.ENTERING_SCENE && !UpdateCheckIsPlayingRun())
					{
						PlayRun();
					}
				}
				else
				{
					switch (heroCtrl.transitionState)
					{
					case HeroTransitionState.EXITING_SCENE:
					case HeroTransitionState.WAITING_TO_ENTER_LEVEL:
						if (!animator.IsPlaying("Airborne") && !animator.IsPlaying("Dash Down") && !animator.IsPlaying("Shadow Dash Down"))
						{
							PlayFromFrame("Airborne", 7);
						}
						break;
					case HeroTransitionState.ENTERING_SCENE:
						if (setEntryAnim)
						{
							break;
						}
						switch (heroCtrl.gatePosition)
						{
						case GatePosition.top:
							if (heroCtrl.dashingDown)
							{
								Play("Dash Down");
							}
							else
							{
								PlayFromFrame("Airborne", 7, force: true);
							}
							break;
						case GatePosition.bottom:
							PlayFromFrame("Airborne", 3, force: true);
							break;
						}
						setEntryAnim = true;
						break;
					}
				}
			}
			if (!wasJumping && cState.jumping)
			{
				wantsToJump = true;
			}
		}
		else if (setEntryAnim)
		{
			setEntryAnim = false;
			if (!wasJumping && cState.jumping)
			{
				wantsToJump = true;
			}
		}
		else if (cState.dashing)
		{
			if (heroCtrl.dashingDown)
			{
				if (cState.shadowDashing)
				{
					Play("Shadow Dash Down");
				}
				else
				{
					Play("Dash Down");
				}
			}
			else if (cState.shadowDashing)
			{
				Play("Shadow Dash");
			}
			else if (cState.airDashing)
			{
				Play("Air Dash");
			}
			else
			{
				Play("Dash");
			}
		}
		else if (cState.backDashing)
		{
			Play("Back Dash");
		}
		else if (playSlashLand && cState.attackCount == previousAttackCount && cState.toolThrowCount == previousToolThrowCount && !cState.jumping)
		{
			PlaySlashLand();
			isPlayingSlashLand = true;
		}
		else if (cState.downSpikeAntic)
		{
			playSlashEnd = false;
			Play("DownSpike Antic");
		}
		else if (cState.downSpikeBouncing)
		{
			playSlashEnd = false;
			if (!playedDownSpikeBounce)
			{
				if (cState.downSpikeBouncingShort)
				{
					Play("DownSpikeBounce 1");
					cState.downSpikeBouncingShort = false;
				}
				else
				{
					Play(Probability.GetRandomItemByProbabilityFair<ProbabilityString, string>(downspikeAnims, ref downspikeAnimProbabilities));
				}
				playedDownSpikeBounce = true;
			}
		}
		else if (cState.downSpiking)
		{
			playSlashEnd = false;
			Play("DownSpike");
		}
		else if (cState.attacking && ((!canceledSlash && !playSlashEnd && !playSlashLand && !IsPlayingAirRecovery()) || cState.attackCount != previousAttackCount))
		{
			canceledSlash = false;
			playSlashLand = false;
			playSlashEnd = false;
			playMantleCancel = false;
			playBackflip = false;
			cState.mantleRecovery = false;
			float speedMultiplier = (heroCtrl.IsUsingQuickening ? heroCtrl.Config.QuickAttackSpeedMult : 1f);
			if (cState.upAttacking)
			{
				Play(cState.altAttack ? "UpSlashAlt" : "UpSlash", speedMultiplier);
			}
			else if (cState.downAttacking)
			{
				Play(cState.altAttack ? "DownSlashAlt" : "DownSlash", speedMultiplier);
			}
			else if (cState.wallSliding)
			{
				Play("Wall Slash", speedMultiplier);
			}
			else
			{
				Play(cState.altAttack ? "SlashAlt" : "Slash", speedMultiplier);
			}
		}
		else if (cState.isToolThrowing && (!canceledSlash || cState.toolThrowCount != previousToolThrowCount))
		{
			string text = null;
			if (cState.wallSliding)
			{
				text = "ToolThrow Wall";
			}
			else if (cState.throwingToolVertical == 0)
			{
				text = ((cState.toolThrowCount % 2 == 0) ? "ToolThrowAlt Q" : "ToolThrow Q");
			}
			else if (cState.throwingToolVertical > 0)
			{
				text = "ToolThrow Up";
			}
			else
			{
				Debug.LogError("ToolThrow Down anim not implemented");
			}
			if (!string.IsNullOrEmpty(text))
			{
				if (cState.toolThrowCount != previousToolThrowCount)
				{
					PlayFromFrame(text, 0, force: true);
				}
				else
				{
					Play(text);
				}
			}
			previousToolThrowCount = cState.toolThrowCount;
			canceledSlash = false;
			playSlashLand = false;
		}
		else if (cState.shuttleCock)
		{
			Play("Shuttlecock");
		}
		else if (cState.floating)
		{
			Play("Float");
		}
		else if (cState.wallClinging)
		{
			PlayFromFrame("Wall Cling", wasWallClinging ? 2 : 0);
			playSlashEnd = false;
			playMantleCancel = false;
			playBackflip = false;
		}
		else if (cState.wallSliding)
		{
			PlayFromFrame("Wall Slide", wasWallSliding ? 2 : 0);
			playSlashEnd = false;
			playMantleCancel = false;
			playBackflip = false;
		}
		else if (cState.downSpikeRecovery && !playedDownSpikeBounce && !cState.doubleJumping)
		{
			Play(cState.onGround ? "Downspike Recovery Land" : "Downspike Recovery");
		}
		else if (cState.casting)
		{
			Play("Fireball");
		}
		else if (actorState == ActorStates.idle)
		{
			if (cState.lookingUpAnim && !animator.IsPlaying("LookUp") && !animator.IsPlaying("LookUp Updraft") && !animator.IsPlaying("LookUp Windy"))
			{
				PlayLookUp();
			}
			else if (CanPlayLookDown())
			{
				PlayLookDown();
			}
			else if (!cState.lookingUpAnim && !cState.lookingDownAnim && CanPlayIdle())
			{
				PlayIdle();
			}
		}
		else if (actorState == ActorStates.running)
		{
			if (!IsPlayingTurn() && !animator.IsPlaying("TurnWalk"))
			{
				if (cState.inWalkZone)
				{
					if (didJustLand)
					{
						Play("Land To Walk");
					}
					else if (!wasInWalkZone)
					{
						if (IsHurt())
						{
							Play("Run To Walk Weak");
						}
						else
						{
							Play("Run To Walk");
						}
					}
					else if (!animator.IsPlaying("Run To Walk") && !animator.IsPlaying("Land To Walk") && !animator.IsPlaying("Run To Walk Weak"))
					{
						if (IsHurt())
						{
							string clipName = "Weak Walk Faster";
							if (!animator.IsPlaying(clipName))
							{
								skipIdleToRun = false;
								if (animator.CurrentClip.name == "Run To Walk")
								{
									PlayFromFrame(clipName, 1);
								}
								else
								{
									Play(clipName);
								}
							}
						}
						else
						{
							string clipName2 = (heroCtrl.IsUsingQuickening ? "Walk Q" : "Walk");
							if (!animator.IsPlaying(clipName2))
							{
								skipIdleToRun = false;
								if (animator.CurrentClip.name == "Run To Walk")
								{
									PlayFromFrame(clipName2, 1);
								}
								else
								{
									Play(clipName2);
								}
							}
						}
					}
				}
				else if (!animator.IsPlaying("Mantle Land"))
				{
					PlayRun();
				}
			}
		}
		else if (actorState == ActorStates.airborne && !animator.IsPlaying("Slash") && !animator.IsPlaying("SlashAlt"))
		{
			string clipName3 = (wallJumpedFromScramble ? "Walljump Somersault" : "Walljump");
			isPlayingSlashLand = false;
			playSlashEnd = false;
			playSlashLand = false;
			if (heroCtrl.wallLocked || (!cState.doubleJumping && wallJumpedFromScramble && animator.IsPlaying(clipName3)))
			{
				if (justWallJumped)
				{
					PlayFromFrame(clipName3, 0, force: true);
				}
				else
				{
					Play(clipName3);
				}
				playSlashEnd = false;
			}
			else if (cState.doubleJumping || playingDoubleJump)
			{
				if (!animator.IsPlaying("Double Jump"))
				{
					Play("Double Jump");
					playingDoubleJump = true;
				}
				else if (canForceDoubleJump)
				{
					PlayFromFrame("Double Jump", 0, force: true);
				}
				canForceDoubleJump = false;
			}
			else if (playingDownDashEnd)
			{
				if (!animator.IsPlaying("Dash Down End"))
				{
					Play("Dash Down End");
					playingDownDashEnd = true;
				}
			}
			else if (cState.jumping)
			{
				playSlashLand = false;
				playSlashEnd = false;
				if (!TryPlayMantleCancelJump())
				{
					PlayFromFrame("Airborne", 0, !wasJumping || wantsToJump);
				}
			}
			else if (cState.falling || startWithFallAnim)
			{
				if (!TryPlayMantleCancelJump() && !animator.IsPlaying("Super Jump Fall") && !animator.IsPlaying("Silk Charge End"))
				{
					bool flag = IsPlayingAirRecovery();
					if (!animator.IsPlaying("Airborne") && !flag)
					{
						PlayFromFrame("Airborne", wasPlayingAirRecovery ? 9 : 7);
					}
					wasPlayingAirRecovery = flag;
					if (startWithFallAnim)
					{
						startWithFallAnim = false;
					}
				}
			}
			else if (!TryPlayMantleCancelJump() && !animator.IsPlaying("Super Jump Fall") && !animator.IsPlaying("Silk Charge End") && !animator.IsPlaying("Airborne") && !IsPlayingAirRecovery())
			{
				PlayFromFrame("Airborne", 3);
			}
		}
		else if (actorState == ActorStates.dash_landing)
		{
			Play("Dash Down Land");
		}
		else if (actorState == ActorStates.hard_landing)
		{
			Play("HardLand");
		}
		if (cState.facingRight)
		{
			if (!wasFacingRight && cState.onGround && CanPlayTurn())
			{
				PlayTurn();
			}
			wasFacingRight = true;
		}
		else
		{
			if (wasFacingRight && cState.onGround && CanPlayTurn())
			{
				PlayTurn();
			}
			wasFacingRight = false;
		}
		if (!cState.downSpikeBouncing && !cState.downSpikeRecovery && playedDownSpikeBounce)
		{
			playedDownSpikeBounce = false;
		}
		previousAttackCount = cState.attackCount;
		ResetPlays();
		wasJumping = cState.jumping;
		wasWallSliding = cState.wallSliding;
		wasWallClinging = cState.wallClinging;
		justWallJumped = false;
		wasInWalkZone = cState.inWalkZone;
		didJustLand = false;
	}

	public bool IsPlayingTurn()
	{
		return animator.IsPlaying("Turn");
	}

	private bool TryPlayMantleCancelJump()
	{
		if (cState.mantleRecovery && (playMantleCancel || playBackflip))
		{
			if (!cState.onGround)
			{
				if (selectedMantleCancelJumpAnim == null)
				{
					if (playBackflip)
					{
						selectedMantleCancelJumpAnim = "Sprint Backflip";
					}
					else
					{
						selectedMantleCancelJumpAnim = ((cState.facingRight == wasFacingRightWhenStopped) ? "Mantle Cancel To Jump" : "Mantle Cancel To Jump Backwards");
					}
				}
				playSlashLand = false;
				Play(selectedMantleCancelJumpAnim);
				checkMantleCancel = true;
			}
			else
			{
				cState.mantleRecovery = false;
				selectedMantleCancelJumpAnim = null;
				playMantleCancel = false;
				playBackflip = false;
			}
			return true;
		}
		return false;
	}

	private void PlaySlashLand()
	{
		Play((actorState != ActorStates.running) ? "Slash Land" : (cState.altAttack ? "Slash Land Run Alt" : "Slash Land Run"));
	}

	private bool IsPlayingAirRecovery()
	{
		ProbabilityString[] array = downspikeAnims;
		foreach (ProbabilityString probabilityString in array)
		{
			if (animator.IsPlaying(probabilityString.Item))
			{
				return true;
			}
		}
		if (animator.IsPlaying("Dash Upper Recovery"))
		{
			return true;
		}
		return false;
	}

	private bool CanPlayIdle()
	{
		if (!isPlayingSlashLand && !animator.IsPlaying("Land") && !animator.IsPlaying("Land Q") && !animator.IsPlaying("Run To Idle") && !animator.IsPlaying("Walk To Idle") && !animator.IsPlaying("Dash To Idle") && !animator.IsPlaying("Backdash Land") && !animator.IsPlaying("Backdash Land 2") && !animator.IsPlaying("LookUpEnd") && !animator.IsPlaying("LookUpEnd Windy") && !animator.IsPlaying("Look Up Half End") && !animator.IsPlaying("LookDown Slight End") && !animator.IsPlaying("LookDownEnd") && !animator.IsPlaying("LookDownEnd Windy") && !animator.IsPlaying("Exit Door To Idle") && !animator.IsPlaying("Wake Up Ground") && !animator.IsPlaying("Hazard Respawn") && !animator.IsPlaying("Hurt Look Up Windy End") && !animator.IsPlaying("Hurt Look Up End") && !animator.IsPlaying("Hurt Look Down Windy End") && !IsPlayingAirRecovery())
		{
			return true;
		}
		return false;
	}

	private bool CanPlayLookDown()
	{
		if (cState.lookingDownAnim && !animator.IsPlaying("LookDown") && !animator.IsPlaying("LookDown Updraft") && !animator.IsPlaying("LookDown Windy") && !IsPlayingAirRecovery())
		{
			return true;
		}
		return false;
	}

	private bool CanPlayTurn()
	{
		if (!animator.IsPlaying("Wake Up Ground") && !animator.IsPlaying("Hazard Respawn") && !wasWallSliding)
		{
			return true;
		}
		return false;
	}

	private bool CanPlayDashUpperRecovery()
	{
		tk2dSpriteAnimationClip currentClip = animator.CurrentClip;
		if (currentClip != null && (currentClip.name == "Umbrella Deflate" || currentClip.name == "Dash Upper"))
		{
			return true;
		}
		return false;
	}

	private void PlayTurn()
	{
		playSlashLand = false;
		if (!animator.IsPlaying("Turn") && !animator.IsPlaying("TurnWalk"))
		{
			Play(cState.inWalkZone ? "TurnWalk" : "Turn");
		}
	}

	private void PlayLand()
	{
		Play((IsHurt() || IsInRage()) ? "Land Q" : "Land");
	}

	private void AnimationEventTriggered(tk2dSpriteAnimator sprite, tk2dSpriteAnimationClip clip, int frame)
	{
		if (clip.name == "Wake Up Ground")
		{
			AudioEvent audioEvent;
			switch (animEventsTriggered)
			{
			default:
				return;
			case 0:
				audioEvent = (heroCtrl.Config.ForceBareInventory ? wakeUpGroundCloakless : wakeUpGround1);
				break;
			case 1:
				if (heroCtrl.Config.ForceBareInventory)
				{
					return;
				}
				audioEvent = wakeUpGround2;
				break;
			}
			audioEvent.SpawnAndPlayOneShot(base.transform.position);
			animEventsTriggered++;
		}
		else if (clip.frames[frame].eventInfo == "Footstep")
		{
			if (meshRenderer.enabled)
			{
				audioCtrl.PlayFootstep();
			}
		}
		else if (clip.name == "Sprint Backflip")
		{
			backflipSpawnedAudio = backflipSpin.SpawnAndPlayLooped(null, base.transform.position, 0f, clearBackflipSpawnedAudio);
		}
	}

	private void AnimationCompleteDelegate(tk2dSpriteAnimator sprite, tk2dSpriteAnimationClip clip)
	{
		if (isPlayingSlashLand)
		{
			isPlayingSlashLand = false;
			playSlashEnd = true;
			canceledSlash = true;
			skipIdleToRun = true;
			if (cState.onGround)
			{
				playRunToIdle = true;
			}
		}
		switch (clip.name)
		{
		case "Land":
		case "Land Q":
		case "Run To Idle":
		case "Walk To Idle":
		case "Backdash To Idle":
		case "Dash To Idle":
		case "Exit Door To Idle":
			PlayIdle();
			break;
		case "Idle To Run":
		case "Idle To Run Short":
		case "Land To Run":
		case "Sprint Skid To Run":
		case "Sprint To Run":
		case "Slash To Run":
		case "Idle To Run Weak":
			Play(GetRunAnim());
			break;
		case "Idle Rest":
			ResetIdleLook();
			break;
		case "Slash":
		case "SlashAlt":
		case "UpSlash":
		case "ToolThrowAlt Q":
		case "ToolThrow Q":
		case "ToolThrow Up":
			if (!canceledSlash)
			{
				playSlashEnd = true;
				canceledSlash = true;
				skipIdleToRun = true;
				if (cState.onGround)
				{
					playRunToIdle = true;
				}
			}
			break;
		case "Slash Land":
		case "Slash Land Run":
		case "Slash Land Run Alt":
			playSlashLand = false;
			break;
		case "Mantle Land To Idle":
		case "Mantle Cancel To Jump":
		case "Mantle Cancel To Jump Backwards":
		case "Sprint Backflip":
			cState.mantleRecovery = false;
			selectedMantleCancelJumpAnim = null;
			playBackflip = false;
			break;
		case "Mantle Land To Run":
			cState.mantleRecovery = false;
			skipIdleToRun = true;
			break;
		case "Dash To Run":
			PlayFromFrame(GetRunAnim(), 2);
			break;
		case "Downspike Recovery Land":
			SetPlayRunToIdle();
			idleToRunShort = true;
			break;
		case "Double Jump":
			playingDoubleJump = false;
			break;
		case "Dash Down End":
			playingDownDashEnd = false;
			break;
		case "Super Jump Fall":
			PlayFromFrame("Airborne", 8);
			break;
		case "Rage Idle End":
			PlayFromFrame("Run To Idle", 1);
			break;
		}
	}

	public bool IsHurt()
	{
		if ((pd.health != 1 || pd.healthBlue >= 1) && !isCursed && !cState.isMaggoted)
		{
			return cState.fakeHurt;
		}
		return true;
	}

	private bool IsInRage()
	{
		return heroCtrl.WarriorState.IsInRageMode;
	}

	public bool CurrentClipNameContains(string clipName)
	{
		if (string.IsNullOrEmpty(clipName))
		{
			return false;
		}
		return animator.CurrentClip?.name.Contains(clipName) ?? false;
	}

	public void PlayIdle()
	{
		if (cState.mantleRecovery)
		{
			if (cState.onGround)
			{
				Play("Mantle Land To Idle");
			}
			else
			{
				cState.mantleRecovery = false;
			}
		}
		else if (IsHurt())
		{
			if (animator.IsPlaying("Hurt Look Up") || animator.IsPlaying("Hurt Look Up Windy") || CurrentClipNameContains("Hurt Listen Up"))
			{
				if (cState.inUpdraft)
				{
					Play("Hurt Look Up Windy End");
					IsPlayingUpdraftAnim = true;
				}
				else if (cState.inWindRegion)
				{
					Play("Hurt Look Up Windy End");
					IsPlayingWindyAnim = true;
				}
				else
				{
					Play("Hurt Look Up End");
				}
			}
			else if (animator.IsPlaying("Hurt Look Down Windy"))
			{
				if (cState.inUpdraft)
				{
					Play("Hurt Look Down Windy End");
					IsPlayingUpdraftAnim = true;
				}
				else if (cState.inWindRegion)
				{
					Play("Hurt Look Down Windy End");
					IsPlayingWindyAnim = true;
				}
				else
				{
					PlayFromLoopPoint("Idle Hurt Windy");
				}
			}
			else if (cState.inUpdraft)
			{
				if (animator.IsPlaying("Idle Hurt Listen Windy") || animator.IsPlaying("Idle Hurt Talk Windy") || CurrentClipNameContains("Hurt Look") || animator.IsPlaying("Hurt Listen Down Windy"))
				{
					PlayFromLoopPoint("Idle Hurt Windy");
				}
				else
				{
					Play("Idle Hurt Windy");
				}
				IsPlayingUpdraftAnim = true;
			}
			else if (cState.inWindRegion)
			{
				if (animator.IsPlaying("Idle Hurt Listen Windy") || animator.IsPlaying("Idle Hurt Talk Windy") || CurrentClipNameContains("Hurt Look") || animator.IsPlaying("Hurt Listen Down Windy"))
				{
					PlayFromLoopPoint("Idle Hurt Windy");
				}
				else
				{
					Play("Idle Hurt Windy");
				}
				IsPlayingWindyAnim = true;
			}
			else if (animator.IsPlaying("Idle Hurt Listen") || animator.IsPlaying("Idle Hurt Talk") || CurrentClipNameContains("Hurt Look") || animator.IsPlaying("Hurt Listen Down") || CurrentClipNameContains("Weak Walk"))
			{
				PlayFromLoopPoint("Idle Hurt");
			}
			else
			{
				Play("Idle Hurt");
			}
			IsPlayingHurtAnim = true;
		}
		else if (IsInRage())
		{
			Play("Rage Idle");
		}
		else if (animator.IsPlaying("Rage Idle") || animator.IsPlaying("Rage Idle End"))
		{
			Play("Rage Idle End");
			animator.AnimationCompleted = AnimationCompleteDelegate;
		}
		else if (animator.IsPlaying("LookUp") || animator.IsPlaying("LookUp Updraft") || animator.IsPlaying("LookUp Windy"))
		{
			if (cState.inUpdraft)
			{
				Play("LookUpEnd Updraft");
				IsPlayingUpdraftAnim = true;
			}
			else if (cState.inWindRegion)
			{
				Play("LookUpEnd Windy");
				IsPlayingWindyAnim = true;
			}
			else
			{
				Play("LookUpEnd");
			}
		}
		else if (animator.IsPlaying("LookDown") || animator.IsPlaying("LookDown Updraft") || animator.IsPlaying("LookDown Windy"))
		{
			if (cState.inUpdraft)
			{
				Play("LookDownEnd Updraft");
				IsPlayingUpdraftAnim = true;
			}
			else if (cState.inWindRegion)
			{
				Play("LookDownEnd Windy");
				IsPlayingWindyAnim = true;
			}
			else
			{
				Play("LookDownEnd");
			}
		}
		else if (animator.IsPlaying("Look Up Half"))
		{
			Play("Look Up Half End");
		}
		else if (animator.IsPlaying("LookDown Slight"))
		{
			Play("LookDown Slight End");
		}
		else if (animator.IsPlaying("Downspike Recovery Land"))
		{
			Play("Run To Idle");
		}
		else if (cState.inUpdraft)
		{
			Play("Idle Updraft");
			IsPlayingUpdraftAnim = true;
		}
		else if (cState.inWindRegion)
		{
			Play("Idle Windy");
			IsPlayingWindyAnim = true;
		}
		else
		{
			if (heroCtrl.controlReqlinquished || !meshRenderer.enabled)
			{
				ResetIdleLook();
			}
			else
			{
				nextIdleLookTime -= Time.deltaTime;
			}
			if (nextIdleLookTime <= 0f)
			{
				Play("Idle Rest");
				playingIdleRest = true;
				animator.AnimationCompleted = AnimationCompleteDelegate;
			}
			else
			{
				Play("Idle");
			}
		}
		skipIdleToRun = false;
		idleToRunShort = false;
		playSprintToRun = false;
		didSkid = false;
	}

	public void PlayLookUp()
	{
		cState.mantleRecovery = false;
		if (IsHurt())
		{
			if (cState.inUpdraft)
			{
				Play("Hurt Look Up Windy");
				IsPlayingUpdraftAnim = true;
			}
			else if (cState.inWindRegion)
			{
				Play("Hurt Look Up Windy");
				IsPlayingWindyAnim = true;
			}
			else
			{
				Play("Hurt Look Up");
			}
			IsPlayingHurtAnim = true;
		}
		else if (cState.inUpdraft)
		{
			Play("LookUp Updraft");
			IsPlayingUpdraftAnim = true;
		}
		else if (cState.inWindRegion)
		{
			Play("LookUp Windy");
			IsPlayingWindyAnim = true;
		}
		else
		{
			Play("LookUp");
		}
		ResetIdleLook();
	}

	public void PlayLookDown()
	{
		cState.mantleRecovery = false;
		if (IsHurt())
		{
			if (cState.inUpdraft)
			{
				Play("Hurt Look Down Windy");
				IsPlayingUpdraftAnim = true;
			}
			else if (cState.inWindRegion)
			{
				Play("Hurt Look Down Windy");
				IsPlayingWindyAnim = true;
			}
			else
			{
				Play("Hurt Look Down");
			}
			IsPlayingHurtAnim = true;
		}
		else if (cState.inUpdraft)
		{
			Play("LookDown Updraft");
			IsPlayingUpdraftAnim = true;
		}
		else if (cState.inWindRegion)
		{
			Play("LookDown Windy");
			IsPlayingWindyAnim = true;
		}
		else
		{
			Play("LookDown");
		}
		ResetIdleLook();
	}

	private void PlayRun()
	{
		if (cState.mantleRecovery)
		{
			if (cState.onGround)
			{
				Play("Mantle Land To Run");
			}
			else
			{
				cState.mantleRecovery = false;
			}
		}
		else
		{
			if (isPlayingSlashLand)
			{
				return;
			}
			if (playSlashEnd)
			{
				Play("Slash To Run");
			}
			else if (playSprintToRun)
			{
				Play(didSkid ? "Sprint Skid To Run" : "Sprint To Run");
			}
			else if (animator.IsPlaying("Dash To Idle") || animator.IsPlaying("Dash To Run"))
			{
				Play("Dash To Run");
			}
			else if (skipIdleToRun)
			{
				Play(GetRunAnim());
			}
			else if (didJustLand)
			{
				Play("Land To Run");
			}
			else if (!UpdateCheckIsPlayingRun() && !animator.IsPlaying("Idle To Run") && !animator.IsPlaying("Idle To Run Short") && !animator.IsPlaying("Sprint To Run") && !animator.IsPlaying("Sprint Skid To Run") && !animator.IsPlaying("Land To Run") && !animator.IsPlaying("Slash To Run") && !animator.IsPlaying("Idle To Run Weak"))
			{
				if (wasInWalkZone && IsHurt())
				{
					Play((idleToRunShort || animator.IsPlaying("Downspike Recovery Land")) ? "Idle To Run Short" : "Idle To Run Weak");
				}
				else if (CurrentClipNameContains("Land"))
				{
					Play("Land To Run");
				}
				else
				{
					Play((idleToRunShort || animator.IsPlaying("Downspike Recovery Land")) ? "Idle To Run Short" : "Idle To Run");
				}
			}
		}
		skipIdleToRun = false;
		idleToRunShort = false;
		playSprintToRun = false;
		playSlashEnd = false;
		didSkid = false;
	}

	private string GetRunAnim()
	{
		if (!heroCtrl.IsUsingQuickening)
		{
			return "Run";
		}
		return "Run Q";
	}

	private bool UpdateCheckIsPlayingRun()
	{
		if (!animator.IsPlaying("Run") && !animator.IsPlaying("Run Q"))
		{
			return false;
		}
		string text = animator.CurrentClip.name;
		string runAnim = GetRunAnim();
		if (text != runAnim)
		{
			Play(runAnim);
		}
		return true;
	}

	private void Play(string clipName, float speedMultiplier = 1f)
	{
		if (!(clipName == animator.CurrentClip.name))
		{
			if (playingIdleRest)
			{
				ResetIdleLook();
			}
			ResetPlaying();
			tk2dSpriteAnimationClip clip = GetClip(clipName);
			if (!Mathf.Approximately(speedMultiplier, 1f))
			{
				animator.Play(clip, 0f, clip.fps * speedMultiplier);
			}
			else
			{
				animator.Play(clip);
			}
			animator.AnimationEventTriggered = AnimationEventTriggered;
			animator.AnimationCompleted = AnimationCompleteDelegate;
			if (isPlayingSlashLand)
			{
				isPlayingSlashLand = false;
				playSlashLand = false;
			}
			if (checkMantleCancel)
			{
				playMantleCancel = false;
				playBackflip = false;
			}
		}
	}

	private void PlayFromFrame(string clipName, int frame, bool force = false)
	{
		if (clipName != animator.CurrentClip.name || force)
		{
			ResetPlaying();
			animator.PlayFromFrame(GetClip(clipName), frame);
		}
	}

	private void PlayFromLoopPoint(string clipName, bool force = false)
	{
		if (clipName != animator.CurrentClip.name || force)
		{
			ResetPlaying();
			tk2dSpriteAnimationClip clip = GetClip(clipName);
			animator.PlayFromFrame(clip, clip.loopStart);
		}
	}

	public void RefreshAnimationEvents()
	{
		if (cState.isSprinting)
		{
			tk2dSpriteAnimator obj = animator;
			obj.AnimationEventTriggered = (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>)Delegate.Remove(obj.AnimationEventTriggered, new Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>(AnimationEventTriggered));
			tk2dSpriteAnimator obj2 = animator;
			obj2.AnimationEventTriggered = (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>)Delegate.Combine(obj2.AnimationEventTriggered, new Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>(AnimationEventTriggered));
		}
	}

	public tk2dSpriteAnimationClip GetClip(string clipName)
	{
		if ((bool)config)
		{
			tk2dSpriteAnimationClip animationClip = config.GetAnimationClip(clipName);
			if (animationClip != null)
			{
				return animationClip;
			}
		}
		if (heroCtrl.cState.inWindRegion || heroCtrl.cState.inUpdraft)
		{
			tk2dSpriteAnimationClip clipByName = windyAnimLib.GetClipByName(clipName);
			if (clipByName != null)
			{
				return clipByName;
			}
		}
		tk2dSpriteAnimationClip clipByName2 = animator.GetClipByName(clipName);
		if (clipByName2 == null)
		{
			Debug.LogError($"Could not resolve animation clip: {clipName}", this);
		}
		return clipByName2;
	}

	public void ResetPlaying()
	{
		playingDoubleJump = false;
		playingDownDashEnd = false;
		IsPlayingHurtAnim = false;
		playingIdleRest = false;
		if ((bool)backflipSpawnedAudio)
		{
			backflipSpawnedAudio.Stop();
		}
	}

	public void AllowDoubleJumpReEntry()
	{
		canForceDoubleJump = true;
	}

	public void StopControl()
	{
		if (controlEnabled)
		{
			controlEnabled = false;
			stateBeforeControl = actorState;
			cState.mantleRecovery = false;
			selectedMantleCancelJumpAnim = null;
			playBackflip = false;
			wasFacingRightWhenStopped = cState.facingRight;
			playSuperJumpFall = false;
			ResetPlaying();
		}
	}

	public void StartControl()
	{
		actorState = heroCtrl.hero_state;
		ResetAll();
	}

	public void StartControlRunning()
	{
		StartControl();
	}

	public void StartControlWithoutSettingState()
	{
		controlEnabled = true;
		if (stateBeforeControl == ActorStates.running && actorState == ActorStates.running)
		{
			actorState = ActorStates.idle;
		}
	}

	public void StartControlToIdle()
	{
		StartControlToIdle(forcePlay: false);
	}

	public void StartControlToIdle(bool forcePlay)
	{
		actorState = heroCtrl.hero_state;
		controlEnabled = true;
		tk2dSpriteAnimationClip currentClip = animator.CurrentClip;
		if (currentClip == null || forcePlay || (currentClip.name != "Idle" && (!currentClip.name.Contains("land", StringComparison.InvariantCultureIgnoreCase) || currentClip.name == "Mantle Land To Run" || currentClip.name == "Downspike Recovery Land")))
		{
			SetPlayRunToIdle();
		}
		UpdateAnimation();
	}

	public void StopAttack()
	{
		if (animator.IsPlaying("UpSlash") || animator.IsPlaying("UpSlashAlt") || animator.IsPlaying("DownSlash") || animator.IsPlaying("DownSlashAlt"))
		{
			animator.Stop();
		}
	}

	public void StopToolThrow()
	{
		if (animator.IsPlaying("ToolThrowAlt Q") || animator.IsPlaying("ToolThrow Q") || animator.IsPlaying("ToolThrow Up"))
		{
			animator.Stop();
		}
	}

	public float GetCurrentClipDuration()
	{
		return (float)animator.CurrentClip.frames.Length / animator.CurrentClip.fps;
	}

	public float GetClipDuration(string clipName)
	{
		if (animator == null)
		{
			animator = GetComponent<tk2dSpriteAnimator>();
		}
		tk2dSpriteAnimationClip clip = GetClip(clipName);
		if (clip == null)
		{
			Debug.LogError("HeroAnim: Could not find animation clip with the name " + clipName);
			return -1f;
		}
		return (float)clip.frames.Length / clip.fps;
	}

	public bool IsTurnBlocked()
	{
		if (!animator.IsPlaying("DownSpikeBounce 2") && !animator.IsPlaying("DownSpike Antic") && !animator.IsPlaying("DownSpike") && !playedDownSpikeBounce && !animator.IsPlaying("Downspike Recovery Land") && !animator.CurrentClip.name.Equals("Harpoon Catch") && !animator.IsPlaying("Recoil Twirl") && !animator.IsPlaying("Slash_Charged") && !animator.IsPlaying("Super Jump Fall") && !animator.IsPlaying("Dash Upper Recovery"))
		{
			return animator.IsPlaying("Sprint Backflip");
		}
		return true;
	}

	public void FinishedDash()
	{
		playDashToIdle = true;
		if (actorState == ActorStates.running && !heroCtrl.dashingDown)
		{
			skipIdleToRun = true;
		}
	}

	public void FinishedSprint(bool didSkid)
	{
		this.didSkid = didSkid;
		if (didSkid)
		{
			if (actorState == ActorStates.running)
			{
				playSprintToRun = true;
			}
			else
			{
				playDashToIdle = true;
			}
		}
		else if (actorState == ActorStates.running)
		{
			playSprintToRun = true;
		}
		else if (actorState == ActorStates.idle && !playLanding)
		{
			playRunToIdle = true;
		}
	}

	public void SetPlayMantleCancel()
	{
		checkMantleCancel = false;
		playMantleCancel = true;
	}

	public void SetPlayBackflip()
	{
		checkMantleCancel = false;
		playBackflip = true;
	}

	public void SetPlaySuperJumpFall()
	{
		playSuperJumpFall = true;
	}

	public void SetPlayDashUpperRecovery()
	{
		playDashUpperRecovery = true;
	}

	public void SetPlayRunToIdle()
	{
		if (IsHurt())
		{
			PlayIdle();
		}
		else
		{
			playRunToIdle = true;
		}
	}

	public void SetWallJumped()
	{
		justWallJumped = true;
		UpdateWallScramble();
	}

	public void UpdateWallScramble()
	{
		wallJumpedFromScramble = cState.wallScrambling;
	}

	public void SetDownDashEnded()
	{
		playingDownDashEnd = true;
		playDashToIdle = false;
	}

	public void SetPlaySilkChargeEnd()
	{
		playSilkChargeEnd = true;
	}
}
