using TeamCherry.SharedUtils;
using UnityEngine;

namespace Silksong;

[CreateAssetMenu(menuName = "Hornet/HeroController Config")]
public class HeroControllerConfig : ScriptableObject, IIncludeVariableExtensions
{
	public enum DownSlashTypes
	{
		DownSpike,
		Slash,
		Custom
	}

	[Header("Animation")]
	[SerializeField]
	private tk2dSpriteAnimation heroAnimOverrideLib;

	[Header("Abilities")]
	[SerializeField]
	private bool canPlayNeedolin;

	[SerializeField]
	private bool canBrolly;

	[SerializeField]
	private bool canDoubleJump;

	[SerializeField]
	private bool canNailCharge;

	[SerializeField]
	private bool canBind;

	[SerializeField]
	private bool canHarpoonDash;

	[Header("UI")]
	[SerializeField]
	private bool forceBareInventory;

	[Header("\"Constants\"")]
	[SerializeField]
	private DownSlashTypes downSlashType;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsDownSlashTypeCustom", true, true, true)]
	[InspectorValidation]
	private string downSlashEvent;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsDownSlashTypeDownSpike", true, true, true)]
	private float downspikeAnticTime;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsDownSlashTypeDownSpike", true, true, true)]
	private float downspikeTime;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("ShowDownSlashSpeed", true, true, true)]
	private float downspikeSpeed;

	[SerializeField]
	private float downspikeRecoveryTime;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsDownSlashTypeDownSpike", true, true, true)]
	private bool downspikeBurstEffect = true;

	[SerializeField]
	[ModifiableProperty]
	[Conditional("IsDownSlashTypeDownSpike", true, true, true)]
	private bool downspikeThrusts = true;

	[Space]
	[SerializeField]
	private float dashStabSpeed;

	[SerializeField]
	private float dashStabTime;

	[SerializeField]
	private bool forceShortDashStabBounce;

	[SerializeField]
	private float dashStabBounceJumpSpeed;

	[SerializeField]
	private int dashStabSteps = 1;

	[Space]
	[SerializeField]
	private float attackDuration;

	[SerializeField]
	private float quickAttackSpeedMult;

	[SerializeField]
	private float attackRecoveryTime;

	[SerializeField]
	private float attackCooldownTime;

	[SerializeField]
	private float quickAttackCooldownTime;

	[SerializeField]
	private bool canTurnWhileSlashing;

	[Space]
	[SerializeField]
	private bool chargeSlashRecoils;

	[SerializeField]
	private float chargeSlashLungeSpeed;

	[SerializeField]
	private float chargeSlashLungeDeceleration = 1f;

	[SerializeField]
	private int chargeSlashChain;

	[SerializeField]
	private bool wallSlashSlowdown;

	public bool CanPlayNeedolin => canPlayNeedolin;

	public bool CanBrolly => canBrolly;

	public bool CanDoubleJump => canDoubleJump;

	public bool CanNailCharge => canNailCharge;

	public bool CanBind => canBind;

	public bool CanHarpoonDash => canHarpoonDash;

	public bool ForceBareInventory => forceBareInventory;

	public DownSlashTypes DownSlashType => downSlashType;

	public string DownSlashEvent => downSlashEvent;

	public float DownSpikeAnticTime => downspikeAnticTime;

	public float DownSpikeTime => downspikeTime;

	public float DownspikeSpeed => downspikeSpeed;

	public float DownspikeRecoveryTime => downspikeRecoveryTime;

	public bool DownspikeBurstEffect => downspikeBurstEffect;

	public bool DownspikeThrusts => downspikeThrusts;

	public float DashStabSpeed => dashStabSpeed;

	public float DashStabTime => dashStabTime;

	public bool ForceShortDashStabBounce => forceShortDashStabBounce;

	public float DashStabBounceJumpSpeed => dashStabBounceJumpSpeed;

	public int DashStabSteps => dashStabSteps;

	public virtual float AttackDuration => attackDuration;

	public virtual float QuickAttackSpeedMult => quickAttackSpeedMult;

	public virtual float AttackRecoveryTime => attackRecoveryTime;

	public virtual float AttackCooldownTime => attackCooldownTime;

	public virtual float QuickAttackCooldownTime => quickAttackCooldownTime;

	public bool CanTurnWhileSlashing => canTurnWhileSlashing;

	public bool ChargeSlashRecoils => chargeSlashRecoils;

	public float ChargeSlashLungeSpeed => chargeSlashLungeSpeed;

	public float ChargeSlashLungeDeceleration => chargeSlashLungeDeceleration;

	public int ChargeSlashChain => chargeSlashChain;

	public bool WallSlashSlowdown => wallSlashSlowdown;

	private bool IsDownSlashTypeDownSpike()
	{
		return downSlashType == DownSlashTypes.DownSpike;
	}

	private bool ShowDownSlashSpeed()
	{
		if (downspikeThrusts)
		{
			return IsDownSlashTypeDownSpike();
		}
		return false;
	}

	private bool IsDownSlashTypeSlash()
	{
		return downSlashType == DownSlashTypes.Slash;
	}

	private bool IsDownSlashTypeCustom()
	{
		return downSlashType == DownSlashTypes.Custom;
	}

	private void OnValidate()
	{
		if (dashStabSteps < 1)
		{
			dashStabSteps = 1;
		}
	}

	public tk2dSpriteAnimationClip GetAnimationClip(string clipName)
	{
		if (!heroAnimOverrideLib)
		{
			return null;
		}
		return heroAnimOverrideLib.GetClipByName(clipName);
	}

	public void OnUpdatedVariable(string variableName)
	{
	}
}
