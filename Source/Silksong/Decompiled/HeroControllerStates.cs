using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Silksong;

[Serializable]
public class HeroControllerStates
{
	public bool facingRight;

	public bool onGround;

	public bool jumping;

	public bool shuttleCock;

	public bool floating;

	public bool wallJumping;

	public bool doubleJumping;

	public bool nailCharging;

	public bool shadowDashing;

	public bool swimming;

	public bool falling;

	public bool dashing;

	public bool isSprinting;

	public bool isBackSprinting;

	public bool isBackScuttling;

	public bool airDashing;

	public bool superDashing;

	public bool superDashOnWall;

	public bool backDashing;

	public bool touchingWall;

	public bool wallSliding;

	public bool wallClinging;

	public bool wallScrambling;

	public bool transitioning;

	public bool attacking;

	public int attackCount;

	public bool lookingUp;

	public bool lookingDown;

	public bool lookingUpRing;

	public bool lookingDownRing;

	public bool lookingUpAnim;

	public bool lookingDownAnim;

	public bool altAttack;

	public bool upAttacking;

	public bool downAttacking;

	public bool downTravelling;

	public bool downSpikeAntic;

	public bool downSpiking;

	public bool downSpikeBouncing;

	public bool downSpikeBouncingShort;

	public bool downSpikeRecovery;

	public bool bouncing;

	public bool shroomBouncing;

	public bool recoilingRight;

	public bool recoilingLeft;

	public bool recoilingDrill;

	public bool dead;

	public bool isFrostDeath;

	public bool hazardDeath;

	public bool hazardRespawning;

	public bool willHardLand;

	public bool recoilFrozen;

	public bool recoiling;

	public bool invulnerable;

	private int invulnerableCount;

	public bool casting;

	public bool castRecoiling;

	public bool preventDash;

	public bool preventBackDash;

	public bool dashCooldown;

	public bool backDashCooldown;

	public bool nearBench;

	public bool inWalkZone;

	public bool isPaused;

	public bool onConveyor;

	public bool onConveyorV;

	public bool inConveyorZone;

	public bool spellQuake;

	public bool freezeCharge;

	public bool focusing;

	public bool inAcid;

	public bool touchingNonSlider;

	public bool wasOnGround;

	public bool parrying;

	public bool parryAttack;

	public bool mantling;

	public bool mantleRecovery;

	public bool inUpdraft;

	public int downspikeInvulnerabilitySteps;

	public bool isToolThrowing;

	public int toolThrowCount;

	public int throwingToolVertical;

	public bool isInCancelableFSMMove;

	public bool inWindRegion;

	public bool isMaggoted;

	public bool inFrostRegion;

	public bool isFrosted;

	public bool isTouchingSlopeLeft;

	public bool isTouchingSlopeRight;

	public bool isBinding;

	public bool needolinPlayingMemory;

	public bool isScrewDownAttacking;

	public bool evading;

	public bool whipLashing;

	public bool fakeHurt;

	public bool isInCutsceneMovement;

	public bool isTriggerEventsPaused;

	private static BoolFieldAccessOptimizer<HeroControllerStates> boolFieldAccessOptimizer;

	private static Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();

	private HashSet<object> invulnerabilitySources = new HashSet<object>();

	public bool Invulnerable
	{
		get
		{
			if (!invulnerable)
			{
				return invulnerableCount > 0;
			}
			return true;
		}
	}

	public HeroControllerStates()
	{
		facingRight = false;
		if (boolFieldAccessOptimizer == null)
		{
			boolFieldAccessOptimizer = new BoolFieldAccessOptimizer<HeroControllerStates>();
		}
		Reset();
	}

	public bool GetState(string stateName)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			return boolFieldAccessOptimizer.GetField(this, stateName);
		}
		FieldInfo field = GetType().GetField(stateName);
		if (field != null)
		{
			return (bool)field.GetValue(HeroController.instance.cState);
		}
		Debug.LogError("HeroControllerStates: Could not find bool named" + stateName + "in cState");
		return false;
	}

	public void SetState(string stateName, bool value)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			boolFieldAccessOptimizer.SetField(this, stateName, value);
			return;
		}
		FieldInfo field = GetType().GetField(stateName);
		if (field != null)
		{
			try
			{
				field.SetValue(HeroController.instance.cState, value);
				return;
			}
			catch (Exception ex)
			{
				Debug.LogError("Failed to set cState: " + ex);
				return;
			}
		}
		Debug.LogError("HeroControllerStates: Could not find bool named" + stateName + "in cState");
	}

	public static bool CStateExists(string stateName)
	{
		if (CheatManager.UseFieldAccessOptimisers)
		{
			return boolFieldAccessOptimizer.FieldExists(typeof(HeroControllerStates), stateName);
		}
		return typeof(HeroControllerStates).GetField(stateName) != null;
	}

	public void Reset()
	{
		onGround = false;
		jumping = false;
		falling = false;
		dashing = false;
		isSprinting = false;
		isBackSprinting = false;
		backDashing = false;
		touchingWall = false;
		wallSliding = false;
		wallClinging = false;
		transitioning = false;
		attacking = false;
		lookingUp = false;
		lookingDown = false;
		altAttack = false;
		upAttacking = false;
		downAttacking = false;
		downTravelling = false;
		bouncing = false;
		dead = false;
		isFrostDeath = false;
		hazardDeath = false;
		willHardLand = false;
		recoiling = false;
		recoilFrozen = false;
		invulnerable = false;
		casting = false;
		castRecoiling = false;
		preventDash = false;
		preventBackDash = false;
		dashCooldown = false;
		backDashCooldown = false;
		attackCount = 0;
		downspikeInvulnerabilitySteps = 0;
		isToolThrowing = false;
		toolThrowCount = 0;
		throwingToolVertical = 0;
		isInCancelableFSMMove = false;
		mantling = false;
		isBinding = false;
		needolinPlayingMemory = false;
		isScrewDownAttacking = false;
		evading = false;
		fakeHurt = false;
		invulnerabilitySources.Clear();
		invulnerableCount = 0;
		isTriggerEventsPaused = false;
		isInCutsceneMovement = false;
	}

	public void ClearInvulnerabilitySources()
	{
		invulnerabilitySources.Clear();
		invulnerableCount = 0;
	}

	public void AddInvulnerabilitySource(object source)
	{
		if (source != null)
		{
			invulnerabilitySources.Add(source);
			invulnerableCount = invulnerabilitySources.Count;
		}
	}

	public void RemoveInvulnerabilitySource(object source)
	{
		if (invulnerabilitySources.Remove(source))
		{
			invulnerableCount = invulnerabilitySources.Count;
		}
	}
}
