using System;
using System.Collections.Generic;
using GlobalEnums;
using UnityEngine;

namespace Silksong;

public class SlideSurface : MonoBehaviour
{
	private enum SlideSpeeds
	{
		None = -1,
		Regular,
		Fast,
		Brake
	}

	private const string SLIDE_START_ANIM = "Slide Start";

	private const string SLIDE_NORMAL_ANIM = "Slide Normal";

	private const string SLIDE_FAST_ANIM = "Slide Fast";

	private const string SLIDE_FAST_END_ANIM = "Slide Fast End";

	private const string SLIDE_BRAKE_ANIM = "Slide Brake";

	private const string SLIDE_BRAKE_END_ANIM = "Slide Brake End";

	private const string JUMP_ANIM = "Airborne";

	private const float RAY_LENGTH = 0.7f;

	private const int MAX_RAY_HITS = 10;

	private const float FORCE_SLIDE_TIME = 0.15f;

	private const float FORCE_REGULAR_SLIDE_TIME = 0.3f;

	private const float FAST_SPEED_MULTIPLIER = 1.5f;

	private const float BRAKE_SPEED_MULTIPLIER = 0.7f;

	private const float SLOW_JUMP_AIR_VELOCITY = 15f;

	private const float SLOW_JUMP_AIR_VELOCITY_DECAY = 2.5f;

	private const float FAST_JUMP_AIR_VELOCITY = 18f;

	private const float FAST_JUMP_AIR_VELOCITY_DECAY = 3.5f;

	private const float JUMP_GRACE_TIME = 0.1f;

	public const PhysLayers SURFACE_LAYER = PhysLayers.HERO_DETECTOR;

	[SerializeField]
	private float initialSlideSpeed;

	[SerializeField]
	private float shallowSlideSpeed;

	[SerializeField]
	private float steepSlideSpeed;

	[SerializeField]
	private float shallowAcceleration;

	[SerializeField]
	private float steepAcceleration;

	[SerializeField]
	private CameraShakeTarget slideStartShake;

	[SerializeField]
	private AudioSource followLoopSource;

	[SerializeField]
	private AudioEvent enterAudio;

	[SerializeField]
	private AudioEvent exitAudio;

	[SerializeField]
	[ArrayForEnum(typeof(SlideSpeeds))]
	private ParticleSystem[] slideParticles;

	[SerializeField]
	private bool cameraLeading = true;

	private float heroFeetDistance;

	private readonly RaycastHit2D[] storeHits = new RaycastHit2D[10];

	private float inputDirection;

	private float inputAccelerationDirection;

	private float slopeSpeedMultiplier;

	private float forcedSlideTimeLeft;

	private float forcedRegularSlideTimeLeft;

	private float jumpGraceTimeLeft;

	private float slopeDot;

	private Vector2 slopeNormal;

	private float currentSlideSpeed;

	private bool endedOnGround;

	private ParticleSystem currentSlideParticles;

	private Vector2 slideParticlesPosition;

	private bool isHeroInside;

	private bool isHeroAttached;

	private HeroController hc;

	private EnviroRegionListener enviroListener;

	private Rigidbody2D body;

	private tk2dSpriteAnimator animator;

	private BoxCollider2D heroCollider;

	private Collider2D selfCollider;

	private InputHandler ih;

	private CameraTarget cameraTarget;

	private static readonly List<SlideSurface> _slideSurfaces = new List<SlideSurface>();

	private static int _heroInsideCount;

	private bool wasAbove;

	private bool didSetEnviroOverride;

	public static bool IsHeroInside => _heroInsideCount > 0;

	public static bool IsHeroSliding { get; private set; }

	public static bool IsInJumpGracePeriod
	{
		get
		{
			foreach (SlideSurface slideSurface in _slideSurfaces)
			{
				if (slideSurface.jumpGraceTimeLeft > 0f)
				{
					return true;
				}
			}
			return false;
		}
	}

	private void OnValidate()
	{
		ArrayForEnumAttribute.EnsureArraySize(ref slideParticles, typeof(SlideSpeeds));
	}

	private void Awake()
	{
		OnValidate();
		ParticleSystem[] array = slideParticles;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}
		selfCollider = GetComponent<Collider2D>();
		EventRegister.GetRegisterGuaranteed(base.gameObject, "HERO DAMAGED").ReceivedEvent += OnHeroInterrupted;
		EventRegister.GetRegisterGuaranteed(base.gameObject, "FSM CANCEL").ReceivedEvent += OnHeroInterrupted;
	}

	private void OnEnable()
	{
		_slideSurfaces.AddIfNotPresent(this);
	}

	private void Start()
	{
		ih = ManagerSingleton<InputHandler>.Instance;
		cameraTarget = GameCameras.instance.cameraTarget;
	}

	private void OnDisable()
	{
		_slideSurfaces.Remove(this);
		if (didSetEnviroOverride)
		{
			if (enviroListener != null)
			{
				enviroListener.ClearOverride();
			}
			didSetEnviroOverride = false;
		}
	}

	private void OnDestroy()
	{
		if (isHeroInside)
		{
			isHeroInside = false;
			_heroInsideCount--;
		}
	}

	private void Update()
	{
		bool flag = false;
		if (!isHeroAttached)
		{
			if ((bool)enviroListener)
			{
				didSetEnviroOverride = false;
				enviroListener.ClearOverride();
				enviroListener = null;
			}
			if (!(jumpGraceTimeLeft > 0f))
			{
				return;
			}
			flag = true;
			jumpGraceTimeLeft -= Time.deltaTime;
		}
		if (!flag && forcedSlideTimeLeft > 0f)
		{
			forcedSlideTimeLeft -= Time.deltaTime;
		}
		else if (ih.GetWasButtonPressedQueued(HeroActionButton.JUMP, consume: true))
		{
			JumpOff();
			return;
		}
		if (flag)
		{
			return;
		}
		if (forcedRegularSlideTimeLeft > 0f)
		{
			forcedRegularSlideTimeLeft -= Time.deltaTime;
			inputDirection = 0f;
		}
		else
		{
			inputDirection = ih.inputActions.MoveVector.X;
		}
		UpdateFollowPositions();
		if (Math.Abs(inputDirection) > Mathf.Epsilon)
		{
			inputAccelerationDirection = inputDirection * slopeNormal.x;
			if (inputAccelerationDirection > 0f)
			{
				slopeSpeedMultiplier = 1.5f;
				animator.Play("Slide Fast");
				UpdateSlideEffects(SlideSpeeds.Fast);
			}
			else
			{
				slopeSpeedMultiplier = 0.7f;
				animator.Play("Slide Brake");
				UpdateSlideEffects(SlideSpeeds.Brake);
			}
		}
		else
		{
			slopeSpeedMultiplier = 1f;
			if (Math.Abs(inputAccelerationDirection) > Mathf.Epsilon)
			{
				animator.Play((inputAccelerationDirection > 0f) ? "Slide Fast End" : "Slide Brake End");
				UpdateSlideEffects(SlideSpeeds.Regular);
			}
			inputAccelerationDirection = 0f;
		}
	}

	private void FixedUpdate()
	{
		if (isHeroAttached)
		{
			float b = Mathf.Lerp(steepSlideSpeed, shallowSlideSpeed, slopeDot) * slopeSpeedMultiplier;
			float num = Mathf.Lerp(steepAcceleration, shallowAcceleration, slopeDot);
			currentSlideSpeed = Mathf.Lerp(currentSlideSpeed, b, num * Time.deltaTime);
			Vector2 vector = new Vector2(Mathf.Sign(slopeNormal.x) * currentSlideSpeed, 0f) * slopeDot;
			body.linearVelocity = vector;
			if (!TryPositionOnSlope(body.position + vector * Time.deltaTime, out var newHeroPos))
			{
				Detach(allowQueueing: true);
				hc.AddExtraAirMoveVelocity(new HeroController.DecayingVelocity
				{
					Velocity = new Vector2(15f * Mathf.Sign(slopeNormal.x), 0f),
					Decay = 2.5f
				});
			}
			else
			{
				body.MovePosition(newHeroPos);
				UpdateFacing();
			}
		}
	}

	private void OnHeroInterrupted()
	{
		if (isHeroAttached)
		{
			Cancel();
		}
	}

	private void OnTriggerEnter2D(Collider2D collision)
	{
		hc = collision.GetComponent<HeroController>();
		if ((bool)hc && hc.isHeroInPosition)
		{
			if (!isHeroInside)
			{
				isHeroInside = true;
				_heroInsideCount++;
			}
			if (!isHeroAttached)
			{
				body = hc.GetComponent<Rigidbody2D>();
				Vector2 position = body.position;
				Vector2 vector = selfCollider.ClosestPoint(position);
				wasAbove = position.y >= vector.y;
				OnTriggerStay2D(collision);
			}
		}
	}

	private void OnTriggerStay2D(Collider2D collision)
	{
		if (isHeroAttached || !hc || !hc.isHeroInPosition)
		{
			return;
		}
		if (body.linearVelocity.y > 0f && body.gravityScale > 0f)
		{
			Vector2 position = body.position;
			Vector2 vector = selfCollider.ClosestPoint(position);
			if (!(position.y - 0.25f < vector.y))
			{
				wasAbove = true;
				return;
			}
			if (!wasAbove)
			{
				return;
			}
		}
		if (hc.cState.mantling)
		{
			return;
		}
		heroCollider = hc.GetComponent<BoxCollider2D>();
		heroFeetDistance = heroCollider.size.y * 0.5f - heroCollider.offset.y;
		if (TryPositionOnSlope(body.position, out var newHeroPos))
		{
			body.position = newHeroPos;
			enviroListener = hc.GetComponent<EnviroRegionListener>();
			enviroListener.SetOverride(EnvironmentTypes.PeakPuff);
			didSetEnviroOverride = true;
			EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
			hc.ResetAirMoves();
			hc.RelinquishControl();
			hc.StopAnimationControl();
			hc.AffectedByGravity(gravityApplies: false);
			hc.SetAllowNailChargingWhileRelinquished(value: false);
			heroCollider.enabled = false;
			isHeroAttached = true;
			endedOnGround = false;
			if (cameraLeading)
			{
				cameraTarget.SetSliding(active: true);
			}
			IsHeroSliding = true;
			body.transform.SetPosition2D(body.position);
			UpdateFacing();
			animator = hc.GetComponent<tk2dSpriteAnimator>();
			tk2dSpriteAnimator obj = animator;
			obj.AnimationCompleted = (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>)Delegate.Combine(obj.AnimationCompleted, new Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>(OnAnimationCompleted));
			animator.Play("Slide Start");
			forcedSlideTimeLeft = 0.15f;
			forcedRegularSlideTimeLeft = 0.3f;
			currentSlideSpeed = initialSlideSpeed;
			UpdateFollowPositions();
			UpdateSlideEffects(SlideSpeeds.Regular);
			slideStartShake.DoShake(this);
			Vector3 position2 = body.transform.position;
			enterAudio.SpawnAndPlayOneShot(position2);
			if ((bool)followLoopSource)
			{
				followLoopSource.Play();
			}
		}
	}

	private void OnTriggerExit2D(Collider2D collision)
	{
		HeroNotInside();
	}

	private void HeroNotInside()
	{
		if (isHeroInside)
		{
			isHeroInside = false;
			_heroInsideCount--;
		}
	}

	private bool TryPositionOnSlope(in Vector2 heroPos, out Vector2 newHeroPos)
	{
		if (TryPositionOnSlope(in heroPos, out newHeroPos, isBackRay: false))
		{
			return true;
		}
		if (!isHeroAttached)
		{
			return false;
		}
		if (TryPositionOnSlope(in heroPos, out newHeroPos, isBackRay: true))
		{
			return true;
		}
		return false;
	}

	private bool TryPositionOnSlope(in Vector2 heroPos, out Vector2 newHeroPos, bool isBackRay)
	{
		Vector2 origin = heroPos;
		float num = heroCollider.size.y * 0.5f + heroCollider.offset.y;
		origin.y += num;
		float num2 = 0f;
		if (isBackRay)
		{
			float num3 = Mathf.Sign(slopeNormal.x);
			float num4 = heroCollider.size.x * 0.5f + 0.001f;
			origin.x += (0f - num4) * num3;
			Vector2 vector = new Vector2(slopeNormal.y, 0f - slopeNormal.x);
			num2 = 0f - (vector * (num4 * (1f / Mathf.Abs(vector.x)))).y;
		}
		Vector2 down = Vector2.down;
		float distance = num + heroFeetDistance + 0.7f;
		int num5 = Physics2D.RaycastNonAlloc(origin, down, storeHits, distance, 8448);
		if (num5 > 10)
		{
			num5 = 10;
		}
		bool flag = false;
		bool flag2 = false;
		RaycastHit2D raycastHit2D = default(RaycastHit2D);
		float num6 = float.MaxValue;
		for (int i = 0; i < num5; i++)
		{
			RaycastHit2D raycastHit2D2 = storeHits[i];
			bool flag3;
			if (raycastHit2D2.collider.gameObject.layer == 13)
			{
				if (!raycastHit2D2.collider.isTrigger)
				{
					continue;
				}
				SlideSurface component = raycastHit2D2.collider.GetComponent<SlideSurface>();
				if (!component || component != this)
				{
					continue;
				}
				flag3 = true;
			}
			else
			{
				if (raycastHit2D2.collider.isTrigger)
				{
					continue;
				}
				flag3 = false;
			}
			float num7 = origin.y - raycastHit2D2.point.y;
			if (num7 < num6)
			{
				raycastHit2D = raycastHit2D2;
				num6 = num7;
				flag = flag3;
				flag2 = !flag3;
			}
		}
		if (!flag)
		{
			endedOnGround = flag2;
			newHeroPos = heroPos;
			return false;
		}
		float num8 = raycastHit2D.distance - heroFeetDistance + num2;
		Vector2 vector2 = heroPos;
		vector2.y -= num8;
		newHeroPos = vector2;
		slopeNormal = raycastHit2D.normal;
		slopeDot = Mathf.Abs(Vector2.Dot(slopeNormal, Vector2.up));
		return true;
	}

	private void Detach(bool allowQueueing)
	{
		Cancel();
		if (endedOnGround)
		{
			hc.DoSprintSkid();
		}
		exitAudio.SpawnAndPlayOneShot(body.transform.position);
		jumpGraceTimeLeft = (allowQueueing ? 0.1f : 0f);
	}

	private void Cancel()
	{
		isHeroAttached = false;
		HeroNotInside();
		tk2dSpriteAnimator obj = animator;
		obj.AnimationCompleted = (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>)Delegate.Remove(obj.AnimationCompleted, new Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>(OnAnimationCompleted));
		cameraTarget.SetSliding(active: false);
		IsHeroSliding = false;
		UpdateSlideEffects(SlideSpeeds.None);
		if ((bool)followLoopSource)
		{
			followLoopSource.Stop();
		}
		hc.RegainControl();
		hc.StartAnimationControl();
		hc.AffectedByGravity(gravityApplies: true);
		heroCollider.enabled = true;
	}

	private void JumpOff()
	{
		if (!isHeroAttached)
		{
			if (!hc.CanTakeControl())
			{
				return;
			}
			EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
			hc.RelinquishControl();
			hc.StopAnimationControl();
			hc.AffectedByGravity(gravityApplies: false);
			hc.ResetInputQueues();
		}
		animator.Play("Airborne");
		float num = ih.inputActions.MoveVector.X;
		if (num == 0f)
		{
			num = Mathf.Sign(slopeNormal.x);
		}
		float num2;
		float decay;
		if (num * slopeNormal.x < 0f)
		{
			num2 = 15f;
			decay = 2.5f;
			hc.SetStartWithTinyJump();
			hc.SetDoFullJump();
		}
		else
		{
			num2 = 18f;
			decay = 3.5f;
			hc.SetStartWithDownSpikeBounce();
		}
		body.linearVelocity = new Vector2(0f, 10f);
		Detach(allowQueueing: false);
		hc.AddExtraAirMoveVelocity(new HeroController.DecayingVelocity
		{
			Velocity = new Vector2(num2 * Mathf.Sign(slopeNormal.x), 0f),
			Decay = decay
		});
	}

	private void UpdateFacing()
	{
		if (hc.cState.facingRight)
		{
			if (slopeNormal.x < 0f)
			{
				hc.FaceLeft();
			}
		}
		else if (slopeNormal.x > 0f)
		{
			hc.FaceRight();
		}
	}

	private void OnAnimationCompleted(tk2dSpriteAnimator currentAnimator, tk2dSpriteAnimationClip clip)
	{
		if (clip.name == "Slide Start" || clip.name == "Slide Fast End" || clip.name == "Slide Brake End")
		{
			currentAnimator.Play("Slide Normal");
		}
	}

	private void UpdateSlideEffects(SlideSpeeds slideSpeed)
	{
		ParticleSystem particleSystem = ((slideSpeed > SlideSpeeds.None) ? slideParticles[(int)slideSpeed] : null);
		if ((bool)currentSlideParticles)
		{
			if (particleSystem == currentSlideParticles)
			{
				return;
			}
			currentSlideParticles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
		}
		if (particleSystem != null)
		{
			particleSystem.transform.SetPosition2D(slideParticlesPosition);
			particleSystem.Clear(withChildren: true);
			particleSystem.Play(withChildren: true);
		}
		currentSlideParticles = particleSystem;
	}

	private void UpdateFollowPositions()
	{
		Vector3 position = body.transform.position;
		slideParticlesPosition = (Vector2)position - new Vector2(0f, heroFeetDistance);
		if ((bool)currentSlideParticles)
		{
			currentSlideParticles.transform.SetPosition2D(slideParticlesPosition);
		}
		if ((bool)followLoopSource)
		{
			followLoopSource.transform.position = position;
		}
	}
}
