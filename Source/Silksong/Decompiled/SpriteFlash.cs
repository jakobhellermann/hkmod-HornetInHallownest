using System;
using System.Collections;
using System.Collections.Generic;
using GlobalSettings;
using JetBrains.Annotations;
using TeamCherry.SharedUtils;
using UnityEngine;

namespace Silksong;

public class SpriteFlash : MonoBehaviour
{
	public struct FlashHandle : IEquatable<FlashHandle>
	{
		private readonly int id;

		private readonly SpriteFlash owner;

		public int ID => id;

		public FlashHandle(int id, SpriteFlash owner)
		{
			this.id = id;
			this.owner = owner;
		}

		public bool Equals(FlashHandle other)
		{
			if (id == other.id)
			{
				return owner == other.owner;
			}
			return false;
		}
	}

	private class RepeatingFlash
	{
		public float Amount;

		public Color Colour;

		public FlashHandle Handle;

		public bool RequireExplicitCancel;

		public int Priority;

		public Coroutine Routine;
	}

	[Serializable]
	public struct FlashConfig
	{
		public Color Colour;

		[Range(0f, 1f)]
		public float Amount;

		public float TimeUp;

		public float StayTime;

		public float TimeDown;
	}

	[Flags]
	private enum StartFlashes
	{
		[UsedImplicitly]
		None = 0,
		HitFlash = 1,
		QuestPickup = 2
	}

	[SerializeField]
	[Tooltip("Mirrors all flashes to children.")]
	private List<SpriteFlash> children;

	[SerializeField]
	[Tooltip("Add itself to parents children on Awake.")]
	private List<SpriteFlash> parents;

	[SerializeField]
	private bool getParent;

	[SerializeField]
	[HideInInspector]
	[Obsolete]
	private bool doHitFlashOnEnable;

	[SerializeField]
	private StartFlashes startFlashes;

	private float geoTimer;

	private bool geoFlash;

	private Coroutine singleFlashRoutine;

	private Renderer renderer;

	private MaterialPropertyBlock block;

	private float singleFlashAmount;

	private Color singleFlashColour;

	private FlashHandle singleFlashHandle;

	private readonly List<RepeatingFlash> repeatingFlashes = new List<RepeatingFlash>();

	private RepeatingFlash currentRepeatingFlash;

	private bool flashChanged;

	private int lastFlashId;

	private float extraMixAmount;

	private static readonly int _flashAmountId = Shader.PropertyToID("_FlashAmount");

	private static readonly int _flashColorId = Shader.PropertyToID("_FlashColor");

	private bool isBlocked;

	public Color ExtraMixColor { get; set; }

	public float ExtraMixAmount
	{
		get
		{
			return extraMixAmount;
		}
		set
		{
			extraMixAmount = Mathf.Clamp01(value);
			UpdateMaterial();
		}
	}

	public bool IsBlocked
	{
		get
		{
			return isBlocked;
		}
		set
		{
			isBlocked = value;
			UpdateMaterial();
		}
	}

	private void OnValidate()
	{
		if (doHitFlashOnEnable)
		{
			startFlashes |= StartFlashes.HitFlash;
			doHitFlashOnEnable = false;
		}
	}

	private void Awake()
	{
		OnValidate();
		if (getParent && (bool)base.transform.parent)
		{
			SpriteFlash componentInParent = base.transform.parent.GetComponentInParent<SpriteFlash>();
			if ((bool)componentInParent)
			{
				parents.Add(componentInParent);
			}
		}
		foreach (SpriteFlash parent in parents)
		{
			if ((bool)parent)
			{
				parent.children.AddIfNotPresent(this);
			}
		}
	}

	private void OnDisable()
	{
		ComponentSingleton<SpriteFlashCallbackHooks>.Instance.OnUpdate -= OnUpdate;
		CancelFlash();
	}

	private void OnEnable()
	{
		ComponentSingleton<SpriteFlashCallbackHooks>.Instance.OnUpdate += OnUpdate;
		DoStartFlashes();
	}

	private void DoStartFlashes()
	{
		if (startFlashes.HasFlag(StartFlashes.HitFlash))
		{
			FlashEnemyHit();
		}
		if (startFlashes.HasFlag(StartFlashes.QuestPickup))
		{
			flashWhiteLong();
			FlashingWhiteLong();
		}
	}

	private void OnUpdate()
	{
		if (flashChanged)
		{
			UpdateMaterial();
		}
		if (geoFlash)
		{
			if (geoTimer > 0f)
			{
				geoTimer -= Time.deltaTime;
				return;
			}
			Flash(new Color(1f, 1f, 1f), 0.7f, 0.2f, 0.01f, 0.2f, 0.1f, repeating: true, 5);
			geoFlash = false;
		}
	}

	public void GeoFlash()
	{
		geoFlash = true;
		geoTimer = 0.25f;
	}

	private void UpdateMaterial()
	{
		flashChanged = false;
		Coroutine coroutine;
		float num;
		Color color;
		if (currentRepeatingFlash != null)
		{
			coroutine = currentRepeatingFlash.Routine;
			num = currentRepeatingFlash.Amount;
			color = currentRepeatingFlash.Colour;
		}
		else
		{
			coroutine = null;
			num = 0f;
			color = Color.black;
		}
		float num2;
		Color color2;
		if (singleFlashRoutine != null && coroutine == null)
		{
			num2 = singleFlashAmount;
			color2 = singleFlashColour;
		}
		else if (singleFlashRoutine == null && coroutine != null)
		{
			num2 = num;
			color2 = color;
		}
		else if (singleFlashRoutine != null && coroutine != null)
		{
			num2 = Mathf.Lerp(num, singleFlashAmount, singleFlashAmount);
			color2 = Color.Lerp(color, singleFlashColour, singleFlashAmount);
		}
		else
		{
			num2 = 0f;
			color2 = Color.white;
		}
		if (extraMixAmount > 0f)
		{
			num2 = Mathf.Lerp(num2, 1f, extraMixAmount);
			color2 = Color.Lerp(color2, ExtraMixColor, extraMixAmount);
		}
		if (isBlocked)
		{
			num2 = 0f;
		}
		SetParams(num2, color2);
		SetParamsChildrenRecursive(this, num2, color2);
	}

	private static void SetParamsChildrenRecursive(SpriteFlash parent, float flashAmount, Color flashColour)
	{
		parent.children.RemoveAll((SpriteFlash o) => o == null);
		for (int num = parent.children.Count - 1; num >= 0; num--)
		{
			SpriteFlash spriteFlash = parent.children[num];
			if (!(spriteFlash == parent))
			{
				spriteFlash.SetParams(flashAmount, flashColour);
				SetParamsChildrenRecursive(spriteFlash, flashAmount, flashColour);
			}
		}
	}

	private void SetParams(float flashAmount, Color flashColour)
	{
		if (block == null)
		{
			block = new MaterialPropertyBlock();
		}
		else
		{
			block.Clear();
		}
		if (renderer == null)
		{
			renderer = GetComponent<Renderer>();
		}
		if ((bool)renderer)
		{
			GetPropertyBlock(block);
			block.SetFloat(_flashAmountId, flashAmount);
			block.SetColor(_flashColorId, flashColour);
			SetPropertyBlock(block);
		}
	}

	public FlashHandle Flash(FlashConfig config)
	{
		return Flash(config.Colour, config.Amount, config.TimeUp, config.StayTime, config.TimeDown);
	}

	public FlashHandle Flash(Color flashColour, float amount, float timeUp, float stayTime, float timeDown, float stayDownTime = 0f, bool repeating = false, int repeatTimes = 0, int repeatingPriority = 0, bool requireExplicitCancel = false)
	{
		lastFlashId++;
		if (repeating)
		{
			RepeatingFlash repeatingFlash = new RepeatingFlash
			{
				Colour = flashColour,
				Handle = new FlashHandle(lastFlashId, this),
				Priority = repeatingPriority,
				RequireExplicitCancel = requireExplicitCancel
			};
			repeatingFlash.Routine = StartCoroutine(FlashRoutine(amount, timeUp, stayTime, timeDown, stayDownTime, repeating: true, repeatTimes, repeatingFlash));
			repeatingFlashes.Add(repeatingFlash);
			UpdateCurrentRepeatingFlash();
			return repeatingFlash.Handle;
		}
		CancelSingleFlash();
		singleFlashColour = flashColour;
		if (base.gameObject.activeInHierarchy)
		{
			singleFlashRoutine = StartCoroutine(FlashRoutine(amount, timeUp, stayTime, timeDown, stayDownTime, repeating: false, 0, null));
		}
		singleFlashHandle = new FlashHandle(lastFlashId, this);
		return singleFlashHandle;
	}

	public bool IsFlashing(bool repeating, FlashHandle flashHandle)
	{
		if (repeating)
		{
			foreach (RepeatingFlash repeatingFlash in repeatingFlashes)
			{
				if (repeatingFlash.Handle.Equals(flashHandle))
				{
					return true;
				}
			}
			return false;
		}
		if (singleFlashRoutine != null)
		{
			return flashHandle.Equals(singleFlashHandle);
		}
		return false;
	}

	public bool IsFlashing(int ID)
	{
		foreach (RepeatingFlash repeatingFlash in repeatingFlashes)
		{
			if (repeatingFlash.Handle.ID == ID)
			{
				return true;
			}
		}
		if (singleFlashRoutine != null)
		{
			return singleFlashHandle.ID == ID;
		}
		return false;
	}

	public void CancelFlashByID(int ID)
	{
		CancelSingleFlash(ID);
		CancelRepeatingFlash(ID);
		if (flashChanged)
		{
			flashChanged = false;
			UpdateMaterial();
		}
	}

	public void CancelFlash()
	{
		CancelSingleFlash();
		CancelRepeatingFlash();
		UpdateMaterial();
	}

	public void CancelSingleFlash()
	{
		if (singleFlashRoutine != null)
		{
			StopCoroutine(singleFlashRoutine);
			singleFlashRoutine = null;
		}
		singleFlashAmount = 0f;
		flashChanged = true;
	}

	public void CancelSingleFlash(FlashHandle handle)
	{
		if (singleFlashHandle.Equals(handle))
		{
			CancelSingleFlash();
		}
	}

	public void CancelSingleFlash(int ID)
	{
		if (singleFlashHandle.ID == ID)
		{
			CancelSingleFlash();
		}
	}

	public void CancelRepeatingFlash()
	{
		for (int num = repeatingFlashes.Count - 1; num >= 0; num--)
		{
			RepeatingFlash repeatingFlash = repeatingFlashes[num];
			if (!repeatingFlash.RequireExplicitCancel)
			{
				if (repeatingFlash.Routine != null)
				{
					StopCoroutine(repeatingFlash.Routine);
				}
				repeatingFlashes.RemoveAt(num);
			}
		}
		UpdateCurrentRepeatingFlash();
		flashChanged = true;
	}

	public void CancelRepeatingFlash(FlashHandle handle)
	{
		for (int num = repeatingFlashes.Count - 1; num >= 0; num--)
		{
			RepeatingFlash repeatingFlash = repeatingFlashes[num];
			if (repeatingFlash.Handle.Equals(handle))
			{
				if (repeatingFlash.Routine != null)
				{
					StopCoroutine(repeatingFlash.Routine);
				}
				repeatingFlashes.RemoveAt(num);
			}
		}
		UpdateCurrentRepeatingFlash();
		flashChanged = true;
	}

	public void CancelRepeatingFlash(int ID)
	{
		bool flag = false;
		for (int num = repeatingFlashes.Count - 1; num >= 0; num--)
		{
			RepeatingFlash repeatingFlash = repeatingFlashes[num];
			if (repeatingFlash.Handle.ID == ID)
			{
				if (repeatingFlash.Routine != null)
				{
					StopCoroutine(repeatingFlash.Routine);
				}
				repeatingFlashes.RemoveAt(num);
				flag = true;
			}
		}
		if (flag)
		{
			UpdateCurrentRepeatingFlash();
			flashChanged = true;
		}
	}

	public FlashHandle FlashingSuperDashHandled()
	{
		return Flash(new Color(1f, 1f, 1f), 0.7f, 0.1f, 0.01f, 0.1f, 0f, repeating: true);
	}

	public void FlashingSuperDash()
	{
		FlashingSuperDashHandled();
	}

	public void FlashingGhostWounded()
	{
		Flash(new Color(1f, 1f, 1f), 0.7f, 0.5f, 0.01f, 0.5f);
	}

	public void FlashingWhiteStay()
	{
		Flash(new Color(1f, 1f, 1f), 0.6f, 0.01f, 999f, 0.01f, 0f, repeating: true);
	}

	public void FlashingWhiteStayMoth()
	{
		Flash(new Color(1f, 1f, 1f), 0.6f, 2f, 9999f, 2f, 0f, repeating: true);
	}

	public void FlashingFury()
	{
		Flash(new Color(0.71f, 0.18f, 0.18f), 0.75f, 0.25f, 0.01f, 0.25f, 0f, repeating: true);
	}

	[ContextMenu("Test flashing")]
	public void FlashingOrange()
	{
		Flash(new Color(1f, 0.31f, 0f), 0.7f, 0.1f, 0.01f, 0.1f, 0f, repeating: true);
	}

	public void FlashingWhite()
	{
		Flash(new Color(1f, 1f, 1f), 0.7f, 0.1f, 0.01f, 0.1f, 0f, repeating: true);
	}

	public void FlashingWhiteLong()
	{
		Flash(new Color(1f, 1f, 1f), 0.5f, 0.7f, 0.01f, 0.5f, 0f, repeating: true);
	}

	public void FlashingBomb()
	{
		Flash(new Color(0.99f, 0.89f, 0.09f), 0.7f, 0.3f, 0.1f, 0.3f, 0f, repeating: true);
	}

	public void FlashingBombFast()
	{
		Flash(new Color(0.99f, 0.89f, 0.09f), 0.7f, 0.1f, 0.01f, 0.1f, 0f, repeating: true);
	}

	public void FlashingTarSlug()
	{
		Flash(new Color(1f, 0.5f, 0.24f), 0.7f, 0.1f, 0.01f, 0.1f, 0f, repeating: true);
	}

	public void FlashingAcid()
	{
		Flash(new Color(0.62f, 0.86f, 0.51f), 0.9f, 0.1f, 0.01f, 0.1f, 0f, repeating: true);
	}

	public void FlashingMossExtract()
	{
		Flash(new Color(0.5f, 1f, 0.49f), 0.9f, 0.2f, 0.01f, 0.2f, 0f, repeating: true);
	}

	public void FlashingSwampExtract()
	{
		Flash(new Color(0.73f, 0.74f, 0.39f), 0.9f, 0.2f, 0.01f, 0.2f, 0f, repeating: true);
	}

	public void FlashingBluebloodExtract()
	{
		Flash(new Color(0.55f, 0.9f, 1f), 0.9f, 0.2f, 0.01f, 0.2f, 0f, repeating: true);
	}

	public void FlashingQuickened()
	{
		Flash(new Color(0.99f, 0.77f, 0.24f), 0.5f, 0.15f, 0.01f, 0.15f, 0f, repeating: true);
	}

	public void flashInfected()
	{
		Flash(new Color(1f, 0.31f, 0f), 0.9f, 0.01f, 0.01f, 0.25f);
	}

	public void flashDung()
	{
		Flash(new Color(0.45f, 0.27f, 0f), 0.9f, 0.01f, 0.01f, 0.25f);
	}

	public void flashDungQuick()
	{
		Flash(new Color(0.45f, 0.27f, 0f), 0.75f, 0.001f, 0.05f, 0.1f);
	}

	public void flashSporeQuick()
	{
		Flash(new Color(0.95f, 0.9f, 0.15f), 0.75f, 0.001f, 0.05f, 0.1f);
	}

	public void flashWhiteQuick()
	{
		Flash(new Color(1f, 1f, 1f), 1f, 0.001f, 0.05f, 0.001f);
	}

	public void FlashExtraRosary()
	{
		Flash(new Color(1f, 1f, 1f), 1f, 0.001f, 0.5f, 0.4f);
	}

	public void flashInfectedLong()
	{
		Flash(new Color(1f, 0.31f, 0f), 0.9f, 0.01f, 0.25f, 0.35f);
	}

	public void flashArmoured()
	{
		Flash(new Color(1f, 1f, 1f), 0.9f, 0.01f, 0.01f, 0.25f);
	}

	public void flashBenchRest()
	{
		Flash(new Color(1f, 1f, 1f), 0.7f, 0.01f, 0.1f, 0.75f);
	}

	public void flashDreamImpact()
	{
		Flash(new Color(1f, 1f, 1f), 0.9f, 0.01f, 0.25f, 0.75f);
	}

	public void flashMothDepart()
	{
		Flash(new Color(1f, 1f, 1f), 0.75f, 1.9f, 1f, 0.25f);
	}

	public void flashSoulGet()
	{
		Flash(new Color(1f, 1f, 1f), 0.5f, 0.01f, 0.01f, 0.25f);
	}

	public void flashShadeGet()
	{
		Flash(new Color(0f, 0f, 0f), 0.5f, 0.01f, 0.01f, 0.25f);
	}

	public void flashWhiteLong()
	{
		Flash(new Color(1f, 1f, 1f), 1f, 0.01f, 0.01f, 2f);
	}

	public void flashOvercharmed()
	{
		Flash(new Color(0.72f, 0.376f, 0.72f), 0.75f, 0.2f, 0.01f, 0.5f);
	}

	public void flashFocusHeal()
	{
		Flash(new Color(1f, 1f, 1f), 0.85f, 0.01f, 0.01f, 0.35f);
	}

	public void FlashEnemyHit(HitInstance hitInstance)
	{
		if (hitInstance.AttackType == AttackTypes.Coal)
		{
			FlashCoal();
		}
		else
		{
			FlashEnemyHit();
		}
	}

	public void FlashEnemyHit()
	{
		FlashEnemyHit(Color.white);
	}

	public void FlashEnemyHitRage()
	{
		FlashEnemyHit(new Color(1f, 0.6f, 0.6f));
	}

	public void FlashEnemyHit(Color color)
	{
		Flash(color, 0.85f, 0f, 0.07f, 0.05f);
	}

	public void flashFocusGet()
	{
		Flash(new Color(1f, 1f, 1f), 0.5f, 0.01f, 0.01f, 0.35f);
	}

	public void flashFocusGetQuick()
	{
		Flash(new Color(1f, 1f, 1f), 0.5f, 0.01f, 0.01f, 0.1f);
	}

	public void flashWhitePulse()
	{
		Flash(new Color(1f, 1f, 1f), 0.35f, 0.5f, 0.01f, 0.75f);
	}

	public void flashHealBlue()
	{
		Flash(new Color(0.55f, 0.9f, 1f), 0.85f, 0.01f, 0.01f, 0.5f);
	}

	public void flashHealPoison()
	{
		Flash(new Color(0.56f, 0.39f, 0.85f), 0.85f, 0.01f, 0.01f, 0.5f);
	}

	public void FlashShadowRecharge()
	{
		Flash(new Color(0f, 0f, 0f), 0.75f, 0.01f, 0.01f, 0.35f);
	}

	public void FlashLavaBellRecharge()
	{
		Flash(new Color(73f / 85f, 0.32156864f, 1f / 3f), 0.75f, 0.01f, 0.01f, 0.35f);
	}

	public void flashInfectedLoop()
	{
		Flash(new Color(1f, 0.31f, 0f), 0.9f, 0.2f, 0.01f, 0.2f, 0f, repeating: true);
	}

	public void FlashGrimmflame()
	{
		Flash(new Color(1f, 0.25f, 0.25f), 0.75f, 0.01f, 0.01f, 1f);
	}

	public void FlashCoal()
	{
		Flash(new Color(1f, 0.55f, 0.1f), 0.75f, 0.01f, 0.01f, 0.2f);
	}

	public void FlashAcid()
	{
		Flash(new Color(0.62f, 0.86f, 0.51f), 0.75f, 0.2f, 0.01f, 0.2f);
	}

	public void FlashGrimmHit()
	{
		Flash(new Color(1f, 0.25f, 0.25f), 0.75f, 0.01f, 0.01f, 0.25f);
	}

	public void FlashBossFinalHit()
	{
		Flash(new Color(1f, 1f, 1f), 0.8f, 0.01f, 0.1f, 0.25f);
	}

	public void FlashDazzleQuick()
	{
		Flash(new Color(1f, 0.7f, 1f), 0.5f, 0.001f, 0.05f, 0.1f);
	}

	public void FlashWitchPoison()
	{
		Flash(Effects.EnemyWitchPoisonBloodBurst.Colour, 0.7f, 0.001f, 0.05f, 0.1f);
	}

	public void FlashZapExplosion()
	{
		Flash(new Color(0.96f, 0.37f, 0.92f), 0.75f, 0.01f, 0.1f, 1f);
	}

	public FlashHandle FlashingFrosted()
	{
		return Flash(new Color(0.6f, 0.8f, 1f), 0.5f, 0.2f, 0.01f, 0.3f, 1f, repeating: true, 0, 0, requireExplicitCancel: true);
	}

	public FlashHandle FlashingFrostAntic()
	{
		return Flash(new Color(0.6f, 0.8f, 1f), 0.65f, 0.15f, 0.01f, 0.2f, 0f, repeating: true, 0, 2);
	}

	public FlashHandle FlashingMaggot()
	{
		return Flash(Effects.MossEffectsTintDust, 0.5f, 0.2f, 0.01f, 0.3f, 1f, repeating: true, 0, 0, requireExplicitCancel: true);
	}

	public FlashHandle FlashingMaggotNPC()
	{
		return Flash(Effects.MossEffectsTintDust, 0.5f, 0.2f, 0.01f, 0.3f, 1f, repeating: true);
	}

	private void SetPropertyBlock(MaterialPropertyBlock setBlock)
	{
		if ((bool)renderer)
		{
			renderer.SetPropertyBlock(setBlock);
		}
	}

	private void GetPropertyBlock(MaterialPropertyBlock getBlock)
	{
		if ((bool)renderer)
		{
			renderer.GetPropertyBlock(getBlock);
		}
	}

	private IEnumerator FlashRoutine(float amount, float timeUp, float stayTime, float timeDown, float stayDownTime, bool repeating, int repeatTimes, RepeatingFlash repeatingFlash)
	{
		bool hadRepeatTimes = repeatTimes > 0;
		do
		{
			repeatTimes--;
			for (float elapsed = 0f; elapsed < timeUp; elapsed += Time.deltaTime)
			{
				float t = elapsed / timeUp;
				float amount2 = Mathf.Lerp(0f, amount, t);
				if (repeating)
				{
					repeatingFlash.Amount = amount2;
				}
				else
				{
					singleFlashAmount = amount2;
				}
				flashChanged = true;
				yield return null;
			}
			if (repeating)
			{
				repeatingFlash.Amount = amount;
			}
			else
			{
				singleFlashAmount = amount;
			}
			flashChanged = true;
			yield return null;
			if (stayTime > 0f)
			{
				yield return new WaitForSeconds(stayTime);
			}
			else
			{
				yield return null;
			}
			for (float elapsed = 0f; elapsed < timeDown; elapsed += Time.deltaTime)
			{
				float t2 = elapsed / timeDown;
				float amount3 = Mathf.Lerp(amount, 0f, t2);
				if (repeating)
				{
					repeatingFlash.Amount = amount3;
				}
				else
				{
					singleFlashAmount = amount3;
				}
				flashChanged = true;
				yield return null;
			}
			if (repeating)
			{
				repeatingFlash.Amount = 0f;
			}
			else
			{
				singleFlashAmount = 0f;
			}
			flashChanged = true;
			yield return null;
			if (repeating && stayDownTime > 0f)
			{
				yield return new WaitForSeconds(stayDownTime);
			}
		}
		while (repeating && (!hadRepeatTimes || repeatTimes >= 0));
		singleFlashRoutine = null;
	}

	private void UpdateCurrentRepeatingFlash()
	{
		if (repeatingFlashes.Count <= 0)
		{
			currentRepeatingFlash = null;
			return;
		}
		int num = int.MinValue;
		foreach (RepeatingFlash repeatingFlash in repeatingFlashes)
		{
			if (repeatingFlash.Priority > num)
			{
				num = repeatingFlash.Priority;
				currentRepeatingFlash = repeatingFlash;
			}
		}
	}
}
